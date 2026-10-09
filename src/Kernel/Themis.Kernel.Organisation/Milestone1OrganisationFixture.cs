using Themis.Kernel.Contracts.Organisation;
using Themis.Kernel.Primitives;

namespace Themis.Kernel.Organisation;

/// <summary>
/// The organisation fixture of milestone 1 step 3 (brief): one installation; the company's own party row (name,
/// organisation number, country); one company (SEK, Europe/Stockholm, tax rounding level Line as a test value until
/// D-10); one site; one warehouse; fiscal year 2026 with 12 periods, period 1 Closed and the rest Open. Every value
/// is test data; the identifiers are fixed so that seeding is idempotent and tests can name the rows.
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

    /// <summary>A Luhn-valid Swedish organisation number used as test data; the country's localisation validates real ones later.</summary>
    public const string OrganisationNumber = "556677-8899";
    public const string VatNumber = "SE556677889901";

    public static OrganisationFixtureDefinition Definition { get; } = Build();

    public static Guid PeriodId(int periodNumber) => Guid.Parse($"00000000-0000-7000-8000-0000000002{periodNumber:00}");

    private static OrganisationFixtureDefinition Build()
    {
        CountryCode sweden = CountryCode.From("SE");
        const string legalName = "Themis Demo AB";

        BusinessDate yearStart = FiscalCalendar.YearStart(FiscalYearNumber, FiscalYearStartMonth);
        IReadOnlyList<FiscalPeriodDefinition> periods = FiscalCalendar.MonthlyPeriods(
            yearStart,
            PeriodsPerYear,
            PeriodId,
            number => number == 1 ? FiscalPeriodStatus.Closed : FiscalPeriodStatus.Open);

        return new OrganisationFixtureDefinition(
            new InstallationDefinition(InstallationId, Code.From("DEMO"), "Themis demo installation", InstallationStatus.Active, "sv-SE"),
            new PartyDefinition(
                CompanyPartyId,
                Code.From("P-000001"),
                PartyType.Organisation,
                legalName,
                OrganisationNumber,
                VatNumber,
                sweden,
                "sv-SE",
                MasterDataStatus.Active,
                DuplicateKeys.ForParty(OrganisationNumber, VatNumber, legalName, postalCode: null)),
            new ChartOfAccountsDefinition(ChartOfAccountsId, Code.From("BAS"), "BAS-kontoplan", "BAS 2026"),
            new CompanyDefinition(
                CompanyId,
                Code.From("DEMO"),
                CompanyPartyId,
                legalName,
                OrganisationNumber,
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
