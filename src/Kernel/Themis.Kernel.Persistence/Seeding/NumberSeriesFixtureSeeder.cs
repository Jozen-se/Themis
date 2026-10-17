using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Themis.Kernel.Contracts.Documents;
using Themis.Kernel.Contracts.Persistence;
using Themis.Kernel.Persistence.Entities;
using Themis.Kernel.Persistence.Entities.Documents;
using Themis.Kernel.Primitives;

namespace Themis.Kernel.Persistence.Seeding;

/// <summary>
/// Writes a <see cref="NumberSeriesFixtureDefinition"/> as themis_migrator, in one transaction, idempotently: number
/// series are company configuration, so they come with the test and development fixture only (step 7 brief). The
/// type's gapless flag is copied from the registry catalogue into the row's type_requires_gapless, which the
/// database ties to the registry and checks against the series' own flag. Every row inserted gets one audit event
/// under the system user (R-AUD-01); no outbox event, because no command ran.
/// </summary>
internal static class NumberSeriesFixtureSeeder
{
    private static readonly JsonSerializerOptions ChangesJson = new(JsonSerializerDefaults.Web);

    public static async Task<int> ApplyAsync(string migratorConnectionString, NumberSeriesFixtureDefinition fixture, DateTimeOffset now, UserId seededBy, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        await using KernelDbContext context = KernelDbContextFactory.ForMigrations(migratorConnectionString);
        await using IDbContextTransaction transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        Guid by = seededBy.Value;
        int inserted = 0;

        foreach (NumberSeriesDefinition series in fixture.Series)
        {
            Guid id = series.Id;
            if (await context.NumberSeries.AnyAsync(row => row.Id == id, cancellationToken))
            {
                continue;
            }

            bool typeRequiresGapless = DocumentTypes.Get(series.DocumentType).Gapless;
            context.NumberSeries.Add(new NumberSeries
            {
                Id = id,
                CompanyId = series.CompanyId,
                DocumentType = series.DocumentType,
                Scope = series.Scope.ToString(),
                FiscalYearId = series.FiscalYearId,
                Prefix = series.Prefix,
                Padding = series.Padding,
                NextNumber = series.NextNumber,
                Gapless = series.Gapless,
                TypeRequiresGapless = typeRequiresGapless,
                CreatedAt = now,
                CreatedBy = by,
                UpdatedAt = now,
                UpdatedBy = by,
            });
            context.AuditEvents.Add(new AuditEvent
            {
                Id = EntityId.New(),
                OccurredAt = now,
                UserId = by,
                CommandName = OrganisationFixtureSeeder.CommandName,
                EntityType = "NumberSeries",
                EntityId = id,
                Changes = JsonSerializer.Serialize(new Dictionary<string, FieldChange>(StringComparer.Ordinal)
                {
                    ["companyId"] = new(null, series.CompanyId.ToString()),
                    ["documentType"] = new(null, series.DocumentType),
                    ["scope"] = new(null, series.Scope.ToString()),
                    ["fiscalYearId"] = new(null, series.FiscalYearId?.ToString()),
                    ["prefix"] = new(null, series.Prefix),
                    ["padding"] = new(null, series.Padding.ToString(CultureInfo.InvariantCulture)),
                    ["nextNumber"] = new(null, series.NextNumber.ToString(CultureInfo.InvariantCulture)),
                    ["gapless"] = new(null, series.Gapless ? "True" : "False"),
                }, ChangesJson),
            });
            inserted++;
        }

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return inserted;
    }
}
