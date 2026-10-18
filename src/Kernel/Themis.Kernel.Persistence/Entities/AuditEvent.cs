namespace Themis.Kernel.Persistence.Entities;

/// <summary>
/// Phase 0 Specification, AuditEvent: one per changed entity, append-only (R-AUD-01, R-LED-06). The command id (D-51)
/// is required for every event written from step 8 on and empty on the events migrations and seeders wrote before;
/// no audit row is ever changed.
/// </summary>
internal sealed class AuditEvent
{
    public required Guid Id { get; init; }

    public Guid? CommandId { get; init; }

    public required DateTimeOffset OccurredAt { get; init; }

    public required Guid UserId { get; init; }

    public required string CommandName { get; init; }

    public required string EntityType { get; init; }

    public required Guid EntityId { get; init; }

    /// <summary>Before and after per field, as JSON.</summary>
    public required string Changes { get; init; }

    public string? IdempotencyKey { get; init; }

    public string? CorrelationId { get; init; }
}
