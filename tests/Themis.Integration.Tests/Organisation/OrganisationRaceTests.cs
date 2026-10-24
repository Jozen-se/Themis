using Themis.Integration.Tests.Pipeline;
using Themis.Kernel.Organisation;

namespace Themis.Integration.Tests.Organisation;

/// <summary>
/// RR-45 for the organisation commands (milestone 2 step 3 brief, Tests): two creates racing past the handler's
/// duplicate check end with one row and the mapped R-MD-05 rejection stored for the loser, for each unique index
/// behind the rule: the chart code, the company code, the organisation number per country, the site code per company
/// and the warehouse code per company. The organisation create seam holds the first create after its checks while the
/// second commits (docs/development/test-seams.md).
/// </summary>
[Collection(PostgresTestGroup.Name)]
[Trait("Category", "Integration")]
public sealed class OrganisationRaceTests : IAsyncLifetime
{
    private readonly PostgresFixture _db;
    private PipelineTestHost _host = null!;

    public OrganisationRaceTests(PostgresFixture db)
    {
        _db = db;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        await _db.TruncateAsync(Ct);
        _host = await PipelineTestHost.StartAsync(_db);
    }

    public async ValueTask DisposeAsync()
    {
        _host.OrganisationCreateSeam.AfterDuplicateChecks = null;
        await _host.DisposeAsync();
    }

    [Theory]
    [InlineData("chart", "ux_chart_of_accounts_code", "chart_of_accounts_code_duplicate", "SELECT count(*) FROM kernel.chart_of_accounts WHERE code = 'RACE'")]
    [InlineData("company-code", "ux_company_code", "company_code_duplicate", "SELECT count(*) FROM kernel.company WHERE code = 'RACE'")]
    [InlineData("organisation-number", "ux_company_country_organisation_number", "organisation_number_duplicate", "SELECT count(*) FROM kernel.company WHERE organisation_number_normalised = '7777777777'")]
    [InlineData("site", "ux_site_company_code", "site_code_duplicate", "SELECT count(*) FROM kernel.site WHERE code = 'RACE'")]
    [InlineData("warehouse", "ux_warehouse_company_code", "warehouse_code_duplicate", "SELECT count(*) FROM kernel.warehouse WHERE code = 'RACE'")]
    public async Task RR_45_two_creates_racing_past_the_duplicate_check_end_with_one_row_and_the_mapped_R_MD_05_rejection_stored_for_the_loser(string which, string index, string code, string countSql)
    {
        (string route, object loserBody, object winnerBody, string heldEntity, string heldCode) = which switch
        {
            "chart" => (OrganisationBodies.Charts, new { code = "RACE", name = "Race chart" }, new { code = "race", name = "Race chart" }, "ChartOfAccounts", "RACE"),
            "company-code" => (OrganisationBodies.Companies, OrganisationBodies.Company("RACE", organisationNumber: "111111-1111", vatNumber: null), OrganisationBodies.Company("race", organisationNumber: "222222-2222", vatNumber: null), "Company", "RACE"),
            "organisation-number" => (OrganisationBodies.Companies, OrganisationBodies.Company("RACE-A", organisationNumber: "777777-7777", vatNumber: null), OrganisationBodies.Company("RACE-B", organisationNumber: "7777 777 777", vatNumber: null), "Company", "RACE-A"),
            "site" => (OrganisationBodies.Sites, new { companyId = Milestone1OrganisationFixture.CompanyId, code = "RACE", name = "Race site" }, new { companyId = Milestone1OrganisationFixture.CompanyId, code = "race", name = "Race site" }, "Site", "RACE"),
            _ => (OrganisationBodies.Warehouses, new { siteId = Milestone1OrganisationFixture.SiteId, code = "RACE", name = "Race warehouse" }, new { siteId = Milestone1OrganisationFixture.SiteId, code = "race", name = "Race warehouse" }, "Warehouse", "RACE"),
        };

        TaskCompletionSource paused = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int holds = 0;
        _host.OrganisationCreateSeam.AfterDuplicateChecks = async (entityType, entityCode, cancellationToken) =>
        {
            if (entityType == heldEntity && entityCode.Value == heldCode && Interlocked.Exchange(ref holds, 1) == 0)
            {
                paused.TrySetResult();
                await release.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
            }
        };

        Task<CommandReply> loser = _host.PostAsync(route, loserBody, $"rr45-{which}-loser", cancellationToken: Ct);
        await paused.Task.WaitAsync(TimeSpan.FromSeconds(15), Ct);
        CommandReply winner = await _host.PostAsync(route, winnerBody, $"rr45-{which}-winner", cancellationToken: Ct);
        Assert.True(winner.Status == 200, winner.Body);
        release.SetResult();
        CommandReply lost = await loser;

        Assert.Equal(422, lost.Status);
        Assert.Equal(("R-MD-05", code), (lost.RuleId, lost.Code));
        Assert.Contains(_host.Logs.Lines, line => line.Message.Contains(index, StringComparison.Ordinal) && line.Message.Contains("23505", StringComparison.Ordinal) && line.Message.Contains("RR-45", StringComparison.Ordinal));
        Assert.Equal(1L, await _host.ScalarAsync(countSql, Ct));
        // Stored like any other rejection (R-API-03, R-API-01): the replay returns the same rejection.
        CommandReply replay = await _host.PostAsync(route, loserBody, $"rr45-{which}-loser", cancellationToken: Ct);
        Assert.Equal(422, replay.Status);
        Assert.True(replay.Replayed);
        Assert.Equal(code, replay.Code);

        if (route == OrganisationBodies.Companies)
        {
            // The loser's party and PARTY number rolled back with its transaction (D-64): one party beside the fixture's, the series after it.
            Assert.Equal(2L, await _host.CountAsync("kernel.party", Ct));
            Assert.Equal(3L, await _host.ScalarAsync("SELECT next_number FROM kernel.number_series WHERE company_id IS NULL AND document_type = 'PARTY'", Ct));
        }
    }
}
