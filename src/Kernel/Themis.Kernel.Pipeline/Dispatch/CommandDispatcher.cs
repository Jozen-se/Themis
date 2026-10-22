using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Themis.Kernel.Contracts.Commands;
using Themis.Kernel.Contracts.Persistence;
using Themis.Kernel.Contracts.Security;
using Themis.Kernel.Contracts.Settings;
using Themis.Kernel.Pipeline.Http;
using Themis.Kernel.Pipeline.Json;
using Themis.Kernel.Pipeline.Telemetry;
using Themis.Kernel.Primitives;

namespace Themis.Kernel.Pipeline.Dispatch;

/// <summary>
/// Runs one command through the kernel pipeline (brief, One command, one transaction):
/// <list type="number">
/// <item>A signed-in subject (401) and an Idempotency-Key (400).</item>
/// <item>One connection as themis_app, one transaction; every statement switches to its owning role (D-41, R-DB-01).</item>
/// <item>Inside the transaction, before any business statement (Build Brief, Authorisation): the subject's User row
/// becomes the principal (403 subject_unknown, user_disabled); then the command's validators run (400, pure, never
/// stored; after the user so that an unknown subject learns nothing from a validation answer, RR-51); then the
/// command names its scope from its own data or from the record it loads (404, 403 scope_undeclared), and the
/// principal must hold the permission TYPE.COMMAND through an assignment covering that scope and valid on the
/// company's business date (403 permission_missing, out_of_scope); all under R-SEC-01, none of them stored.</item>
/// <item>A transaction-scoped advisory lock on (user, idempotency key): a concurrent duplicate fails to take it and
/// gets 409 at once, without waiting on the first request (R-API-01).</item>
/// <item>A stored record with the same request hash is replayed; a different hash is rejected (R-API-01, R-API-04).
/// A record whose expiry has passed counts as absent: it is deleted under the same advisory lock and the key is
/// processed as new; a failure, a 412 or a retry rolls the delete back with the rest (RR-42).</item>
/// <item>The handler runs inside a savepoint. A rejection rolls back to the savepoint, so the business writes
/// disappear while the transaction stays open to store the rejection as the key's result (R-API-03). A unique
/// violation of a registered business-key constraint (RR-45) and a numeric overflow (RR-46) raised inside the
/// handler are rejections too, mapped and stored the same way; any other database failure is an error. Not stored:
/// authorisation and validation outcomes, the 409 duplicate, a missing If-Match (428) and a stale version (412),
/// so a retry with the fresh version runs afresh (R-API-02; owner's conditions on pull request 5).</item>
/// <item>On success the buffered audit events and outbox rows are written as themis_kernel (R-AUD-01, D-44); the
/// pipeline fills the envelope's command name and correlation id (D-50).</item>
/// <item>Only SQLSTATE 40001 and 40P01, wherever they sit in the exception chain (RR-40), restart the whole pipeline,
/// authorisation included, at most twice, each retry counted (D-41). Any other exception rolls everything back and
/// stores nothing.</item>
/// </list>
/// </summary>
internal sealed partial class CommandDispatcher : ICommandDispatcher
{
    private const string BusinessSavepoint = "business";

    private readonly IServiceProvider _services;
    private readonly ICurrentSubject _currentSubject;
    private readonly IAccessControl _accessControl;
    private readonly IUnitOfWorkFactory _unitOfWorkFactory;
    private readonly IIdempotencyStore _idempotency;
    private readonly IAuditStore _auditStore;
    private readonly IOutboxStore _outboxStore;
    private readonly TimeProvider _clock;
    private readonly PipelineTelemetry _telemetry;
    private readonly ISettingsReader _settings;
    private readonly IReadOnlyList<UniqueConstraintRule> _uniqueRules;
    private readonly ILogger<CommandDispatcher> _logger;

    public CommandDispatcher(
        IServiceProvider services,
        ICurrentSubject currentSubject,
        IAccessControl accessControl,
        IUnitOfWorkFactory unitOfWorkFactory,
        IIdempotencyStore idempotency,
        IAuditStore auditStore,
        IOutboxStore outboxStore,
        TimeProvider clock,
        PipelineTelemetry telemetry,
        ISettingsReader settings,
        IEnumerable<UniqueConstraintRule> uniqueRules,
        ILogger<CommandDispatcher> logger)
    {
        _services = services;
        _currentSubject = currentSubject;
        _accessControl = accessControl;
        _unitOfWorkFactory = unitOfWorkFactory;
        _idempotency = idempotency;
        _auditStore = auditStore;
        _outboxStore = outboxStore;
        _clock = clock;
        _telemetry = telemetry;
        _settings = settings;
        _uniqueRules = [.. uniqueRules];
        _logger = logger;
    }

    public async Task<CommandResponse> DispatchAsync<TCommand, TResult>(TCommand command, CommandEnvelope envelope, CancellationToken cancellationToken)
        where TCommand : ICommand<TResult>
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(envelope);

        CommandMetadata metadata = CommandMetadata.For(typeof(TCommand));
        AuthenticatedSubject? subject = _currentSubject.Current;
        if (envelope.CorrelationId is null)
        {
            // D-25: a caller outside HTTP gets a correlation id from the pipeline, so audit events always carry one.
            envelope = envelope with { CorrelationId = CorrelationIds.Fresh() };
        }

        CorrelationId correlationId = envelope.CorrelationId.Value;

        // Every log line of the command carries its name, correlation id and subject (D-25); the user id joins once resolved.
        using IDisposable? scope = _logger.BeginScope(new Dictionary<string, object>
        {
            ["CommandName"] = metadata.Name,
            ["CorrelationId"] = correlationId.Value,
            ["Subject"] = subject?.Subject ?? "anonymous",
        });
        using Activity? activity = _telemetry.Source.StartActivity($"command {metadata.Name}");
        activity?.SetTag("themis.command", metadata.Name);
        activity?.SetTag("themis.correlation_id", correlationId.Value);

        CommandResponse response = await RunAsync<TCommand, TResult>(command, envelope, correlationId, metadata, subject, cancellationToken);
        _telemetry.Commands.Add(1, new KeyValuePair<string, object?>("command", metadata.Name), new KeyValuePair<string, object?>("outcome", Outcome(response)));
        activity?.SetTag("http.response.status_code", response.StatusCode);
        activity?.SetTag("themis.replayed", response.Replayed);
        return response;
    }

    private async Task<CommandResponse> RunAsync<TCommand, TResult>(TCommand command, CommandEnvelope envelope, CorrelationId correlationId, CommandMetadata metadata, AuthenticatedSubject? subject, CancellationToken cancellationToken)
        where TCommand : ICommand<TResult>
    {
        if (subject is null)
        {
            return ProblemDetailsBody.Http(StatusCodes.Status401Unauthorized, "authentication_required", AccessDenied.RuleId, "The command requires a signed-in user.", correlationId);
        }

        if (envelope.IdempotencyKey is not { } idempotencyKey)
        {
            return ProblemDetailsBody.Http(StatusCodes.Status400BadRequest, "idempotency_key_required", "R-API-01", "Every command carries an Idempotency-Key header.", correlationId);
        }

        for (int attempt = 1; ; attempt++)
        {
            try
            {
                return await AttemptAsync<TCommand, TResult>(command, correlationId, metadata, subject, idempotencyKey, cancellationToken);
            }
            catch (Exception exception) when (DatabaseErrors.IsRetryable(exception) && attempt < KernelPipelineOptions.MaxAttempts)
            {
                // D-41: the whole command restarts in a new transaction; nothing of the failed attempt survives. The SQLSTATE
                // is read wherever it sits in the chain, since Entity Framework wraps the provider's exception (RR-40).
                string? sqlState = DatabaseErrors.FindSqlState(exception);
                _telemetry.Retries.Add(1, new KeyValuePair<string, object?>("command", metadata.Name), new KeyValuePair<string, object?>("sqlstate", sqlState));
                LogRetrying(_logger, exception, metadata.Name, attempt, sqlState);
            }
        }
    }

    private async Task<CommandResponse> AttemptAsync<TCommand, TResult>(TCommand command, CorrelationId correlationId, CommandMetadata metadata, AuthenticatedSubject subject, IdempotencyKey idempotencyKey, CancellationToken cancellationToken)
        where TCommand : ICommand<TResult>
    {
        // R-API-04: the fingerprint covers the command type, the body and the If-Match version.
        string requestHash = RequestHash.Compute(command, (command as IVersionedCommand)?.ExpectedVersion);
        DateTimeOffset now = _clock.GetUtcNow();

        await using IKernelUnitOfWork unitOfWork = await _unitOfWorkFactory.BeginAsync(cancellationToken);

        // R-SEC-01: the subject's User row, on every attempt, before any business statement (D-41).
        Result<Principal> resolved = await _accessControl.ResolvePrincipalAsync(unitOfWork, subject, cancellationToken);
        if (resolved.IsFailure)
        {
            await unitOfWork.RollbackAsync(cancellationToken);
            LogSubjectRefused(_logger, metadata.Name, subject.Subject, resolved.Primary!.Code);
            return ProblemDetailsBody.ToResponse(resolved.Errors, correlationId);
        }

        Principal principal = resolved.Value;
        using IDisposable? userScope = _logger.BeginScope(new Dictionary<string, object> { ["UserId"] = principal.UserId.ToString() });
        Activity.Current?.SetTag("themis.user_id", principal.UserId.ToString());

        // RR-51: validators run after the user is known and before any business statement; they are pure, run on every
        // attempt (D-41) and their outcome is never stored (R-API-03).
        List<Error> validationErrors = [.. _services.GetServices<ICommandValidator<TCommand>>().SelectMany(validator => validator.Validate(command))];
        if (validationErrors.Count > 0)
        {
            await unitOfWork.RollbackAsync(cancellationToken);
            return ProblemDetailsBody.ToResponse(validationErrors, correlationId);
        }

        // D-47: the command names its scope from its own data or the record it loads; a missing record is a 404.
        Result<RequestScope> scope = await ScopeSources.ResolveAsync(command, unitOfWork, _services, cancellationToken);
        if (scope.IsFailure)
        {
            await unitOfWork.RollbackAsync(cancellationToken);
            LogRejected(_logger, metadata.Name, scope.Primary!.Code, scope.Primary.RuleId);
            return ProblemDetailsBody.ToResponse(scope.Errors, correlationId);
        }

        // R-SEC-01: permission and scope, inside the transaction, before any business logic; never stored.
        Result authorised = await _accessControl.AuthorizeAsync(unitOfWork, principal, metadata.Name, scope.Value, now, cancellationToken);
        if (authorised.IsFailure)
        {
            await unitOfWork.RollbackAsync(cancellationToken);
            LogRejected(_logger, metadata.Name, authorised.Primary!.Code, authorised.Primary.RuleId);
            return ProblemDetailsBody.ToResponse(authorised.Errors, correlationId);
        }

        // R-API-01: a concurrent duplicate cannot take the lock and is answered at once with 409; nothing is stored,
        // so the client retries after the first request completed and then receives the replay.
        if (!await unitOfWork.TryAdvisoryLockAsync(AdvisoryLockKey.For(principal.UserId, idempotencyKey), cancellationToken))
        {
            await unitOfWork.RollbackAsync(cancellationToken);
            return ProblemDetailsBody.ToResponse(Error.Conflict("idempotency_key_in_progress", "A request with this Idempotency-Key is still running.", "R-API-01"), correlationId);
        }

        StoredIdempotencyRecord? stored = await _idempotency.FindAsync(unitOfWork, principal.UserId, idempotencyKey, cancellationToken);
        if (stored is not null && stored.ExpiresAt <= now)
        {
            // RR-42: an expired record counts as absent. It is removed here, under the advisory lock and in this transaction,
            // and the key is processed as new; a rejection that is not stored, a failure or a retry rolls the removal back.
            await _idempotency.DeleteAsync(unitOfWork, principal.UserId, idempotencyKey, cancellationToken);
            LogExpiredRecordRemoved(_logger, metadata.Name);
            stored = null;
        }

        if (stored is not null)
        {
            await unitOfWork.RollbackAsync(cancellationToken);
            if (!string.Equals(stored.RequestHash, requestHash, StringComparison.Ordinal))
            {
                // R-API-04: the same key with a different request is a client bug, reported and never stored.
                return ProblemDetailsBody.ToResponse(Error.Rule("R-API-04", "idempotency_key_reused", "The Idempotency-Key was already used for a different request."), correlationId);
            }

            LogReplayed(_logger, metadata.Name);
            return stored.Response with { Replayed = true };
        }

        CommandContext context = new(CommandId.New(), correlationId, principal, idempotencyKey, now, unitOfWork);
        ICommandHandler<TCommand, TResult> handler = _services.GetRequiredService<ICommandHandler<TCommand, TResult>>();

        await unitOfWork.SavepointAsync(BusinessSavepoint, cancellationToken);
        Result<TResult> result;
        try
        {
            result = await handler.HandleAsync(command, context, cancellationToken);
        }
        catch (Exception exception) when (DatabaseErrors.MapRejection(exception, _uniqueRules) is { } mapped)
        {
            // RR-45 and RR-46: a unique violation of a mapped business key, or a numeric overflow, raised inside the handler
            // is the command's rejection, not an error. Caught around the handler only, never around the commit; the
            // rollback to the savepoint below clears the aborted state and the rejection is stored like any other.
            LogMappedDatabaseRejection(_logger, metadata.Name, mapped.Exception.SqlState, mapped.Exception.ConstraintName, mapped.Error.Code, mapped.Error.RuleId);
            result = Result<TResult>.Failure([mapped.Error]);
        }

        CommandResponse response;
        if (result.IsSuccess)
        {
            await unitOfWork.ReleaseSavepointAsync(BusinessSavepoint, cancellationToken);
            await _auditStore.AddAsync(unitOfWork, context, metadata.Name, context.AuditEntries, cancellationToken);
            await _outboxStore.AddAsync(unitOfWork, context, metadata.Name, context.OutboxEntries, cancellationToken);
            // R-API-02: every success returns the record's new version, rendered as the ETag header.
            response = new CommandResponse(StatusCodes.Status200OK, CommandResponse.JsonContentType, JsonSerializer.Serialize(result.Value, ThemisJson.Options), Replayed: false, (result.Value as IVersionedResult)?.Version);
        }
        else
        {
            response = ProblemDetailsBody.ToResponse(result.Errors, correlationId);
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

        // R-API-01, D-58: the retention is the installation setting idempotency.retention_days, read in this transaction and
        // stamped on the record; a later change of the setting leaves existing records as they are.
        int retentionDays = await _settings.GetIntegerAsync(unitOfWork, SettingKeys.IdempotencyRetentionDays, SettingTarget.Installation, cancellationToken);
        await _idempotency.AddAsync(unitOfWork, new StoredIdempotencyRecord(idempotencyKey, principal.UserId, metadata.Name, requestHash, response, now + TimeSpan.FromDays(retentionDays)), cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        return response;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Command {CommandName} replayed from its idempotency record (R-API-01).")]
    private static partial void LogReplayed(ILogger logger, string commandName);

    [LoggerMessage(Level = LogLevel.Information, Message = "Command {CommandName} rejected with {Code} ({RuleId}).")]
    private static partial void LogRejected(ILogger logger, string commandName, string code, string? ruleId);

    // Build Brief, Authorisation: an unknown subject or a disabled user cannot have an audit event, so the attempt is logged.
    [LoggerMessage(Level = LogLevel.Warning, Message = "Command {CommandName} refused for subject {Subject}: {Code} (R-SEC-01).")]
    private static partial void LogSubjectRefused(ILogger logger, string commandName, string subject, string code);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Command {CommandName} attempt {Attempt} failed with SQLSTATE {SqlState}; retrying the whole command (D-41).")]
    private static partial void LogRetrying(ILogger logger, Exception exception, string commandName, int attempt, string? sqlState);

    [LoggerMessage(Level = LogLevel.Information, Message = "Command {CommandName} found an expired idempotency record for its key; the record is removed and the key processed as new (RR-42).")]
    private static partial void LogExpiredRecordRemoved(ILogger logger, string commandName);

    [LoggerMessage(Level = LogLevel.Information, Message = "Command {CommandName} hit SQLSTATE {SqlState} on {Constraint} inside the handler, mapped to {Code} ({RuleId}) and stored as its rejection (RR-45, RR-46).")]
    private static partial void LogMappedDatabaseRejection(ILogger logger, string commandName, string sqlState, string? constraint, string code, string? ruleId);

    private static string Outcome(CommandResponse response) => response.Replayed ? "replayed" : response.IsSuccess ? "success" : "rejected";
}
