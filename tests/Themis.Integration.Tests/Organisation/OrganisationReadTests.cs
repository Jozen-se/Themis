using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Themis.Integration.Tests.Pipeline;
using Themis.Kernel.Contracts.Organisation;
using Themis.Kernel.Contracts.Persistence;
using Themis.Kernel.Organisation;
using Themis.Kernel.Primitives;

namespace Themis.Integration.Tests.Organisation;

/// <summary>
/// The step 3 read contracts on the seeded fixture: company and warehouse reads, business dates in the company's
/// time zone (R-TIME-01), fiscal period resolution with R-LED-04, and the company GET endpoint (COMPANY.READ).
/// </summary>
[Collection(PostgresTestGroup.Name)]
[Trait("Category", "Integration")]
public sealed class OrganisationReadTests : IAsyncLifetime
{
    private readonly PostgresFixture _db;
    private PipelineTestHost _host = null!;

    public OrganisationReadTests(PostgresFixture db)
    {
        _db = db;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _host = await PipelineTestHost.StartAsync(_db);

    public async ValueTask DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    public async Task The_company_and_the_warehouse_with_its_site_and_company_are_read_on_the_kernel_role()
    {
        IOrganisationReader reader = _host.Services.GetRequiredService<IOrganisationReader>();
        await using IKernelUnitOfWork unitOfWork = await _host.Services.GetRequiredService<IUnitOfWorkFactory>().BeginAsync(Ct);

        CompanyInfo? company = await reader.GetCompanyAsync(unitOfWork, Milestone1OrganisationFixture.CompanyId, Ct);
        WarehouseInfo? warehouse = await reader.GetWarehouseAsync(unitOfWork, Milestone1OrganisationFixture.WarehouseId, Ct);

        Assert.NotNull(company);
        Assert.Equal("SE01", company.Code.Value);
        Assert.Equal("SEK", company.BaseCurrency.Value);
        Assert.Equal("Europe/Stockholm", company.TimeZone);
        Assert.Equal(RoundingMode.HalfAwayFromZero, company.RoundingMode);
        Assert.Equal(TaxRoundingLevel.Line, company.TaxRoundingLevel);
        Assert.Equal(CompanyStatus.Active, company.Status);
        Assert.NotNull(warehouse);
        Assert.True(warehouse.IsDefault);
        Assert.Equal(Milestone1OrganisationFixture.SiteId, warehouse.Site.Id);
        Assert.Equal(company, warehouse.Company);
        Assert.Null(await reader.GetCompanyAsync(unitOfWork, Guid.NewGuid(), Ct));
        Assert.Null(await reader.GetWarehouseAsync(unitOfWork, Guid.NewGuid(), Ct));
    }

    [Theory]
    [InlineData("2026-01-31T23:30:00Z", "2026-02-01", 2, FiscalPeriodStatus.Open)]
    [InlineData("2026-06-30T22:30:00Z", "2026-07-01", 7, FiscalPeriodStatus.Open)]
    [InlineData("2026-06-15T12:00:00Z", "2026-06-15", 6, FiscalPeriodStatus.Open)]
    [InlineData("2026-01-15T12:00:00Z", "2026-01-15", 1, FiscalPeriodStatus.Closed)]
    [InlineData("2025-12-31T23:30:00Z", "2026-01-01", 1, FiscalPeriodStatus.Closed)]
    public async Task R_TIME_01_and_R_LED_04_an_instant_becomes_a_Stockholm_business_date_that_resolves_to_its_period(string instant, string expectedDate, int expectedPeriod, FiscalPeriodStatus expectedStatus)
    {
        IOrganisationReader reader = _host.Services.GetRequiredService<IOrganisationReader>();
        await using IKernelUnitOfWork unitOfWork = await _host.Services.GetRequiredService<IUnitOfWorkFactory>().BeginAsync(Ct);
        CompanyInfo company = (await reader.GetCompanyAsync(unitOfWork, Milestone1OrganisationFixture.CompanyId, Ct))!;

        BusinessDate date = reader.BusinessDateFor(company, DateTimeOffset.Parse(instant, System.Globalization.CultureInfo.InvariantCulture));
        Result<FiscalPeriodInfo> period = await reader.ResolveFiscalPeriodAsync(unitOfWork, company.Id, date, Ct);

        Assert.Equal(BusinessDate.Parse(expectedDate), date);
        Assert.True(period.IsSuccess);
        Assert.Equal(expectedPeriod, period.Value.PeriodNumber);
        Assert.Equal(expectedStatus, period.Value.Status);
        Assert.Equal(Milestone1OrganisationFixture.PeriodId(expectedPeriod), period.Value.Id);
        Assert.Equal(Milestone1OrganisationFixture.FiscalYearId, period.Value.FiscalYearId);
    }

    [Theory]
    [InlineData("2025-12-31")]
    [InlineData("2027-01-01")]
    public async Task R_LED_04_a_date_outside_every_fiscal_year_is_rejected(string date)
    {
        IOrganisationReader reader = _host.Services.GetRequiredService<IOrganisationReader>();
        await using IKernelUnitOfWork unitOfWork = await _host.Services.GetRequiredService<IUnitOfWorkFactory>().BeginAsync(Ct);

        Result<FiscalPeriodInfo> period = await reader.ResolveFiscalPeriodAsync(unitOfWork, Milestone1OrganisationFixture.CompanyId, BusinessDate.Parse(date), Ct);

        Assert.True(period.IsFailure);
        Assert.Equal("R-LED-04", period.Primary!.RuleId);
        Assert.Equal("fiscal_period_not_found", period.Primary.Code);
    }

    [Fact]
    public async Task GET_company_returns_the_company_with_decimals_as_strings_and_integers_as_numbers_and_needs_COMPANY_READ()
    {
        // D-47: the preparer holds COMPANY.READ in SE01; a subject the ERP does not know is refused (step 4 brief).
        CommandReply reply = await _host.GetAsync($"/v1/kernel/companies/{Milestone1OrganisationFixture.CompanyId}", subject: TestUsers.PreparerSubject, cancellationToken: Ct);
        CommandReply forbidden = await _host.GetAsync($"/v1/kernel/companies/{Milestone1OrganisationFixture.CompanyId}", subject: "nobody-" + Guid.NewGuid(), cancellationToken: Ct);
        CommandReply missing = await _host.GetAsync($"/v1/kernel/companies/{Guid.NewGuid()}", cancellationToken: Ct);
        CommandReply malformed = await _host.GetAsync("/v1/kernel/companies/not-a-guid", cancellationToken: Ct);

        Assert.Equal(200, reply.Status);
        Assert.Equal("SE01", reply.Json!["code"]!.GetValue<string>());
        Assert.Equal("SEK", reply.Json["baseCurrency"]!.GetValue<string>());
        Assert.Equal("Europe/Stockholm", reply.Json["timeZone"]!.GetValue<string>());
        Assert.Equal("Line", reply.Json["taxRoundingLevel"]!.GetValue<string>());
        Assert.Equal(JsonValueKind.Number, reply.Json["version"]!.GetValueKind());
        Assert.Equal("\"1\"", reply.ETag);
        Assert.Equal(403, forbidden.Status);
        Assert.Equal("R-SEC-01", forbidden.RuleId);
        Assert.Equal(404, missing.Status);
        Assert.Equal("company_not_found", missing.Code);
        Assert.Equal(400, malformed.Status);
    }
}
