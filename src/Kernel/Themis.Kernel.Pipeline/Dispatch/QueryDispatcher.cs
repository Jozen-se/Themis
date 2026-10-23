using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Themis.Kernel.Contracts.Commands;
using Themis.Kernel.Contracts.Persistence;
using Themis.Kernel.Contracts.Queries;
using Themis.Kernel.Contracts.Security;
using Themis.Kernel.Pipeline.Http;
using Themis.Kernel.Pipeline.Json;
using Themis.Kernel.Pipeline.Telemetry;
using Themis.Kernel.Primitives;

namespace Themis.Kernel.Pipeline.Dispatch;

/// <summary>
/// Runs a query (P-04: GET only): a signed-in subject (401), then inside one read-only unit of work rolled back at
/// the end (R-DB-01): the subject's User row (403 subject_unknown, user_disabled), the query's scope from its own
/// data or the record it names (403 scope_undeclared), and the permission TYPE.READ in that scope (403
/// permission_missing, out_of_scope), all under R-SEC-01 and D-47; then the handler's result as JSON or problem
/// details (R-API-03). The role is reset to themis_app immediately before the scope resolver and the handler, as in
/// the command dispatcher (RR-62). No idempotency, audit or outbox: a query changes nothing.
/// </summary>
internal sealed partial class QueryDispatcher : IQueryDispatcher
{
    private readonly IServiceProvider _services;
    private readonly ICurrentSubject _currentSubject;
    private readonly IAccessControl _accessControl;
    private readonly IUnitOfWorkFactory _unitOfWorkFactory;
    private readonly TimeProvider _clock;
    private readonly PipelineTelemetry _telemetry;
    private readonly ILogger<QueryDispatcher> _logger;

    public QueryDispatcher(IServiceProvider services, ICurrentSubject currentSubject, IAccessControl accessControl, IUnitOfWorkFactory unitOfWorkFactory, TimeProvider clock, PipelineTelemetry telemetry, ILogger<QueryDispatcher> logger)
    {
        _services = services;
        _currentSubject = currentSubject;
        _accessControl = accessControl;
        _unitOfWorkFactory = unitOfWorkFactory;
        _clock = clock;
        _telemetry = telemetry;
        _logger = logger;
    }

    public static string PermissionOf(Type queryType)
    {
        string code = queryType.GetCustomAttribute<QueryAttribute>(inherit: false)?.PermissionCode
            ?? throw new InvalidOperationException($"{queryType.FullName} carries no [Query(\"TYPE.READ\")] attribute; every query names its permission (R-SEC-01).");
        (string _, string verb) = PermissionCodes.Split(code);
        return string.Equals(verb, PermissionCodes.ReadVerb, StringComparison.Ordinal)
            ? code
            : throw new InvalidOperationException($"{queryType.FullName} names permission {code}; a query's permission is TYPE.READ (D-47).");
    }

    public async Task<CommandResponse> DispatchAsync<TQuery, TResult>(TQuery query, CorrelationId? correlationId, CancellationToken cancellationToken)
        where TQuery : IQuery<TResult>
    {
        ArgumentNullException.ThrowIfNull(query);
        string permission = PermissionOf(typeof(TQuery));
        CorrelationId correlation = correlationId ?? CorrelationIds.Fresh();
        AuthenticatedSubject? subject = _currentSubject.Current;

        using IDisposable? scope = _logger.BeginScope(new Dictionary<string, object>
        {
            ["QueryName"] = permission,
            ["CorrelationId"] = correlation.Value,
            ["Subject"] = subject?.Subject ?? "anonymous",
        });
        using Activity? activity = _telemetry.Source.StartActivity($"query {permission}");
        activity?.SetTag("themis.query", permission);
        activity?.SetTag("themis.correlation_id", correlation.Value);

        CommandResponse response = await RunAsync<TQuery, TResult>(query, permission, correlation, subject, cancellationToken);
        _telemetry.Queries.Add(1, new KeyValuePair<string, object?>("query", permission), new KeyValuePair<string, object?>("outcome", response.IsSuccess ? "success" : "rejected"));
        activity?.SetTag("http.response.status_code", response.StatusCode);
        return response;
    }

    private async Task<CommandResponse> RunAsync<TQuery, TResult>(TQuery query, string permission, CorrelationId correlation, AuthenticatedSubject? subject, CancellationToken cancellationToken)
        where TQuery : IQuery<TResult>
    {
        if (subject is null)
        {
            return ProblemDetailsBody.Http(StatusCodes.Status401Unauthorized, "authentication_required", AccessDenied.RuleId, "The query requires a signed-in user.", correlation);
        }

        await using IKernelUnitOfWork unitOfWork = await _unitOfWorkFactory.BeginAsync(cancellationToken);
        try
        {
            Result<Principal> resolved = await _accessControl.ResolvePrincipalAsync(unitOfWork, subject, cancellationToken);
            if (resolved.IsFailure)
            {
                LogSubjectRefused(_logger, permission, subject.Subject, resolved.Primary!.Code);
                return ProblemDetailsBody.ToResponse(resolved.Errors, correlation);
            }

            Principal principal = resolved.Value;
            using IDisposable? userScope = _logger.BeginScope(new Dictionary<string, object> { ["UserId"] = principal.UserId.ToString() });

            // RR-62: the scope resolver and the handler are module code, so each starts as themis_app.
            await unitOfWork.ResetRoleAsync(cancellationToken);
            Result<RequestScope> scope = await ScopeSources.ResolveAsync(query, unitOfWork, _services, cancellationToken);
            if (scope.IsFailure)
            {
                LogRejected(_logger, permission, scope.Primary!.Code, scope.Primary.RuleId);
                return ProblemDetailsBody.ToResponse(scope.Errors, correlation);
            }

            Result authorised = await _accessControl.AuthorizeAsync(unitOfWork, principal, permission, scope.Value, _clock.GetUtcNow(), cancellationToken);
            if (authorised.IsFailure)
            {
                LogRejected(_logger, permission, authorised.Primary!.Code, authorised.Primary.RuleId);
                return ProblemDetailsBody.ToResponse(authorised.Errors, correlation);
            }

            IQueryHandler<TQuery, TResult> handler = _services.GetRequiredService<IQueryHandler<TQuery, TResult>>();
            await unitOfWork.ResetRoleAsync(cancellationToken);
            Result<TResult> result = await handler.HandleAsync(query, new QueryContext(principal, unitOfWork), cancellationToken);
            if (!result.IsSuccess)
            {
                LogRejected(_logger, permission, result.Primary!.Code, result.Primary.RuleId);
                return ProblemDetailsBody.ToResponse(result.Errors, correlation);
            }

            return new CommandResponse(StatusCodes.Status200OK, CommandResponse.JsonContentType, JsonSerializer.Serialize(result.Value, ThemisJson.Options), Replayed: false, (result.Value as IVersionedResult)?.Version);
        }
        finally
        {
            await unitOfWork.RollbackAsync(cancellationToken);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Query {QueryName} rejected with {Code} ({RuleId}).")]
    private static partial void LogRejected(ILogger logger, string queryName, string code, string? ruleId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Query {QueryName} refused for subject {Subject}: {Code} (R-SEC-01).")]
    private static partial void LogSubjectRefused(ILogger logger, string queryName, string subject, string code);

    private sealed record QueryContext(Principal Principal, IUnitOfWork UnitOfWork) : IQueryContext;
}
