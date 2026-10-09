using Themis.Kernel.Primitives;

namespace Themis.Kernel.Contracts.Commands;

/// <summary>
/// The one path every command takes: validation, authorisation (R-SEC-01), idempotency (R-API-01, R-API-04), one
/// transaction with role switching (D-41, R-DB-01), audit events (R-AUD-01), outbox events (D-44) and telemetry (D-25).
/// </summary>
public interface ICommandDispatcher
{
    Task<CommandResponse> DispatchAsync<TCommand, TResult>(TCommand command, CommandEnvelope envelope, CancellationToken cancellationToken)
        where TCommand : ICommand<TResult>;
}

/// <summary>Request-level inputs that are not part of the command body.</summary>
/// <param name="IdempotencyKey">The Idempotency-Key header; missing means the command is rejected (R-API-01).</param>
/// <param name="CorrelationId">The request's correlation id (D-25).</param>
public sealed record CommandEnvelope(IdempotencyKey? IdempotencyKey, CorrelationId CorrelationId);

/// <summary>
/// The HTTP representation of a command's outcome. A replayed response is byte-for-byte the first one (R-API-01).
/// </summary>
/// <param name="StatusCode">The HTTP status the API returns.</param>
/// <param name="ContentType">application/json for a result, application/problem+json for a rejection (R-API-03).</param>
/// <param name="Body">The JSON body.</param>
/// <param name="Replayed">True when the response came from the idempotency store.</param>
public sealed record CommandResponse(int StatusCode, string ContentType, string Body, bool Replayed)
{
    public const string JsonContentType = "application/json";
    public const string ProblemContentType = "application/problem+json";

    public bool IsSuccess => StatusCode is >= 200 and < 300;
}
