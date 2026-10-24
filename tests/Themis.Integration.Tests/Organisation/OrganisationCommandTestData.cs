using System.Text.Json.Nodes;
using Npgsql;
using Themis.Integration.Tests.Pipeline;
using Themis.Kernel.Contracts.Security;
using Themis.Kernel.Identity;
using Themis.Kernel.Organisation;

namespace Themis.Integration.Tests.Organisation;

/// <summary>
/// The users and grants the organisation command tests need beside the fixture (milestone 2 step 3): organisation
/// administrators limited to one site, written as test data like <see cref="Identity.IdentityTestData"/> and removed
/// on dispose, and the fixture administrator's assignment in a company a test created, which
/// <see cref="PostgresFixture.TruncateAsync"/> removes with the company.
/// </summary>
internal sealed class OrganisationTestUsers : IAsyncDisposable
{
    private readonly PostgresFixture _db;
    private readonly List<Guid> _users = [];

    public OrganisationTestUsers(PostgresFixture db)
    {
        _db = db;
    }

    /// <summary>A user holding ORGANISATION_ADMINISTRATOR in the company, limited to the site when one is given; returns the token subject.</summary>
    public async Task<string> AddAdministratorAsync(Guid company, Guid? site, CancellationToken cancellationToken)
    {
        Guid user = Guid.NewGuid();
        string subject = "orgadmin-" + Guid.NewGuid().ToString("N");
        string system = WellKnownUsers.System.Value.ToString();
        await RunAsync($"INSERT INTO kernel.user_account (id, external_subject, display_name, is_service_account, status, created_at, created_by, updated_at, updated_by) VALUES ('{user}', '{subject}', 'Site administrator', false, 'Active', now(), '{system}', now(), '{system}')", cancellationToken);
        _users.Add(user);
        await RunAsync(Assignment(user, company, site, system), cancellationToken);
        return subject;
    }

    /// <summary>The fixture organisation administrator gets a company-wide assignment in a company a test created, so the company-scoped commands and reads on it run (R-SEC-01).</summary>
    public static Task GrantFixtureAdministratorAsync(PipelineTestHost host, Guid company, CancellationToken cancellationToken) =>
        host.ScalarAsync(Assignment(TestUsers.OrganisationAdministrator, company, site: null, WellKnownUsers.System.Value.ToString()), cancellationToken);

    public async ValueTask DisposeAsync()
    {
        foreach (Guid user in _users)
        {
            // D-60: audit events are insert-only even for the table owner, so their cleanup goes through the fixture's cleanup seam.
            await _db.CleanupAsync($"DELETE FROM kernel.audit_event WHERE user_id = '{user}'", CancellationToken.None);
            await RunAsync($"DELETE FROM kernel.idempotency_record WHERE user_id = '{user}'; DELETE FROM kernel.role_assignment WHERE user_id = '{user}'; DELETE FROM kernel.user_account WHERE id = '{user}'", CancellationToken.None);
        }
    }

    private static string Assignment(Guid user, Guid company, Guid? site, string system) =>
        "INSERT INTO kernel.role_assignment (id, user_id, role_id, company_id, site_id, valid_from, valid_to, assigned_by, created_at, created_by, updated_at, updated_by) " +
        $"VALUES ('{Guid.NewGuid()}', '{user}', '{Milestone1IdentityFixture.OrganisationAdministratorRoleId}', '{company}', {(site is null ? "NULL" : $"'{site}'")}, '2026-01-01', NULL, '{system}', now(), '{system}', now(), '{system}')";

    private async Task RunAsync(string sql, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await PostgresFixture.OpenAsync(_db.ConnectionStringFor("themis_migrator"), cancellationToken);
        await using NpgsqlCommand command = new(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}

/// <summary>The request bodies of the organisation commands with the fixture's values as defaults.</summary>
internal static class OrganisationBodies
{
    public const string Charts = "/v1/kernel/charts-of-accounts";
    public const string Companies = "/v1/kernel/companies";
    public const string Sites = "/v1/kernel/sites";
    public const string Warehouses = "/v1/kernel/warehouses";

    public static string ChartUpdate(Guid id) => $"{Charts}/{id}/update";

    public static string CompanyUpdate(Guid id) => $"{Companies}/{id}/update";

    public static string SiteUpdate(Guid id) => $"{Sites}/{id}/update";

    public static string WarehouseUpdate(Guid id) => $"{Warehouses}/{id}/update";

    public static object Company(
        string code,
        string legalName = "Nordic Bolts AB",
        string organisationNumber = "556677-8899",
        string? vatNumber = "SE556677889901",
        string countryCode = "SE",
        string baseCurrency = "EUR",
        string timeZone = "Europe/Stockholm",
        int fiscalYearStartMonth = 1,
        Guid? chartOfAccountsId = null,
        string? taxRoundingLevel = "Line",
        string? roundingMode = null) =>
        new { code, legalName, organisationNumber, vatNumber, countryCode, baseCurrency, timeZone, fiscalYearStartMonth, chartOfAccountsId = chartOfAccountsId ?? Milestone1OrganisationFixture.ChartOfAccountsId, taxRoundingLevel, roundingMode };

    /// <summary>COMPANY.UPDATE restates every changeable attribute; these are the fixture company's values, with a test's changes.</summary>
    public static object Se01Update(
        string legalName = Milestone1OrganisationFixture.LegalName,
        string organisationNumber = Milestone1OrganisationFixture.OrganisationNumber,
        string? vatNumber = Milestone1OrganisationFixture.VatNumber,
        string countryCode = "SE",
        string baseCurrency = "SEK",
        string timeZone = "Europe/Stockholm",
        int fiscalYearStartMonth = 1,
        Guid? chartOfAccountsId = null,
        string? roundingMode = "HalfAwayFromZero",
        string? taxRoundingLevel = "Line",
        string? code = null) =>
        new { legalName, organisationNumber, vatNumber, countryCode, baseCurrency, timeZone, fiscalYearStartMonth, chartOfAccountsId = chartOfAccountsId ?? Milestone1OrganisationFixture.ChartOfAccountsId, roundingMode, taxRoundingLevel, code };

    /// <summary>The D-50 envelope of the one outbox event of the type: aggregate type and id, company (none for an installation-scope aggregate) and command.</summary>
    public static async Task<(string Type, string Aggregate, Guid Id, Guid? Company, string Command)> EnvelopeAsync(PipelineTestHost host, string eventType, CancellationToken cancellationToken)
    {
        string json = (string)(await host.ScalarAsync("SELECT to_jsonb(e) FROM (SELECT event_type, aggregate_type, aggregate_id, company_id, command_name FROM kernel.outbox_event WHERE event_type = @type) e", cancellationToken, ("type", eventType)))!;
        JsonNode row = JsonNode.Parse(json)!;
        return (row["event_type"]!.GetValue<string>(), row["aggregate_type"]!.GetValue<string>(), row["aggregate_id"]!.GetValue<Guid>(), row["company_id"] is { } company ? company.GetValue<Guid>() : null, row["command_name"]!.GetValue<string>());
    }
}
