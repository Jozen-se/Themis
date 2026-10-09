using System.Text.Json;
using Themis.Kernel.Contracts.Commands;
using Themis.Kernel.Contracts.Messaging;
using Themis.Kernel.Contracts.Persistence;
using Themis.Kernel.Contracts.Security;
using Themis.Kernel.Pipeline.Json;
using Themis.Kernel.Primitives;

namespace Themis.Kernel.Pipeline.Dispatch;

/// <summary>
/// The context one attempt of a command runs in. Audit and outbox entries are buffered while the handler runs and
/// written by the dispatcher as themis_kernel only when the handler succeeds; a rejected command leaves no audit or
/// outbox row (R-AUD-01, D-44).
/// </summary>
internal sealed class CommandContext : ICommandContext, IAuditWriter, IOutboxWriter
{
    private readonly List<AuditEntry> _audit = [];
    private readonly List<OutboxEntry> _outbox = [];

    public CommandContext(CommandId commandId, CorrelationId correlationId, Principal principal, IdempotencyKey idempotencyKey, DateTimeOffset now, IUnitOfWork unitOfWork)
    {
        CommandId = commandId;
        CorrelationId = correlationId;
        Principal = principal;
        IdempotencyKey = idempotencyKey;
        Now = now;
        UnitOfWork = unitOfWork;
    }

    public CommandId CommandId { get; }

    public CorrelationId CorrelationId { get; }

    public Principal Principal { get; }

    public IdempotencyKey IdempotencyKey { get; }

    public DateTimeOffset Now { get; }

    public IUnitOfWork UnitOfWork { get; }

    public IAuditWriter Audit => this;

    public IOutboxWriter Outbox => this;

    public IReadOnlyList<AuditEntry> AuditEntries => _audit;

    public IReadOnlyList<OutboxEntry> OutboxEntries => _outbox;

    public void Record(string entityType, Guid entityId, IReadOnlyDictionary<string, FieldChange> changes) =>
        _audit.Add(new AuditEntry(entityType, entityId, changes));

    public void Publish(string eventType, int schemaVersion, Guid aggregateId, object payload) =>
        _outbox.Add(new OutboxEntry(eventType, schemaVersion, aggregateId, JsonSerializer.Serialize(payload, payload.GetType(), ThemisJson.Options)));

    /// <summary>Discards what a rolled-back handler buffered.</summary>
    public void Discard()
    {
        _audit.Clear();
        _outbox.Clear();
    }
}
