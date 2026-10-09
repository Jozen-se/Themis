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
/// Runs a query (P-04: GET only): signed-in principal (401), permission TYPE.READ (403, R-SEC-01), one read-only
/// unit of work rolled back at the end (R-DB-01), the handler's result as JSON or problem details (R-API-03).
/// No idempotency, audit or outbox: a query changes nothing.
/// </summary>
internal sealed partial class QueryDispatcher : IQueryDispatcher
{
    private readonly IServiceProvider _services;
    private readonly ICurrentPrincipal _currentPrincipal;
    private readonly IPermissionEvaluator _permissions;
    private readonly IUnitOfWorkFactory _unitOfWorkFactory;
    private readonly PipelineTelemetry _telemetry;
    private readonly ILogger<QueryDispatcher> _logger;

    public QueryDispatcher(IServiceProvider services, ICurrentPrincipal currentPrincipal, IPermissionEvaluator permissions, IUnitOfWorkFactory unitOfWorkFactory, PipelineTelemetry telemetry, ILogger<QueryDispatcher> logger)
    {
        _services = services;
        _currentPrincipal = currentPrincipal;
        _permissions = permissions;
        _unitOfWorkFactory = unitOfWorkFactory;
        _telemetry = telemetry;
        _logger = logger;
    }

    public static string PermissionOf(Type queryType) =>
        queryType.GetCustomAttribute<QueryAttribute>(inherit: false)?.PermissionCode
        ?? throw new InvalidOperationException($"{queryType.FullName} carries no [Query(\"TYPE.READ\")] attribute; every query names its permission (R-SEC-01).");

    public async Task<CommandResponse> DispatchAsync<TQuery, TResult>(TQuery query, CorrelationId? correlationId, CancellationToken cancellationToken)
        where TQuery : IQuery<TResult>
    {
        ArgumentNullException.ThrowIfNull(query);
        string permission = PermissionOf(typeof(TQuery));
        CorrelationId correlation = correlationId ?? CorrelationIds.Fresh();
        Principal? principal = _currentPrincipal.Current;
        string userId = principal?.UserId.ToString() ?? "anonymous";

        using IDisposable? scope = _logger.BeginScope(new Dictionary<string, object>
        {
            ["QueryName"] = permission,
            ["CorrelationId"] = correlation.Value,
            ["UserId"] = userId,
        });
        using Activity? activity = _telemetry.Source.StartActivity($"query {permission}");
        activity?.SetTag("themis.query", permission);
        activity?.SetTag("themis.correlation_id", correlation.Value);

        if (principal is null)
        {
            return ProblemDetailsBody.Http(StatusCodes.Status401Unauthorized, "authentication_required", "R-SEC-01", "The query requires a signed-in user.", correlation);
        }

        if (!await _permissions.HasPermissionAsync(principal, permission, cancellationToken))
        {
            return ProblemDetailsBody.ToResponse(Error.Forbidden("permission_missing", $"The user lacks permission {permission}."), correlation);
        }

        IQueryHandler<TQuery, TResult> handler = _services.GetRequiredService<IQueryHandler<TQuery, TResult>>();
        await using IKernelUnitOfWork unitOfWork = await _unitOfWorkFactory.BeginAsync(cancellationToken);
        Result<TResult> result = await handler.HandleAsync(query, new QueryContext(principal, unitOfWork), cancellationToken);
        await unitOfWork.RollbackAsync(cancellationToken);

        CommandResponse response = result.IsSuccess
            ? new CommandResponse(StatusCodes.Status200OK, CommandResponse.JsonContentType, JsonSerializer.Serialize(result.Value, ThemisJson.Options), Replayed: false, (result.Value as IVersionedResult)?.Version)
            : ProblemDetailsBody.ToResponse(result.Errors, correlation);
        _telemetry.Queries.Add(1, new KeyValuePair<string, object?>("query", permission), new KeyValuePair<string, object?>("outcome", response.IsSuccess ? "success" : "rejected"));
        if (!response.IsSuccess)
        {
            LogRejected(_logger, permission, result.Primary!.Code, result.Primary.RuleId);
        }

        return response;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Query {QueryName} rejected with {Code} ({RuleId}).")]
    private static partial void LogRejected(ILogger logger, string queryName, string code, string? ruleId);

    private sealed record QueryContext(Principal Principal, IUnitOfWork UnitOfWork) : IQueryContext;
}
