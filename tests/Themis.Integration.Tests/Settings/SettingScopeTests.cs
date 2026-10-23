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
