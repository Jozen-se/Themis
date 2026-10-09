namespace Themis.Kernel.Persistence.Entities;

/// <summary>Phase 0 Specification, IdempotencyRecord: unique per user and key; the same key with a different request hash is rejected (R-API-01, R-API-04).</summary>
internal sealed class IdempotencyRecord
{
    public required Guid UserId { get; init; }

    public required string Key { get; init; }

    public required string CommandName { get; init; }

    public required string RequestHash { get; init; }

    /// <summary>The stored response as JSON: status code, content type and body, replayed byte for byte.</summary>
    public required string Response { get; init; }

    public required DateTimeOffset ExpiresAt { get; init; }
}
