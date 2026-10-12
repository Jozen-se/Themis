using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Themis.Kernel.Contracts.MasterData;
using Themis.Kernel.Contracts.Persistence;
using Themis.Kernel.Persistence.Entities;
using Themis.Kernel.Persistence.Entities.MasterData;
using Themis.Kernel.Primitives;

namespace Themis.Kernel.Persistence.Seeding;

/// <summary>
/// Writes a <see cref="MasterDataFixtureDefinition"/> as themis_migrator, in one transaction, idempotently: item
/// posting groups are company configuration, not reference data, so they come with the test and development
/// fixture only (step 5 brief). Every row inserted gets one audit event under the system user (R-AUD-01).
/// </summary>
internal static class MasterDataFixtureSeeder
{
    private static readonly JsonSerializerOptions ChangesJson = new(JsonSerializerDefaults.Web);

    public static async Task<int> ApplyAsync(string migratorConnectionString, MasterDataFixtureDefinition fixture, DateTimeOffset now, UserId seededBy, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        await using KernelDbContext context = KernelDbContextFactory.ForMigrations(migratorConnectionString);
        await using IDbContextTransaction transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        Guid by = seededBy.Value;
        int inserted = 0;

        foreach (ItemPostingGroupDefinition group in fixture.ItemPostingGroups)
        {
            Guid id = group.Id;
            if (await context.ItemPostingGroups.AnyAsync(row => row.Id == id, cancellationToken))
            {
                continue;
            }

            context.ItemPostingGroups.Add(new ItemPostingGroup { Id = id, Code = group.Code.Value, Name = group.Name, CreatedAt = now, CreatedBy = by, UpdatedAt = now, UpdatedBy = by });
            context.AuditEvents.Add(new AuditEvent
            {
                Id = EntityId.New(),
                OccurredAt = now,
                UserId = by,
                CommandName = OrganisationFixtureSeeder.CommandName,
                EntityType = "ItemPostingGroup",
                EntityId = id,
                Changes = JsonSerializer.Serialize(new Dictionary<string, FieldChange> { ["code"] = new(null, group.Code.Value), ["name"] = new(null, group.Name) }, ChangesJson),
            });
            inserted++;
        }

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return inserted;
    }
}
