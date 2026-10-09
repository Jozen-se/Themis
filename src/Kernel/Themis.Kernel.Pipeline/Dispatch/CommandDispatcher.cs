using System.Data.Common;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Themis.Kernel.Contracts.Commands;
using Themis.Kernel.Contracts.Persistence;
using Themis.Kernel.Contracts.Security;
using Themis.Kernel.Pipeline.Http;
using Themis.Kernel.Pipeline.Json;
using Themis.Kernel.Pipeline.Telemetry;
using Themis.Kernel.Primitives;

namespace Themis.Kernel.Pipeline.Dispatch;

/// <summary>
/// Runs one command through the kernel pipeline (brief, One command, one transaction):
/// <list type="number">
/// <item>Principal and permission TYPE.COMMAND (R-SEC-01); validation by the command's validators.</item>
/// <item>One connection as themis_app, one transaction; every statement switches to its owning role (D-41, R-DB-01).</item>
/// <item>A transaction-scoped advisory lock on (user, idempotency key): a concurrent duplicate fails to take it and
/// gets 409 at once, without waiting on the first request (R-API-01).</item>
/// <item>A stored record with the same request hash is replayed; a different hash is rejected (R-API-01, R-API-04).</item>
/// <item>The handler runs inside a savepoint. A rejection rolls back to the savepoint, so the business writes
/// disappear while the transaction stays open to store the rejection as the key's result (R-API-03). Not stored:
/// authorisation and validation outcomes, the 409 duplicate, a missing If-Match (428) and a stale version (412),
/// so a retry with the fresh version runs afresh (R-API-02; owner's conditions on pull request 5).</item>
/// <item>On success the buffered audit events and outbox rows are written as themis_kernel (R-AUD-01, D-44).</item>
/// <item>Only SQLSTATE 40001 and 40P01 restart the whole pipeline, authorisation included, at most twice, each retry
/// counted (D-41). Any other exception rolls everything back and stores nothing.</item>
/// </list>
/// </summary>
internal sealed partial class CommandDispatcher : ICommandDispatcher
{
    private const string BusinessSavepoint = "business";
    private const string SerializationFailure = "40001";
    private const string DeadlockDetected = "40P01";

    private readonly IServiceProvider _services;
    private readonly ICurrentPrincipal _currentPrincipal;
    private readonly IPermissionEvaluator _permissions;
    private readonly IUnitOfWorkFactory _unitOfWorkFactory;
    private readonly IIdempotencyStore _idempotency;
    private readonly IAuditStore _auditStore;
    private readonly IOutboxStore _outboxStore;
    private readonly TimeProvider _clock;
    private readonly PipelineTelemetry _telemetry;
    private readonly KernelPipelineOptions _options;
    private readonly ILogger<CommandDispatcher> _logger;

    public CommandDispatcher(
        IServiceProvider services,
        ICurrentPrincipal currentPrincipal,
        IPermissionEvaluator permissions,
        IUnitOfWorkFactory unitOfWorkFactory,
        IIdempotencyStore idempotency,
        IAuditStore auditStore,
        IOutboxStore outboxStore,
        TimeProvider clock,
        PipelineTelemetry telemetry,
        IOptions<KernelPipelineOptions> options,
        ILogger<CommandDispatcher> logger)
    {
        _services = services;
        _currentPrincipal = currentPrincipal;
        _permissions = permissions;
        _unitOfWorkFactory = unitOfWorkFactory;
        _idempotency = idempotency;
        _auditStore = auditStore;
        _outboxStore = outboxStore;
        _clock = clock;
        _telemetry = telemetry;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<CommandResponse> DispatchAsync<TCommand, TResult>(TCommand command, CommandEnvelope envelope, CancellationToken cancellationToken)
        where TCommand : ICommand<TResult>
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(envelope);

        CommandMetadata metadata = CommandMetadata.For(typeof(TCommand));
        Principal? principal = _currentPrincipal.Current;
        string userId = principal?.UserId.ToString() ?? "anonymous";

        // Every log line of the command carries its name, correlation id and user (D-25).
        using IDisposable? scope = _logger.BeginScope(new Dictionary<string, object>
        {
            ["CommandName"] = metadata.Name,
            ["CorrelationId"] = envelope.CorrelationId.Value,
            ["UserId"] = userId,
        });
        using Activity? activity = _telemetry.Source.StartActivity($"command {metadata.Name}");
        activity?.SetTag("themis.command", metadata.Name);
        activity?.SetTag("themis.correlation_id", envelope.CorrelationId.Value);
        activity?.SetTag("themis.user_id", userId);

        CommandResponse response = await RunAsync<TCommand, TResult>(command, envelope, metadata, principal, cancellationToken);
        _telemetry.Commands.Add(1, new KeyValuePair<string, object?>("command", metadata.Name), new KeyValuePair<string, object?>("outcome", Outcome(response)));
        activity?.SetTag("http.response.status_code", response.StatusCode);
        activity?.SetTag("themis.replayed", response.Replayed);
        return response;
    }

    private async Task<CommandResponse> RunAsync<TCommand, TResult>(TCommand command, CommandEnvelope envelope, CommandMetadata metadata, Principal? principal, CancellationToken cancellationToken)
        where TCommand : ICommand<TResult>
    {
        if (principal is null)
        {
            return ProblemDetailsBody.Http(StatusCodes.Status401Unauthorized, "authentication_required", "R-SEC-01", "The command requires a signed-in user.", envelope.CorrelationId);
        }

        if (envelope.IdempotencyKey is not { } idempotencyKey)
        {
            return ProblemDetailsBody.Http(StatusCodes.Status400BadRequest, "idempotency_key_required", "R-API-01", "Every command carries an Idempotency-Key header.", envelope.CorrelationId);
        }

        for (int attempt = 1; ; attempt++)
        {
            try
            {
                return await AttemptAsync<TCommand, TResult>(command, envelope, metadata, principal, idempotencyKey, cancellationToken);
            }
            catch (DbException exception) when (IsRetryable(exception) && attempt < KernelPipelineOptions.MaxAttempts)
            {
                // D-41: the whole command restarts in a new transaction; nothing of the failed attempt survives.
                _telemetry.Retries.Add(1, new KeyValuePair<string, object?>("command", metadata.Name), new KeyValuePair<string, object?>("sqlstate", exception.SqlState));
                LogRetrying(_logger, exception, metadata.Name, attempt, exception.SqlState);
            }
        }
    }

    private async Task<CommandResponse> AttemptAsync<TCommand, TResult>(TCommand command, CommandEnvelope envelope, CommandMetadata metadata, Principal principal, IdempotencyKey idempotencyKey, CancellationToken cancellationToken)
        where TCommand : ICommand<TResult>
    {
        // R-SEC-01: authorisation runs on every attempt, before any statement.
        if (!await _permissions.HasPermissionAsync(principal, metadata.Name, cancellationToken))
        {
            return ProblemDetailsBody.ToResponse(Error.Forbidden("permission_missing", $"The user lacks permission {metadata.Name}."), envelope.CorrelationId);
        }

        List<Error> validationErrors = [.. _services.GetServices<ICommandValidator<TCommand>>().SelectMany(validator => validator.Validate(command))];
        if (validationErrors.Count > 0)
        {
            return ProblemDetailsBody.ToResponse(validationErrors, envelope.CorrelationId);
        }

        // R-API-04: the fingerprint covers the command type, the body and the If-Match version.
        string requestHash = RequestHash.Compute(command, (command as IVersionedCommand)?.ExpectedVersion);
        DateTimeOffset now = _clock.GetUtcNow();

        await using IKernelUnitOfWork unitOfWork = await _unitOfWorkFactory.BeginAsync(cancellationToken);

        // R-API-01: a concurrent duplicate cannot take the lock and is answered at once with 409; nothing is stored,
        // so the client retries after the first request completed and then receives the replay.
        if (!await unitOfWork.TryAdvisoryLockAsync(AdvisoryLockKey.For(principal.UserId, idempotencyKey), cancellationToken))
        {
            await unitOfWork.RollbackAsync(cancellationToken);
            return ProblemDetailsBody.ToResponse(Error.Conflict("idempotency_key_in_progress", "A request with this Idempotency-Key is still running.", "R-API-01"), envelope.CorrelationId);
        }

        StoredIdempotencyRecord? stored = await _idempotency.FindAsync(unitOfWork, principal.UserId, idempotencyKey, cancellationToken);
        if (stored is not null)
        {
            await unitOfWork.RollbackAsync(cancellationToken);
            if (!string.Equals(stored.RequestHash, requestHash, StringComparison.Ordinal))
            {
                // R-API-04: the same key with a different request is a client bug, reported and never stored.
                return ProblemDetailsBody.ToResponse(Error.Rule("R-API-04", "idempotency_key_reused", "The Idempotency-Key was already used for a different request."), envelope.CorrelationId);
            }

            LogReplayed(_logger, metadata.Name);
            return stored.Response with { Replayed = true };
        }

        CommandContext context = new(CommandId.New(), envelope.CorrelationId, principal, idempotencyKey, now, unitOfWork);
        ICommandHandler<TCommand, TResult> handler = _services.GetRequiredService<ICommandHandler<TCommand, TResult>>();

        await unitOfWork.SavepointAsync(BusinessSavepoint, cancellationToken);
        Result<TResult> result = await handler.HandleAsync(command, context, cancellationToken);

        CommandResponse response;
        if (result.IsSuccess)
        {
            await unitOfWork.ReleaseSavepointAsync(BusinessSavepoint, cancellationToken);
            await _auditStore.AddAsync(unitOfWork, context, metadata.Name, context.AuditEntries, cancellationToken);
            await _outboxStore.AddAsync(unitOfWork, now, context.OutboxEntries, cancellationToken);
            response = new CommandResponse(StatusCodes.Status200OK, CommandResponse.JsonContentType, JsonSerializer.Serialize(result.Value, ThemisJson.Options), Replayed: false);
        }
        else
        {
            response = ProblemDetailsBody.ToResponse(result.Errors, envelope.CorrelationId);
            LogRejected(_logger, metadata.Name, result.Primary!.Code, result.Primary.RuleId);
            if (result.Errors.Any(error => error.Kind == ErrorKind.PreconditionFailed))
            {
                // R-API-02: a stale version is not the key's result. Nothing is stored, so the client's retry with
                // the same key and the fresh version runs the command (owner's condition on pull request 5).
                await unitOfWork.RollbackAsync(cancellationToken);
                return response;
            }

            // R-API-03 and R-API-01: the rejection is the key's result. The business writes roll back to the
            // savepoint; the transaction stays open so that the record below commits.
            await unitOfWork.RollbackToSavepointAsync(BusinessSavepoint, cancellationToken);
            context.Discard();
        }

        await _idempotency.AddAsync(unitOfWork, new StoredIdempotencyRecord(idempotencyKey, principal.UserId, metadata.Name, requestHash, response, now + _options.IdempotencyRetention), cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        return response;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Command {CommandName} replayed from its idempotency record (R-API-01).")]
    private static partial void LogReplayed(ILogger logger, string commandName);

    [LoggerMessage(Level = LogLevel.Information, Message = "Command {CommandName} rejected with {Code} ({RuleId}).")]
    private static partial void LogRejected(ILogger logger, string commandName, string code, string? ruleId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Command {CommandName} attempt {Attempt} failed with SQLSTATE {SqlState}; retrying the whole command (D-41).")]
    private static partial void LogRetrying(ILogger logger, Exception exception, string commandName, int attempt, string? sqlState);

    private static bool IsRetryable(DbException exception) => exception.SqlState is SerializationFailure or DeadlockDetected;

    private static string Outcome(CommandResponse response) => response.Replayed ? "replayed" : response.IsSuccess ? "success" : "rejected";
}
