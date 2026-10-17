namespace Themis.Kernel.Persistence.Entities;

/// <summary>
/// Phase 0 Specification, OutboxEvent: written in the same transaction as the change (D-44) with the envelope of
/// D-50 (aggregate type and id, company, occurrence time, command name, correlation id; never the idempotency key
/// or the user); the relay updates only published_at and attempts.
/// </summary>
internal sealed class OutboxEvent
{
    public required Guid Id { get; init; }

    public required string EventType { get; init; }

    public required int SchemaVersion { get; init; }

    public required string AggregateType { get; init; }

    public required Guid AggregateId { get; init; }

    public Guid? CompanyId { get; init; }

    public required string CommandName { get; init; }

    public required string CorrelationId { get; init; }

    public required string Payload { get; init; }

    public required DateTimeOffset OccurredAt { get; init; }

    public DateTimeOffset? PublishedAt { get; set; }

    public int Attempts { get; set; }
}
