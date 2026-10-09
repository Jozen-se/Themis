using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Themis.Kernel.Contracts.Commands;
using Themis.Kernel.Contracts.Persistence;
using Themis.Kernel.Persistence.Entities;
using Themis.Kernel.Primitives;

namespace Themis.Kernel.Persistence.Stores;

/// <summary>Idempotency records on the command's unit of work, written as themis_kernel (R-API-01, R-API-04).</summary>
internal sealed class KernelIdempotencyStore : IIdempotencyStore
{
    private static readonly JsonSerializerOptions ResponseJson = new(JsonSerializerDefaults.Web);

    public async Task<StoredIdempotencyRecord?> FindAsync(IKernelUnitOfWork unitOfWork, UserId userId, IdempotencyKey key, CancellationToken cancellationToken)
    {
        await using KernelDbContext context = KernelDbContextFactory.OnUnitOfWork(unitOfWork);
        string keyValue = key.Value;
        IdempotencyRecord? record = await context.IdempotencyRecords.AsNoTracking()
            .SingleOrDefaultAsync(r => r.UserId == userId.Value && r.Key == keyValue, cancellationToken);
        if (record is null)
        {
            return null;
        }

        StoredResponse stored = JsonSerializer.Deserialize<StoredResponse>(record.Response, ResponseJson)
            ?? throw new InvalidOperationException("Stored idempotency response is unreadable.");
        return new StoredIdempotencyRecord(key, userId, record.CommandName, record.RequestHash,
            new CommandResponse(stored.StatusCode, stored.ContentType, stored.Body, Replayed: true), record.ExpiresAt);
    }

    public async Task AddAsync(IKernelUnitOfWork unitOfWork, StoredIdempotencyRecord record, CancellationToken cancellationToken)
    {
        await using KernelDbContext context = KernelDbContextFactory.OnUnitOfWork(unitOfWork);
        context.IdempotencyRecords.Add(new IdempotencyRecord
        {
            UserId = record.UserId.Value,
            Key = record.Key.Value,
            CommandName = record.CommandName,
            RequestHash = record.RequestHash,
            Response = JsonSerializer.Serialize(new StoredResponse(record.Response.StatusCode, record.Response.ContentType, record.Response.Body), ResponseJson),
            ExpiresAt = record.ExpiresAt,
        });
        await context.SaveChangesAsync(cancellationToken);
    }

    private sealed record StoredResponse(int StatusCode, string ContentType, string Body);
}

/// <summary>Audit events on the command's unit of work, insert only (R-AUD-01, R-LED-06).</summary>
internal sealed class KernelAuditStore : IAuditStore
{
    private static readonly JsonSerializerOptions ChangesJson = new(JsonSerializerDefaults.Web);

    public async Task AddAsync(IKernelUnitOfWork unitOfWork, ICommandContext commandContext, string commandName, IReadOnlyList<AuditEntry> entries, CancellationToken cancellationToken)
    {
        if (entries.Count == 0)
        {
            return;
        }

        await using KernelDbContext context = KernelDbContextFactory.OnUnitOfWork(unitOfWork);
        foreach (AuditEntry entry in entries)
        {
            context.AuditEvents.Add(new AuditEvent
            {
                Id = EntityId.New(),
                OccurredAt = commandContext.Now,
                UserId = commandContext.Principal.UserId.Value,
                CommandName = commandName,
                EntityType = entry.EntityType,
                EntityId = entry.EntityId,
                Changes = JsonSerializer.Serialize(entry.Changes, ChangesJson),
                IdempotencyKey = commandContext.IdempotencyKey.Value,
                CorrelationId = commandContext.CorrelationId.Value,
            });
        }

        await context.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>Outbox events on the command's unit of work, in the same transaction as the change (D-44).</summary>
internal sealed class KernelOutboxStore : IOutboxStore
{
    public async Task AddAsync(IKernelUnitOfWork unitOfWork, DateTimeOffset occurredAt, IReadOnlyList<OutboxEntry> entries, CancellationToken cancellationToken)
    {
        if (entries.Count == 0)
        {
            return;
        }

        await using KernelDbContext context = KernelDbContextFactory.OnUnitOfWork(unitOfWork);
        foreach (OutboxEntry entry in entries)
        {
            context.OutboxEvents.Add(new OutboxEvent
            {
                Id = EntityId.New(),
                EventType = entry.EventType,
                SchemaVersion = entry.SchemaVersion,
                AggregateId = entry.AggregateId,
                Payload = entry.PayloadJson,
                OccurredAt = occurredAt,
            });
        }

        await context.SaveChangesAsync(cancellationToken);
    }
}
