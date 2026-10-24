using Themis.Kernel.Contracts.Organisation;
using Themis.Kernel.Primitives;

namespace Themis.Kernel.Organisation;

/// <summary>
/// The organisation fixture of milestone 1 step 3 (approved step brief): installation THEMIS-DEV (sv-SE, Active);
/// the company's own party (Organisation, SE; its number comes from the installation-level PARTY series when the
/// seeder inserts it, milestone 2 step 3, D-64, so the first fixture party is P-000001); company SE01 with a
/// clearly fictitious legal name and organisation number, SE, SEK, Europe/Stockholm, fiscal year start month 1,
/// HalfAwayFromZero, tax rounding level Line (test value until D-10), no payment tolerance, chart BAS (header only),
/// Active; one site; one default warehouse; fiscal year 2026 (2026-01-01 to 2026-12-31), Open, 12 monthly periods,
/// period 1 Closed and periods 2 to 12 Open. Every value is test data; the identifiers are fixed so that seeding is
/// idempotent and tests can name the rows.
/// </summary>
internal static class Milestone1OrganisationFixture
{
    public static Guid InstallationId { get; } = Guid.Parse("00000000-0000-7000-8000-000000000101");
    public static Guid CompanyPartyId { get; } = Guid.Parse("00000000-0000-7000-8000-000000000102");
    public static Guid ChartOfAccountsId { get; } = Guid.Parse("00000000-0000-7000-8000-000000000103");
    public static Guid CompanyId { get; } = Guid.Parse("00000000-0000-7000-8000-000000000104");
    public static Guid SiteId { get; } = Guid.Parse("00000000-0000-7000-8000-000000000105");
    public static Guid WarehouseId { get; } = Guid.Parse("00000000-0000-7000-8000-000000000106");
    public static Guid FiscalYearId { get; } = Guid.Parse("00000000-0000-7000-8000-000000000107");

    public const int FiscalYearNumber = 2026;
    public const int FiscalYearStartMonth = 1;
    public const int PeriodsPerYear = 12;

    /// <summary>A clearly fictitious, Luhn-valid organisation number; the country's localisation validates real ones later.</summary>
    public const string OrganisationNumber = "555555-5555";
    public const string VatNumber = "SE555555555501";
    public const string LegalName = "Themis Fictitious Fasteners AB";
    public const string InstallationCode = "THEMIS-DEV";
    public const string CompanyCode = "SE01";

    public static OrganisationFixtureDefinition Definition { get; } = Build();

    public static Guid PeriodId(int periodNumber) => Guid.Parse($"00000000-0000-7000-8000-0000000002{periodNumber:00}");

    private static OrganisationFixtureDefinition Build()
    {
        CountryCode sweden = CountryCode.From("SE");

        BusinessDate yearStart = FiscalCalendar.YearStart(FiscalYearNumber, FiscalYearStartMonth);
        IReadOnlyList<FiscalPeriodDefinition> periods = FiscalCalendar.MonthlyPeriods(
            yearStart,
            PeriodsPerYear,
            PeriodId,
            number => number == 1 ? FiscalPeriodStatus.Closed : FiscalPeriodStatus.Open);

        return new OrganisationFixtureDefinition(
            new InstallationDefinition(InstallationId, Code.From(InstallationCode), "Themis development installation", InstallationStatus.Active, "sv-SE"),
            new PartyDefinition(
                CompanyPartyId,
                PartyType.Organisation,
                LegalName,
                OrganisationNumber,
                VatNumber,
                sweden,
                "sv-SE",
                MasterDataStatus.Active,
                DuplicateKeys.ForParty(OrganisationNumber, VatNumber, LegalName, postalCode: null)),
            new ChartOfAccountsDefinition(ChartOfAccountsId, Code.From("BAS"), "BAS-kontoplan", "BAS 2026"),
            new CompanyDefinition(
                CompanyId,
                Code.From(CompanyCode),
                CompanyPartyId,
                LegalName,
                OrganisationNumber,
                DuplicateKeys.Normalise(OrganisationNumber),
                VatNumber,
                sweden,
                CurrencyCode.From("SEK"),
                "Europe/Stockholm",
                FiscalYearStartMonth,
                ChartOfAccountsId,
                RoundingMode.HalfAwayFromZero,
                TaxRoundingLevel.Line,
                CompanyStatus.Active),
            new SiteDefinition(SiteId, CompanyId, Code.From("MAIN"), "Main site", TimeZone: null),
            new WarehouseDefinition(WarehouseId, SiteId, CompanyId, Code.From("MAIN"), "Main warehouse", IsDefault: true, MasterDataStatus.Active),
            new FiscalYearDefinition(
                FiscalYearId,
                CompanyId,
                Code.From("2026"),
                yearStart,
                FiscalCalendar.YearEnd(yearStart, PeriodsPerYear),
                FiscalYearStatus.Open,
                periods));
    }
}
