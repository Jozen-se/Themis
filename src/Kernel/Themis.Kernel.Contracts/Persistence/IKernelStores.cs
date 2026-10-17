using Themis.Kernel.Contracts.Commands;
using Themis.Kernel.Primitives;

namespace Themis.Kernel.Contracts.Persistence;

/// <summary>A stored idempotency record (Phase 0 Specification, IdempotencyRecord; R-API-01, R-API-04).</summary>
public sealed record StoredIdempotencyRecord(IdempotencyKey Key, UserId UserId, string CommandName, string RequestHash, CommandResponse Response, DateTimeOffset ExpiresAt);

/// <summary>Reads and writes idempotency records on the command's unit of work, as themis_kernel. Kernel use only.</summary>
public interface IIdempotencyStore
{
    Task<StoredIdempotencyRecord?> FindAsync(IKernelUnitOfWork unitOfWork, UserId userId, IdempotencyKey key, CancellationToken cancellationToken);

    Task AddAsync(IKernelUnitOfWork unitOfWork, StoredIdempotencyRecord record, CancellationToken cancellationToken);
}

/// <summary>One audit event to write (R-AUD-01).</summary>
public sealed record AuditEntry(string EntityType, Guid EntityId, IReadOnlyDictionary<string, FieldChange> Changes);

/// <summary>Before and after of one field, as JSON text; null means absent.</summary>
public sealed record FieldChange(string? Before, string? After);

/// <summary>Writes audit events on the command's unit of work, as themis_kernel, insert only (R-LED-06). Kernel use only.</summary>
public interface IAuditStore
{
    Task AddAsync(IKernelUnitOfWork unitOfWork, ICommandContext context, string commandName, IReadOnlyList<AuditEntry> entries, CancellationToken cancellationToken);
}

/// <summary>One outbox event to write in the command's transaction (D-44), with the publisher's part of the envelope (D-50).</summary>
public sealed record OutboxEntry(string EventType, int SchemaVersion, string AggregateType, Guid AggregateId, Guid? CompanyId, string PayloadJson);

/// <summary>
/// Writes outbox events on the command's unit of work, as themis_kernel, completing the envelope with the
/// occurrence time, the command name and the correlation id of the context (D-50). Kernel use only.
/// </summary>
public interface IOutboxStore
{
    Task AddAsync(IKernelUnitOfWork unitOfWork, ICommandContext context, string commandName, IReadOnlyList<OutboxEntry> entries, CancellationToken cancellationToken);
}
