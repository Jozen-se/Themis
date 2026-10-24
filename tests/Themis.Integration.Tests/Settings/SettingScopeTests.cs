using Npgsql;
using Themis.Integration.Tests.Pipeline;
using Themis.Kernel.Identity;
using Themis.Kernel.Organisation;

namespace Themis.Integration.Tests.Settings;

/// <summary>
/// The scope of the settings commands and query (D-63, RR-102, RR-52) and the event envelope of their writes (D-50),
/// on a database of their own, seeded like the fixture's, so the site-limited users and the second site of these
/// tests never reach the shared database. A site-limited assignment of SETTINGS_ADMINISTRATOR for the warehouse's
/// site may set and read a warehouse value; one for another site gets 403; a site-limited assignment satisfies
/// neither installation nor company scope.
/// </summary>
[Collection(PostgresTestGroup.Name)]
[Trait("Category", "Integration")]
public sealed class SettingScopeTests : IAsyncLifetime
{
    private const string Set = "/v1/kernel/settings/set";
    private const string Key = SettingTestData.AllLevelsKey;

    private const string MainSiteAdministratorSubject = "scope-main-site-administrator";
    private const string OtherSiteAdministratorSubject = "scope-other-site-administrator";

    private static readonly Guid Company = Milestone1OrganisationFixture.CompanyId;
    private static readonly Guid Site = Milestone1OrganisationFixture.SiteId;
    private static readonly Guid Warehouse = Milestone1OrganisationFixture.WarehouseId;
    private static readonly Guid OtherSite = Guid.Parse("00000000-0000-7000-8000-00000000a105");
    private static readonly Guid OtherCompany = Guid.Parse("00000000-0000-7000-8000-00000000b104");
    private static readonly Guid OtherCompanySite = Guid.Parse("00000000-0000-7000-8000-00000000b105");
    private static readonly Guid OtherCompanyWarehouse = Guid.Parse("00000000-0000-7000-8000-00000000b106");

    private readonly PostgresFixture _db;
    private string _database = null!;
    private PipelineTestHost _host = null!;

    public SettingScopeTests(PostgresFixture db)
    {
        _db = db;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        string connectionString = await _db.CreateSeededDatabaseAsync(Ct);
        _database = new NpgsqlConnectionStringBuilder(connectionString).Database!;
        _host = await PipelineTestHost.StartAsync(_db, _database);
        await SettingTestData.RegisterAsync(_host, new NpgsqlConnectionStringBuilder(connectionString) { Username = "themis_migrator", Password = PostgresFixture.TestRolePassword }.ConnectionString, Ct);

        // A second site in the same company, and two users who hold SETTINGS_ADMINISTRATOR for one site each.
        await _host.ScalarAsync(
            "INSERT INTO kernel.site (id, company_id, code, name, created_at, created_by, updated_at, updated_by, version, extension) " +
            "VALUES (@id, @company, 'NORTH', 'North site', now(), @system, now(), @system, 1, '{}'::jsonb)",
            Ct, ("id", OtherSite), ("company", Company), ("system", Kernel.Contracts.Security.WellKnownUsers.System.Value));
        // A second company with its own site and warehouse, for the targets that belong to another company than the body names.
        // Since milestone 2 step 3 a company has a party of its own (ux_company_party, D-64) and a normalised organisation number (R-MD-05).
        string audit = "now(), @system, now(), @system";
        Guid otherParty = Guid.NewGuid();
        await _host.ScalarAsync(
            $"INSERT INTO kernel.party (id, party_number, party_type, name, organisation_number, country_code, status, duplicate_key, created_at, created_by, updated_at, updated_by) VALUES (@id, 'P-TEST02', 'Organisation', 'Other company', '5555555556', 'SE', 'Active', '5555555556||OTHERCOMPANY|', {audit})",
            Ct, ("id", otherParty), ("system", Kernel.Contracts.Security.WellKnownUsers.System.Value));
        await _host.ScalarAsync(
            "INSERT INTO kernel.company (id, code, party_id, legal_name, organisation_number, organisation_number_normalised, country_code, base_currency, time_zone, fiscal_year_start_month, chart_of_accounts_id, rounding_mode, tax_rounding_level, status, created_at, created_by, updated_at, updated_by) " +
            $"VALUES (@id, 'SE02', @party, 'Other company', '5555555556', '5555555556', 'SE', 'SEK', 'Europe/Stockholm', 1, @chart, 'HalfAwayFromZero', 'Line', 'Active', {audit})",
            Ct, ("id", OtherCompany), ("party", otherParty), ("chart", Milestone1OrganisationFixture.ChartOfAccountsId), ("system", Kernel.Contracts.Security.WellKnownUsers.System.Value));
        await _host.ScalarAsync($"INSERT INTO kernel.site (id, company_id, code, name, created_at, created_by, updated_at, updated_by) VALUES (@id, @company, 'MAIN', 'Other main', {audit})",
            Ct, ("id", OtherCompanySite), ("company", OtherCompany), ("system", Kernel.Contracts.Security.WellKnownUsers.System.Value));
        await _host.ScalarAsync($"INSERT INTO kernel.warehouse (id, site_id, company_id, code, name, is_default, status, created_at, created_by, updated_at, updated_by) VALUES (@id, @site, @company, 'MAIN', 'Other warehouse', true, 'Active', {audit})",
            Ct, ("id", OtherCompanyWarehouse), ("site", OtherCompanySite), ("company", OtherCompany), ("system", Kernel.Contracts.Security.WellKnownUsers.System.Value));

        await AddSiteAdministratorAsync(MainSiteAdministratorSubject, Site);
        await AddSiteAdministratorAsync(OtherSiteAdministratorSubject, OtherSite);
    }

    public async ValueTask DisposeAsync()
    {
        await _host.DisposeAsync();
        await _db.DropDatabaseAsync(new NpgsqlConnectionStringBuilder(_db.SuperuserConnectionString) { Database = _database }.ConnectionString, CancellationToken.None);
    }

    [Fact]
    public async Task RR_102_D_63_a_site_limited_settings_administrator_for_the_warehouses_site_sets_and_reads_a_warehouse_value_and_one_for_another_site_gets_403()
    {
        CommandReply set = await _host.PostAsync(Set, AtWarehouse("12"), "scope-wh-main", ifMatch: "\"0\"", subject: MainSiteAdministratorSubject, cancellationToken: Ct);
        Assert.True(set.Status == 200, set.Body);
        // The site serves the scope only; the value row names its company and warehouse (ck_setting_value_target).
        Assert.Null(await _host.ScalarAsync("SELECT site_id FROM kernel.setting_value WHERE id = @id", Ct, ("id", set.Json!["id"]!.GetValue<Guid>())));

        CommandReply read = await _host.GetAsync($"/v1/kernel/settings/{Key}?companyId={Company}&warehouseId={Warehouse}", subject: MainSiteAdministratorSubject, cancellationToken: Ct);
        Assert.True(read.Status == 200, read.Body);
        Assert.Equal("12", read.Json!["value"]!.GetValue<string>());
        Assert.Equal("Warehouse", read.Json["source"]!.GetValue<string>());

        CommandReply otherSet = await _host.PostAsync(Set, AtWarehouse("13"), "scope-wh-other", ifMatch: "\"1\"", subject: OtherSiteAdministratorSubject, cancellationToken: Ct);
        Assert.Equal(403, otherSet.Status);
        CommandReply otherRead = await _host.GetAsync($"/v1/kernel/settings/{Key}?companyId={Company}&warehouseId={Warehouse}", subject: OtherSiteAdministratorSubject, cancellationToken: Ct);
        Assert.Equal(403, otherRead.Status);
        Assert.Equal(1L, await _host.ScalarAsync("SELECT count(*) FROM kernel.setting_value WHERE definition_key = @key", Ct, ("key", Key)));
    }

    [Fact]
    public async Task RR_102_D_63_a_warehouse_that_does_not_exist_is_a_400_stored_as_the_keys_result_not_a_scope_failure()
    {
        object body = new { key = Key, level = "Warehouse", value = "5", companyId = Company, warehouseId = Guid.NewGuid() };

        CommandReply unknown = await _host.PostAsync(Set, body, "scope-wh-unknown", ifMatch: "\"0\"", cancellationToken: Ct);
        Assert.Equal(400, unknown.Status);
        Assert.Equal("warehouse_unknown", unknown.Code);

        CommandReply replay = await _host.PostAsync(Set, body, "scope-wh-unknown", ifMatch: "\"0\"", cancellationToken: Ct);
        Assert.Equal(400, replay.Status);
        Assert.True(replay.Replayed);
    }

    [Theory]
    [InlineData("Site", "site_other_company")]
    [InlineData("Warehouse", "warehouse_other_company")]
    [InlineData("UnknownSite", "site_unknown")]
    public async Task RR_102_D_63_a_site_or_warehouse_of_another_company_or_an_unknown_site_declares_the_bodys_company_and_is_a_400_stored_as_the_keys_result(string target, string code)
    {
        object body = target switch
        {
            "Site" => new { key = Key, level = "Site", value = "5", companyId = Company, siteId = OtherCompanySite },
            "Warehouse" => new { key = Key, level = "Warehouse", value = "5", companyId = Company, warehouseId = OtherCompanyWarehouse },
            _ => new { key = Key, level = "Site", value = "5", companyId = Company, siteId = Guid.NewGuid() },
        };
        string idempotencyKey = $"scope-foreign-{target}";

        CommandReply refused = await _host.PostAsync(Set, body, idempotencyKey, ifMatch: "\"0\"", cancellationToken: Ct);
        Assert.True(refused.Status == 400, refused.Body);
        Assert.Equal(code, refused.Code);

        CommandReply replay = await _host.PostAsync(Set, body, idempotencyKey, ifMatch: "\"0\"", cancellationToken: Ct);
        Assert.Equal(400, replay.Status);
        Assert.True(replay.Replayed);
        Assert.Equal(0L, await _host.ScalarAsync("SELECT count(*) FROM kernel.setting_value WHERE definition_key = @key", Ct, ("key", Key)));
    }

    [Fact]
    public async Task RR_102_D_63_SETTING_READ_of_an_unknown_warehouse_or_one_of_another_company_is_404()
    {
        CommandReply unknown = await _host.GetAsync($"/v1/kernel/settings/{Key}?companyId={Company}&warehouseId={Guid.NewGuid()}", cancellationToken: Ct);
        Assert.Equal(404, unknown.Status);
        Assert.Equal("warehouse_not_found", unknown.Code);

        CommandReply foreign = await _host.GetAsync($"/v1/kernel/settings/{Key}?companyId={Company}&warehouseId={OtherCompanyWarehouse}", cancellationToken: Ct);
        Assert.Equal(404, foreign.Status);
        Assert.Equal("warehouse_not_found", foreign.Code);
    }

    [Fact]
    public async Task RR_52_an_installation_write_needs_installation_scope_and_a_company_write_needs_the_company_which_a_site_limited_assignment_gives_neither()
    {
        CommandReply installation = await _host.PostAsync(Set, new { key = Key, level = "Installation", value = "1" }, "scope-inst-site", ifMatch: "\"0\"", subject: MainSiteAdministratorSubject, cancellationToken: Ct);
        Assert.Equal(403, installation.Status);
        CommandReply company = await _host.PostAsync(Set, new { key = Key, level = "Company", value = "2", companyId = Company }, "scope-co-site", ifMatch: "\"0\"", subject: MainSiteAdministratorSubject, cancellationToken: Ct);
        Assert.Equal(403, company.Status);
        CommandReply site = await _host.PostAsync(Set, new { key = Key, level = "Site", value = "3", companyId = Company, siteId = Site }, "scope-site-site", ifMatch: "\"0\"", subject: MainSiteAdministratorSubject, cancellationToken: Ct);
        Assert.True(site.Status == 200, site.Body);

        // The company-wide settings administrator satisfies installation scope (RR-52, one installation per company group).
        CommandReply companyWide = await _host.PostAsync(Set, new { key = Key, level = "Installation", value = "1" }, "scope-inst-company", ifMatch: "\"0\"", cancellationToken: Ct);
        Assert.True(companyWide.Status == 200, companyWide.Body);
        CommandReply companyLevel = await _host.PostAsync(Set, new { key = Key, level = "Company", value = "2", companyId = Company }, "scope-co-company", ifMatch: "\"0\"", cancellationToken: Ct);
        Assert.True(companyLevel.Status == 200, companyLevel.Body);
    }

    [Fact]
    public async Task D_50_the_SETTING_CHANGED_envelope_carries_the_company_for_company_site_and_warehouse_values_and_none_for_an_installation_value()
    {
        Guid installation = await WriteAsync(new { key = Key, level = "Installation", value = "1" }, "env-inst");
        Guid company = await WriteAsync(new { key = Key, level = "Company", value = "2", companyId = Company }, "env-co");
        Guid site = await WriteAsync(new { key = Key, level = "Site", value = "3", companyId = Company, siteId = Site }, "env-site");
        Guid warehouse = await WriteAsync(AtWarehouse("4"), "env-wh");

        const string envelope = "SELECT company_id FROM kernel.outbox_event WHERE aggregate_id = @id AND event_type = 'SETTING_CHANGED'";
        Assert.Null(await _host.ScalarAsync(envelope, Ct, ("id", installation)));
        Assert.Equal(Company, await _host.ScalarAsync(envelope, Ct, ("id", company)));
        Assert.Equal(Company, await _host.ScalarAsync(envelope, Ct, ("id", site)));
        Assert.Equal(Company, await _host.ScalarAsync(envelope, Ct, ("id", warehouse)));
        Assert.Equal(4L, await _host.ScalarAsync("SELECT count(*) FROM kernel.outbox_event WHERE event_type = 'SETTING_CHANGED' AND aggregate_type = 'SETTING'", Ct));
    }

    private static object AtWarehouse(string value) => new { key = Key, level = "Warehouse", value, companyId = Company, warehouseId = Warehouse };

    private async Task<Guid> WriteAsync(object body, string idempotencyKey)
    {
        CommandReply reply = await _host.PostAsync(Set, body, idempotencyKey, ifMatch: "\"0\"", cancellationToken: Ct);
        Assert.True(reply.Status == 200, reply.Body);
        return reply.Json!["id"]!.GetValue<Guid>();
    }

    private async Task AddSiteAdministratorAsync(string subject, Guid site)
    {
        Guid user = Guid.NewGuid();
        Guid system = Kernel.Contracts.Security.WellKnownUsers.System.Value;
        await _host.ScalarAsync(
            "INSERT INTO kernel.user_account (id, external_subject, display_name, is_service_account, status, created_at, created_by, updated_at, updated_by, version, extension) " +
            "VALUES (@id, @subject, @subject, false, 'Active', now(), @system, now(), @system, 1, '{}'::jsonb)",
            Ct, ("id", user), ("subject", subject), ("system", system));
        await _host.ScalarAsync(
            "INSERT INTO kernel.role_assignment (id, user_id, role_id, company_id, site_id, valid_from, assigned_by, created_at, created_by, updated_at, updated_by, version, extension) " +
            "VALUES (gen_random_uuid(), @user, @role, @company, @site, DATE '2026-01-01', @system, now(), @system, now(), @system, 1, '{}'::jsonb)",
            Ct, ("user", user), ("role", Milestone1IdentityFixture.SettingsAdministratorRoleId), ("company", Company), ("site", site), ("system", system));
    }
}
