using Themis.Kernel.Contracts.Persistence;

namespace Themis.Kernel.Contracts.Messaging;

/// <summary>
/// Collects audit events during a command; the pipeline writes them with the commit (R-AUD-01): one per changed
/// entity, carrying the command name, idempotency key and correlation id. A rejected command writes none.
/// </summary>
public interface IAuditWriter
{
    void Record(string entityType, Guid entityId, IReadOnlyDictionary<string, FieldChange> changes);
}

/// <summary>
/// Collects outbox events during a command; the pipeline writes them in the same transaction (D-44): one per
/// document or master data row whose state the command changes, schema version 1 unless stated otherwise.
/// </summary>
public interface IOutboxWriter
{
    void Publish(string eventType, int schemaVersion, Guid aggregateId, object payload);
}
