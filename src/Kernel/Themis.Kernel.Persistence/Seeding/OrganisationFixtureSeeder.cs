using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Themis.Kernel.Contracts.Organisation;
using Themis.Kernel.Contracts.Persistence;
using Themis.Kernel.Persistence.Entities;
using Themis.Kernel.Persistence.Entities.Organisation;
using Themis.Kernel.Primitives;

namespace Themis.Kernel.Persistence.Seeding;

/// <summary>
/// Writes an <see cref="OrganisationFixtureDefinition"/> as themis_migrator, in one transaction, idempotently: a row
/// whose id already exists is left untouched, so the seed can run on every start of Themis.Migrator. The seed is
/// not a command, so it writes no outbox event, but every row it inserts gets one audit event with command name
/// SYSTEM.SEED_FIXTURE (R-AUD-01). Only the migrator seeds (brief, Migrations); the application never does. The
/// party's number is taken from the installation-level PARTY series through the delegate ApplyAsync receives, the
/// numbering service COMPANY.CREATE uses, on this seeder's own connection and transaction (milestone 2 step 3,
/// D-64), so the fixture never holds a fixed number that a later party could collide with; a re-run finds the party
/// and takes no number.
/// </summary>
internal static class OrganisationFixtureSeeder
{
    public const string CommandName = "SYSTEM.SEED_FIXTURE";

    private static readonly JsonSerializerOptions ChangesJson = new(JsonSerializerDefaults.Web);

    public static async Task<int> ApplyAsync(string migratorConnectionString, OrganisationFixtureDefinition fixture, Func<IUnitOfWork, CancellationToken, Task<string>> takePartyNumber, DateTimeOffset now, UserId seededBy, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        ArgumentNullException.ThrowIfNull(takePartyNumber);
        await using KernelDbContext context = KernelDbContextFactory.ForMigrations(migratorConnectionString);
        await using IDbContextTransaction transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        int inserted = 0;
        Guid by = seededBy.Value;
        // D-51: one command id per seeder call, shared by every audit event it writes.
        Guid commandId = EntityId.New();

        inserted += await AddIfMissingAsync(context, commandId, "Installation", fixture.Installation, fixture.Installation.Id, now, by, () => new Installation
        {
            Id = fixture.Installation.Id,
            Code = fixture.Installation.Code.Value,
            Name = fixture.Installation.Name,
            Status = fixture.Installation.Status.ToString(),
            DefaultLocale = fixture.Installation.DefaultLocale,
            CreatedAt = now,
            CreatedBy = by,
            UpdatedAt = now,
            UpdatedBy = by,
        }, cancellationToken);

        PartyDefinition party = fixture.CompanyParty;
        string? partyNumber = null;
        if (!await context.Parties.AnyAsync(row => row.Id == party.Id, cancellationToken))
        {
            // D-64: the number comes from the PARTY series, locked and advanced in this transaction (last lock class, R-LED-08).
            partyNumber = await takePartyNumber(new SeedingUnitOfWork(context.Database.GetDbConnection(), transaction.GetDbTransaction()), cancellationToken);
        }

        inserted += await AddIfMissingAsync(context, commandId, "Party", new { party.Id, PartyNumber = partyNumber, party.PartyType, party.Name, party.OrganisationNumber, party.VatNumber, party.CountryCode, party.Language, party.Status, party.DuplicateKey }, party.Id, now, by, () => new Party
        {
            Id = party.Id,
            PartyNumber = partyNumber!,
            PartyType = party.PartyType.ToString(),
            Name = party.Name,
            OrganisationNumber = party.OrganisationNumber,
            VatNumber = party.VatNumber,
            CountryCode = party.CountryCode.Value,
            Language = party.Language,
            Status = party.Status.ToString(),
            DuplicateKey = party.DuplicateKey,
            CreatedAt = now,
            CreatedBy = by,
            UpdatedAt = now,
            UpdatedBy = by,
        }, cancellationToken);

        inserted += await AddIfMissingAsync(context, commandId, "ChartOfAccounts", fixture.ChartOfAccounts, fixture.ChartOfAccounts.Id, now, by, () => new ChartOfAccounts
        {
            Id = fixture.ChartOfAccounts.Id,
            Code = fixture.ChartOfAccounts.Code.Value,
            Name = fixture.ChartOfAccounts.Name,
            Template = fixture.ChartOfAccounts.Template,
            CreatedAt = now,
            CreatedBy = by,
            UpdatedAt = now,
            UpdatedBy = by,
        }, cancellationToken);

        CompanyDefinition company = fixture.Company;
        inserted += await AddIfMissingAsync(context, commandId, "Company", company, company.Id, now, by, () => new Company
        {
            Id = company.Id,
            Code = company.Code.Value,
            PartyId = company.PartyId,
            LegalName = company.LegalName,
            OrganisationNumber = company.OrganisationNumber,
            OrganisationNumberNormalised = company.OrganisationNumberNormalised,
            VatNumber = company.VatNumber,
            CountryCode = company.CountryCode.Value,
            BaseCurrency = company.BaseCurrency.Value,
            TimeZone = company.TimeZone,
            FiscalYearStartMonth = company.FiscalYearStartMonth,
            ChartOfAccountsId = company.ChartOfAccountsId,
            RoundingMode = company.RoundingMode.ToString(),
            TaxRoundingLevel = company.TaxRoundingLevel.ToString(),
            Status = company.Status.ToString(),
            CreatedAt = now,
            CreatedBy = by,
            UpdatedAt = now,
            UpdatedBy = by,
        }, cancellationToken);

        inserted += await AddIfMissingAsync(context, commandId, "Site", fixture.Site, fixture.Site.Id, now, by, () => new Site
        {
            Id = fixture.Site.Id,
            CompanyId = fixture.Site.CompanyId,
            Code = fixture.Site.Code.Value,
            Name = fixture.Site.Name,
            TimeZone = fixture.Site.TimeZone,
            CreatedAt = now,
            CreatedBy = by,
            UpdatedAt = now,
            UpdatedBy = by,
        }, cancellationToken);

        inserted += await AddIfMissingAsync(context, commandId, "Warehouse", fixture.Warehouse, fixture.Warehouse.Id, now, by, () => new Warehouse
        {
            Id = fixture.Warehouse.Id,
            SiteId = fixture.Warehouse.SiteId,
            CompanyId = fixture.Warehouse.CompanyId,
            Code = fixture.Warehouse.Code.Value,
            Name = fixture.Warehouse.Name,
            IsDefault = fixture.Warehouse.IsDefault,
            Status = fixture.Warehouse.Status.ToString(),
            CreatedAt = now,
            CreatedBy = by,
            UpdatedAt = now,
            UpdatedBy = by,
        }, cancellationToken);

        FiscalYearDefinition year = fixture.FiscalYear;
        inserted += await AddIfMissingAsync(context, commandId, "FiscalYear", year with { Periods = [] }, year.Id, now, by, () => new FiscalYear
        {
            Id = year.Id,
            CompanyId = year.CompanyId,
            Code = year.Code.Value,
            StartDate = year.StartDate.Value,
            EndDate = year.EndDate.Value,
            Status = year.Status.ToString(),
            CreatedAt = now,
            CreatedBy = by,
            UpdatedAt = now,
            UpdatedBy = by,
        }, cancellationToken);

        foreach (FiscalPeriodDefinition period in year.Periods)
        {
            inserted += await AddIfMissingAsync(context, commandId, "FiscalPeriod", period, period.Id, now, by, () => new FiscalPeriod
            {
                Id = period.Id,
                FiscalYearId = year.Id,
                CompanyId = year.CompanyId,
                PeriodNumber = period.PeriodNumber,
                StartDate = period.StartDate.Value,
                EndDate = period.EndDate.Value,
                Status = period.Status.ToString(),
                CreatedAt = now,
                CreatedBy = by,
                UpdatedAt = now,
                UpdatedBy = by,
            }, cancellationToken);
        }

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return inserted;
    }

    private static async Task<int> AddIfMissingAsync<T>(KernelDbContext context, Guid commandId, string entityType, object definition, Guid id, DateTimeOffset now, Guid by, Func<T> create, CancellationToken cancellationToken)
        where T : MasterDataRow
    {
        if (await context.Set<T>().AnyAsync(row => row.Id == id, cancellationToken))
        {
            return 0;
        }

        context.Set<T>().Add(create());
        // R-AUD-01: one audit event per seeded row, every field as a change from nothing to its seeded value; one command id per seeder call (D-51).
        context.AuditEvents.Add(new AuditEvent
        {
            Id = EntityId.New(),
            CommandId = commandId,
            OccurredAt = now,
            UserId = by,
            CommandName = CommandName,
            EntityType = entityType,
            EntityId = id,
            Changes = ChangesFrom(definition),
        });
        return 1;
    }

    private static string ChangesFrom(object definition)
    {
        JsonObject values = JsonSerializer.SerializeToNode(definition, definition.GetType(), ChangesJson)!.AsObject();
        JsonObject changes = [];
        foreach ((string name, JsonNode? value) in values)
        {
            // Codes and dates are record structs with one Value member; the audit trail stores the value itself.
            JsonNode? unwrapped = value is JsonObject { Count: 1 } single && single.TryGetPropertyValue("value", out JsonNode? inner) ? inner : value;
            string? after = unwrapped switch
            {
                null => null,
                JsonValue scalar => scalar.ToString(),
                _ => unwrapped.ToJsonString(ChangesJson),
            };
            changes[name] = new JsonObject { ["before"] = null, ["after"] = after };
        }

        return changes.ToJsonString(ChangesJson);
    }
}
