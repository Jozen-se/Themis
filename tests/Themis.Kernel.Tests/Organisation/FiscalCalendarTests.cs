using Themis.Kernel.Contracts.Organisation;
using Themis.Kernel.Organisation;
using Themis.Kernel.Primitives;

namespace Themis.Kernel.Tests.Organisation;

/// <summary>Fiscal years and their periods (Phase 0 Specification, FiscalYear and FiscalPeriod; R-TIME-01), the duplicate key (R-MD-01) and the step 3 fixture.</summary>
[Trait("Category", "Unit")]
public sealed class FiscalCalendarTests
{
    [Fact]
    public void A_calendar_year_has_twelve_contiguous_monthly_periods()
    {
        IReadOnlyList<FiscalPeriodDefinition> periods = FiscalCalendar.MonthlyPeriods(FiscalCalendar.YearStart(2026, 1), 12, _ => Guid.NewGuid(), _ => FiscalPeriodStatus.Open);

        Assert.Equal(12, periods.Count);
        Assert.Equal(new DateOnly(2026, 1, 1), periods[0].StartDate.Value);
        Assert.Equal(new DateOnly(2026, 2, 28), periods[1].EndDate.Value);
        Assert.Equal(new DateOnly(2026, 12, 31), periods[11].EndDate.Value);
        Assert.All(periods.Zip(periods.Skip(1)), pair => Assert.Equal(pair.First.EndDate.AddDays(1), pair.Second.StartDate));
        Assert.Equal(new DateOnly(2026, 12, 31), FiscalCalendar.YearEnd(FiscalCalendar.YearStart(2026, 1), 12).Value);
    }

    [Fact]
    public void A_broken_fiscal_year_crosses_the_calendar_year_and_a_leap_February_has_29_days()
    {
        IReadOnlyList<FiscalPeriodDefinition> periods = FiscalCalendar.MonthlyPeriods(FiscalCalendar.YearStart(2027, 7), 12, _ => Guid.NewGuid(), _ => FiscalPeriodStatus.Open);

        Assert.Equal(new DateOnly(2027, 7, 1), periods[0].StartDate.Value);
        Assert.Equal(new DateOnly(2028, 2, 29), periods[7].EndDate.Value);
        Assert.Equal(new DateOnly(2028, 6, 30), periods[11].EndDate.Value);
    }

    [Fact]
    public void An_extended_first_year_may_run_up_to_18_months_and_no_more()
    {
        Assert.Equal(18, FiscalCalendar.MonthlyPeriods(FiscalCalendar.YearStart(2026, 7), 18, _ => Guid.NewGuid(), _ => FiscalPeriodStatus.Open).Count);
        Assert.Throws<ArgumentOutOfRangeException>(() => FiscalCalendar.MonthlyPeriods(FiscalCalendar.YearStart(2026, 7), 19, _ => Guid.NewGuid(), _ => FiscalPeriodStatus.Open));
        Assert.Throws<ArgumentOutOfRangeException>(() => FiscalCalendar.YearStart(2026, 13));
        Assert.Throws<ArgumentException>(() => FiscalCalendar.MonthlyPeriods(BusinessDate.Parse("2026-01-15"), 12, _ => Guid.NewGuid(), _ => FiscalPeriodStatus.Open));
    }

    [Theory]
    [InlineData("556677-8899", "SE556677889901", "Themis Demo AB", null, "5566778899|SE556677889901|THEMISDEMOAB|")]
    [InlineData(null, null, " Nordic  Fasteners  ", "SE-123 45", "||NORDICFASTENERS|SE12345")]
    public void R_MD_01_the_duplicate_key_normalises_organisation_number_vat_number_name_and_postal_code(string? organisationNumber, string? vatNumber, string name, string? postalCode, string expected)
    {
        Assert.Equal(expected, DuplicateKeys.ForParty(organisationNumber, vatNumber, name, postalCode));
    }

    [Fact]
    public void The_milestone_1_fixture_matches_the_brief()
    {
        OrganisationFixtureDefinition fixture = Milestone1OrganisationFixture.Definition;

        Assert.Equal("SEK", fixture.Company.BaseCurrency.Value);
        Assert.Equal("Europe/Stockholm", fixture.Company.TimeZone);
        Assert.Equal(TaxRoundingLevel.Line, fixture.Company.TaxRoundingLevel);
        Assert.Equal(fixture.CompanyParty.Id, fixture.Company.PartyId);
        Assert.Equal("SE", fixture.CompanyParty.CountryCode.Value);
        Assert.Equal(fixture.Company.Id, fixture.Site.CompanyId);
        Assert.Equal(fixture.Site.Id, fixture.Warehouse.SiteId);
        Assert.Equal(fixture.Company.Id, fixture.Warehouse.CompanyId);
        Assert.Equal("2026", fixture.FiscalYear.Code.Value);
        Assert.Equal(12, fixture.FiscalYear.Periods.Count);
        Assert.Equal(FiscalPeriodStatus.Closed, fixture.FiscalYear.Periods[0].Status);
        Assert.All(fixture.FiscalYear.Periods.Skip(1), period => Assert.Equal(FiscalPeriodStatus.Open, period.Status));
        Assert.Equal(fixture.FiscalYear.StartDate, fixture.FiscalYear.Periods[0].StartDate);
        Assert.Equal(fixture.FiscalYear.EndDate, fixture.FiscalYear.Periods[11].EndDate);
        Assert.Equal(12, fixture.FiscalYear.Periods.Select(period => period.Id).Distinct().Count());
    }

    [Theory]
    [InlineData("2026-01-31T23:30:00Z", "2026-02-01")]
    [InlineData("2026-06-30T22:30:00Z", "2026-07-01")]
    [InlineData("2026-06-15T12:00:00Z", "2026-06-15")]
    [InlineData("2026-03-29T00:30:00Z", "2026-03-29")]
    public void R_TIME_01_the_business_date_of_an_instant_is_its_calendar_date_in_the_company_time_zone(string instant, string expected)
    {
        CompanyInfo company = new(Guid.NewGuid(), Code.From("SE01"), Guid.NewGuid(), "Test", "555555-5555", null, CountryCode.From("SE"), CurrencyCode.From("SEK"), "Europe/Stockholm", 1, Guid.NewGuid(), RoundingMode.HalfAwayFromZero, TaxRoundingLevel.Line, CompanyStatus.Active, 1);

        BusinessDate date = new OrganisationReader().BusinessDateFor(company, DateTimeOffset.Parse(instant, System.Globalization.CultureInfo.InvariantCulture));

        Assert.Equal(BusinessDate.Parse(expected), date);
    }

    [Fact]
    public void Country_codes_are_two_upper_case_letters()
    {
        Assert.Equal("SE", CountryCode.From("se").Value);
        Assert.False(CountryCode.TryFrom("SWE", out _));
        Assert.False(CountryCode.TryFrom("s1", out _));
    }
}
