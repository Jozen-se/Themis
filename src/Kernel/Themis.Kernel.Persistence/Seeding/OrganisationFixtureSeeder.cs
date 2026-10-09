using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Themis.Kernel.Contracts.Organisation;
using Themis.Kernel.Persistence.Entities.Organisation;
using Themis.Kernel.Primitives;

namespace Themis.Kernel.Persistence.Seeding;

/// <summary>
/// Writes an <see cref="OrganisationFixtureDefinition"/> as themis_migrator, in one transaction, idempotently: a row
/// whose id already exists is left untouched, so the seed can run on every start of Themis.Migrator. Only the
/// migrator seeds (brief, Migrations); the application never does.
/// </summary>
internal static class OrganisationFixtureSeeder
{
    public static async Task<int> ApplyAsync(string migratorConnectionString, OrganisationFixtureDefinition fixture, DateTimeOffset now, UserId seededBy, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        await using KernelDbContext context = KernelDbContextFactory.ForMigrations(migratorConnectionString);
        await using IDbContextTransaction transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        int inserted = 0;
        Guid by = seededBy.Value;

        inserted += await AddIfMissingAsync(context, fixture.Installation.Id, () => new Installation
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
        inserted += await AddIfMissingAsync(context, party.Id, () => new Party
        {
            Id = party.Id,
            PartyNumber = party.PartyNumber.Value,
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

        inserted += await AddIfMissingAsync(context, fixture.ChartOfAccounts.Id, () => new ChartOfAccounts
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
        inserted += await AddIfMissingAsync(context, company.Id, () => new Company
        {
            Id = company.Id,
            Code = company.Code.Value,
            PartyId = company.PartyId,
            LegalName = company.LegalName,
            OrganisationNumber = company.OrganisationNumber,
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

        inserted += await AddIfMissingAsync(context, fixture.Site.Id, () => new Site
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

        inserted += await AddIfMissingAsync(context, fixture.Warehouse.Id, () => new Warehouse
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
        inserted += await AddIfMissingAsync(context, year.Id, () => new FiscalYear
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
            inserted += await AddIfMissingAsync(context, period.Id, () => new FiscalPeriod
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

    private static async Task<int> AddIfMissingAsync<T>(KernelDbContext context, Guid id, Func<T> create, CancellationToken cancellationToken)
        where T : MasterDataRow
    {
        if (await context.Set<T>().AnyAsync(row => row.Id == id, cancellationToken))
        {
            return 0;
        }

        context.Set<T>().Add(create());
        return 1;
    }
}
