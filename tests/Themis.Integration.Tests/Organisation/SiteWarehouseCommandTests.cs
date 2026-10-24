using Themis.Integration.Tests.Pipeline;
using Themis.Kernel.Organisation;

namespace Themis.Integration.Tests.Organisation;

/// <summary>
/// SITE.CREATE, SITE.UPDATE, WAREHOUSE.CREATE, WAREHOUSE.UPDATE and their reads end to end (milestone 2 step 3
/// brief, items 6 and 7): one audit and one outbox event per changed row with the company (D-44, D-50), codes
/// unique per company (R-MD-05) and fixed (R-MD-06), If-Match (R-API-02), the default warehouse moving within a site
/// with the previous default versioned, only an Active warehouse becoming the default (R-DOC-01), the warehouse scope
/// taken from the site row so a site-limited assignment acts on its own site only (R-SEC-01, D-47), and lists
/// ordered by code.
/// </summary>
[Collection(PostgresTestGroup.Name)]
[Trait("Category", "Integration")]
public sealed class SiteWarehouseCommandTests : IAsyncLifetime
{
    private static readonly Guid Se01 = Milestone1OrganisationFixture.CompanyId;
    private static readonly Guid MainSite = Milestone1OrganisationFixture.SiteId;
    private static readonly Guid MainWarehouse = Milestone1OrganisationFixture.WarehouseId;

    private readonly PostgresFixture _db;
    private PipelineTestHost _host = null!;
    private OrganisationTestUsers _users = null!;

    public SiteWarehouseCommandTests(PostgresFixture db)
    {
        _db = db;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        await _db.TruncateAsync(Ct);
        _host = await PipelineTestHost.StartAsync(_db);
        _users = new OrganisationTestUsers(_db);
    }

    public async ValueTask DisposeAsync()
    {
        await _users.DisposeAsync();
        await _host.DisposeAsync();
    }

    private static string Sites(Guid company) => $"{OrganisationBodies.Companies}/{company}/sites";

    private static string Warehouses(Guid company, Guid? site = null) => $"{OrganisationBodies.Companies}/{company}/warehouses{(site is null ? string.Empty : $"?siteId={site}")}";

    private async Task<Guid> CreateSiteAsync(string code, Guid? company = null, string? timeZone = null, string? subject = null)
    {
        CommandReply reply = await _host.PostAsync(OrganisationBodies.Sites, new { companyId = company ?? Se01, code, name = $"{code} site", timeZone }, $"site-{code}-{Guid.NewGuid():N}", subject: subject, cancellationToken: Ct);
        Assert.True(reply.Status == 200, reply.Body);
        return reply.Json!["id"]!.GetValue<Guid>();
    }

    private Task<CommandReply> CreateWarehouseAsync(Guid site, string code, bool isDefault = false, string? key = null, string? subject = null) =>
        _host.PostAsync(OrganisationBodies.Warehouses, new { siteId = site, code, name = $"{code} warehouse", isDefault }, key ?? $"warehouse-{code}-{Guid.NewGuid():N}", subject: subject, cancellationToken: Ct);

    private Task<CommandReply> UpdateWarehouseAsync(Guid id, string name, bool isDefault, string ifMatch, string? key = null, string? code = null, string? subject = null) =>
        _host.PostAsync(OrganisationBodies.WarehouseUpdate(id), new { name, isDefault, code }, key ?? $"warehouse-update-{Guid.NewGuid():N}", ifMatch: ifMatch, subject: subject, cancellationToken: Ct);

    // ---- sites ----------------------------------------------------------------------------------------------------

    [Fact]
    public async Task SITE_CREATE_writes_the_site_with_one_audit_event_and_one_D_50_envelope_with_the_company_a_replay_writes_nothing_and_the_reads_are_ordered_by_code()
    {
        var body = new { companyId = Se01, code = "north", name = "North site", timeZone = "Europe/Helsinki" };
        CommandReply reply = await _host.PostAsync(OrganisationBodies.Sites, body, "site-1", correlationId: "corr-site-1", cancellationToken: Ct);
        CommandReply replay = await _host.PostAsync(OrganisationBodies.Sites, body, "site-1", cancellationToken: Ct);

        Assert.True(reply.Status == 200, reply.Body);
        Assert.Equal("\"1\"", reply.ETag);
        Guid id = reply.Json!["id"]!.GetValue<Guid>();
        Assert.Equal(Se01, reply.Json["companyId"]!.GetValue<Guid>());
        Assert.Equal("NORTH", reply.Json["code"]!.GetValue<string>());
        Assert.Equal("North site", reply.Json["name"]!.GetValue<string>());
        Assert.Equal("Europe/Helsinki", reply.Json["timeZone"]!.GetValue<string>());
        Assert.Equal(1, reply.Json["version"]!.GetValue<long>());
        Assert.Equal(2L, await _host.CountAsync("kernel.site", Ct));

        const string audit = "FROM kernel.audit_event WHERE command_name = 'SITE.CREATE'";
        Assert.Equal(1L, await _host.ScalarAsync($"SELECT count(*) {audit}", Ct));
        Assert.Equal(id, await _host.ScalarAsync($"SELECT entity_id {audit} AND entity_type = 'Site'", Ct));
        Assert.Equal(TestUsers.OrganisationAdministrator, await _host.ScalarAsync($"SELECT user_id {audit}", Ct));
        Assert.Equal("corr-site-1", await _host.ScalarAsync($"SELECT correlation_id {audit}", Ct));
        Assert.Equal("NORTH", await _host.ScalarAsync($"SELECT changes->'code'->>'after' {audit}", Ct));
        Assert.Equal(Se01.ToString(), await _host.ScalarAsync($"SELECT changes->'companyId'->>'after' {audit}", Ct));
        Assert.Equal(1L, await _host.CountAsync("kernel.outbox_event", Ct));
        Assert.Equal(("SITE_CREATED", "SITE", id, Se01, "SITE.CREATE"), await OrganisationBodies.EnvelopeAsync(_host, "SITE_CREATED", Ct));

        Assert.Equal(200, replay.Status);
        Assert.True(replay.Replayed);
        Assert.Equal(reply.Body, replay.Body);
        Assert.Equal(2L, await _host.CountAsync("kernel.site", Ct));
        Assert.Equal(1L, await _host.CountAsync("kernel.outbox_event", Ct));

        // SITE.READ (D-47): the scope is the company in the route; a site of another company is 404 there.
        CommandReply read = await _host.GetAsync($"{Sites(Se01)}/{id}", cancellationToken: Ct);
        CommandReply list = await _host.GetAsync(Sites(Se01), cancellationToken: Ct);
        CommandReply unknown = await _host.GetAsync($"{Sites(Se01)}/{Guid.NewGuid()}", cancellationToken: Ct);
        Assert.Equal(200, read.Status);
        Assert.Equal(reply.Body, read.Body);
        Assert.Equal("\"1\"", read.ETag);
        Assert.Equal(200, list.Status);
        Assert.Equal(["MAIN", "NORTH"], list.Json!.AsArray().Select(site => site!["code"]!.GetValue<string>()));
        Assert.Null(list.Json.AsArray()[0]!["timeZone"]);
        Assert.Equal(404, unknown.Status);
        Assert.Equal("site_not_found", unknown.Code);
    }

    [Fact]
    public async Task R_MD_05_a_duplicate_site_code_in_the_company_is_422_and_stored_while_the_same_code_in_another_company_is_accepted()
    {
        CommandReply company = await _host.PostAsync(OrganisationBodies.Companies, OrganisationBodies.Company("SE02"), "company-2", cancellationToken: Ct);
        Guid se02 = company.Json!["id"]!.GetValue<Guid>();
        await OrganisationTestUsers.GrantFixtureAdministratorAsync(_host, se02, Ct);

        CommandReply duplicate = await _host.PostAsync(OrganisationBodies.Sites, new { companyId = Se01, code = "main", name = "Main again" }, "site-dup", cancellationToken: Ct);
        CommandReply replay = await _host.PostAsync(OrganisationBodies.Sites, new { companyId = Se01, code = "main", name = "Main again" }, "site-dup", cancellationToken: Ct);
        CommandReply otherCompany = await _host.PostAsync(OrganisationBodies.Sites, new { companyId = se02, code = "MAIN", name = "SE02 main" }, "site-se02", cancellationToken: Ct);

        Assert.Equal(422, duplicate.Status);
        Assert.Equal(("R-MD-05", "site_code_duplicate"), (duplicate.RuleId, duplicate.Code));
        Assert.Equal(422, replay.Status);
        Assert.True(replay.Replayed);
        Assert.True(otherCompany.Status == 200, otherCompany.Body);
        Assert.Equal(se02, otherCompany.Json!["companyId"]!.GetValue<Guid>());
        Assert.Equal(2L, await _host.CountAsync("kernel.site", Ct));
    }

    [Fact]
    public async Task SITE_UPDATE_changes_name_and_time_zone_on_the_current_version_R_API_02_with_its_audit_event_and_event_and_R_MD_06_refuses_a_differing_code()
    {
        var body = new { name = "Main site, Gnosjö", timeZone = "Europe/Stockholm" };
        CommandReply missing = await _host.PostAsync(OrganisationBodies.SiteUpdate(MainSite), body, "site-428", cancellationToken: Ct);
        CommandReply stale = await _host.PostAsync(OrganisationBodies.SiteUpdate(MainSite), body, "site-412", ifMatch: "\"4\"", cancellationToken: Ct);
        CommandReply unknown = await _host.PostAsync(OrganisationBodies.SiteUpdate(Guid.NewGuid()), body, "site-404", ifMatch: "\"1\"", cancellationToken: Ct);
        CommandReply recoded = await _host.PostAsync(OrganisationBodies.SiteUpdate(MainSite), new { body.name, body.timeZone, code = "HQ" }, "site-code", ifMatch: "\"1\"", cancellationToken: Ct);
        CommandReply invalidZone = await _host.PostAsync(OrganisationBodies.SiteUpdate(MainSite), new { body.name, timeZone = "Mars/Olympus" }, "site-zone", ifMatch: "\"1\"", cancellationToken: Ct);
        CommandReply updated = await _host.PostAsync(OrganisationBodies.SiteUpdate(MainSite), new { body.name, body.timeZone, code = "main" }, "site-412", ifMatch: "\"1\"", cancellationToken: Ct);
        CommandReply cleared = await _host.PostAsync(OrganisationBodies.SiteUpdate(MainSite), new { name = "Main site" }, "site-clear", ifMatch: "\"2\"", cancellationToken: Ct);

        Assert.Equal(428, missing.Status);
        Assert.Equal("if_match_required", missing.Code);
        Assert.Equal(412, stale.Status);
        Assert.Equal(("R-API-02", "version_stale"), (stale.RuleId, stale.Code));
        Assert.Equal(404, unknown.Status);
        Assert.Equal("site_not_found", unknown.Code);
        Assert.Equal(422, recoded.Status);
        Assert.Equal(("R-MD-06", "site_code_fixed"), (recoded.RuleId, recoded.Code));
        Assert.Equal(400, invalidZone.Status);
        Assert.Equal("time_zone_invalid", invalidZone.Json!["errors"]![0]!["code"]!.GetValue<string>());
        Assert.Equal(200, updated.Status);
        Assert.False(updated.Replayed);
        Assert.Equal("\"2\"", updated.ETag);
        Assert.Equal("Main site, Gnosjö", updated.Json!["name"]!.GetValue<string>());
        Assert.Equal("Europe/Stockholm", updated.Json["timeZone"]!.GetValue<string>());
        Assert.Equal("MAIN", updated.Json["code"]!.GetValue<string>());
        // An empty time zone means the company's again (Specification, Site).
        Assert.Equal(200, cleared.Status);
        Assert.Null(cleared.Json!["timeZone"]);
        Assert.Equal(3, cleared.Json["version"]!.GetValue<long>());

        const string audit = "FROM kernel.audit_event WHERE command_name = 'SITE.UPDATE'";
        Assert.Equal(2L, await _host.ScalarAsync($"SELECT count(*) {audit}", Ct));
        Assert.Equal(("Main site", "Main site, Gnosjö"), ((string?)await _host.ScalarAsync($"SELECT changes->'name'->>'before' {audit} AND changes->'version'->>'after' = '2'", Ct), (string?)await _host.ScalarAsync($"SELECT changes->'name'->>'after' {audit} AND changes->'version'->>'after' = '2'", Ct)));
        Assert.Equal((null, "Europe/Stockholm"), ((string?)await _host.ScalarAsync($"SELECT changes->'timeZone'->>'before' {audit} AND changes->'version'->>'after' = '2'", Ct), (string?)await _host.ScalarAsync($"SELECT changes->'timeZone'->>'after' {audit} AND changes->'version'->>'after' = '2'", Ct)));
        Assert.Equal(2L, await _host.ScalarAsync("SELECT count(*) FROM kernel.outbox_event WHERE event_type = 'SITE_UPDATED' AND aggregate_type = 'SITE' AND aggregate_id = @id AND company_id = @company", Ct, ("id", MainSite), ("company", Se01)));
    }

    // ---- warehouses -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task WAREHOUSE_CREATE_writes_an_Active_warehouse_of_the_sites_company_with_one_audit_event_and_one_D_50_envelope_R_MD_05_refuses_a_duplicate_code_and_the_reads_are_ordered_by_code()
    {
        CommandReply reply = await CreateWarehouseAsync(MainSite, "second", key: "warehouse-1");
        CommandReply replay = await CreateWarehouseAsync(MainSite, "second", key: "warehouse-1");
        CommandReply duplicate = await CreateWarehouseAsync(MainSite, "main", key: "warehouse-dup");
        CommandReply unknownSite = await CreateWarehouseAsync(Guid.NewGuid(), "ELSEWHERE", key: "warehouse-404");

        Assert.True(reply.Status == 200, reply.Body);
        Assert.Equal("\"1\"", reply.ETag);
        Guid id = reply.Json!["id"]!.GetValue<Guid>();
        Assert.Equal(MainSite, reply.Json["siteId"]!.GetValue<Guid>());
        Assert.Equal(Se01, reply.Json["companyId"]!.GetValue<Guid>());
        Assert.Equal("SECOND", reply.Json["code"]!.GetValue<string>());
        Assert.Equal("second warehouse", reply.Json["name"]!.GetValue<string>());
        Assert.False(reply.Json["isDefault"]!.GetValue<bool>());
        Assert.Equal("Active", reply.Json["status"]!.GetValue<string>());
        Assert.Equal(1, reply.Json["version"]!.GetValue<long>());
        Assert.Equal(Se01, await _host.ScalarAsync("SELECT company_id FROM kernel.warehouse WHERE id = @id", Ct, ("id", id)));

        const string audit = "FROM kernel.audit_event WHERE command_name = 'WAREHOUSE.CREATE'";
        Assert.Equal(1L, await _host.ScalarAsync($"SELECT count(*) {audit}", Ct));
        Assert.Equal(id, await _host.ScalarAsync($"SELECT entity_id {audit} AND entity_type = 'Warehouse'", Ct));
        Assert.Equal("False", await _host.ScalarAsync($"SELECT changes->'isDefault'->>'after' {audit}", Ct));
        Assert.Equal(1L, await _host.CountAsync("kernel.outbox_event", Ct));
        Assert.Equal(("WAREHOUSE_CREATED", "WAREHOUSE", id, Se01, "WAREHOUSE.CREATE"), await OrganisationBodies.EnvelopeAsync(_host, "WAREHOUSE_CREATED", Ct));

        Assert.Equal(200, replay.Status);
        Assert.True(replay.Replayed);
        Assert.Equal(reply.Body, replay.Body);
        Assert.Equal(422, duplicate.Status);
        Assert.Equal(("R-MD-05", "warehouse_code_duplicate"), (duplicate.RuleId, duplicate.Code));
        // The scope is built from the site row; a site that does not exist is 404, not a scope (Build Brief, Authorisation).
        Assert.Equal(404, unknownSite.Status);
        Assert.Equal("site_not_found", unknownSite.Code);
        Assert.Equal(2L, await _host.CountAsync("kernel.warehouse", Ct));

        // WAREHOUSE.READ: one, the company's, one site's; a site of another company is 404 under this company.
        CommandReply read = await _host.GetAsync($"{Warehouses(Se01)}/{id}", cancellationToken: Ct);
        CommandReply all = await _host.GetAsync(Warehouses(Se01), cancellationToken: Ct);
        CommandReply ofSite = await _host.GetAsync(Warehouses(Se01, MainSite), cancellationToken: Ct);
        CommandReply unknown = await _host.GetAsync($"{Warehouses(Se01)}/{Guid.NewGuid()}", cancellationToken: Ct);
        CommandReply foreignSite = await _host.GetAsync(Warehouses(Se01, Guid.NewGuid()), cancellationToken: Ct);
        Assert.Equal(200, read.Status);
        Assert.Equal(reply.Body, read.Body);
        Assert.Equal("\"1\"", read.ETag);
        Assert.Equal(200, all.Status);
        Assert.Equal(["MAIN", "SECOND"], all.Json!.AsArray().Select(warehouse => warehouse!["code"]!.GetValue<string>()));
        Assert.Equal(200, ofSite.Status);
        Assert.Equal(all.Body, ofSite.Body);
        Assert.Equal(404, unknown.Status);
        Assert.Equal("warehouse_not_found", unknown.Code);
        Assert.Equal(404, foreignSite.Status);
        Assert.Equal("site_not_found", foreignSite.Code);
    }

    [Fact]
    public async Task The_default_warehouse_moves_within_the_site_the_previous_default_gets_its_own_version_audit_event_and_WAREHOUSE_UPDATED_event_and_clearing_the_flag_leaves_the_site_without_one()
    {
        // WAREHOUSE.CREATE with isDefault moves the site's default from the fixture warehouse.
        CommandReply annex = await CreateWarehouseAsync(MainSite, "ANNEX", isDefault: true, key: "warehouse-annex");
        Assert.True(annex.Status == 200, annex.Body);
        Guid annexId = annex.Json!["id"]!.GetValue<Guid>();
        Assert.True(annex.Json["isDefault"]!.GetValue<bool>());
        Assert.Equal((false, 2L), await DefaultAndVersionAsync(MainWarehouse));
        Assert.Equal((true, 1L), await DefaultAndVersionAsync(annexId));
        const string createAudit = "FROM kernel.audit_event WHERE command_name = 'WAREHOUSE.CREATE'";
        Assert.Equal(2L, await _host.ScalarAsync($"SELECT count(*) {createAudit}", Ct));
        Assert.Equal(("True", "False"), ((string?)await _host.ScalarAsync($"SELECT changes->'isDefault'->>'before' {createAudit} AND entity_id = @id", Ct, ("id", MainWarehouse)), (string?)await _host.ScalarAsync($"SELECT changes->'isDefault'->>'after' {createAudit} AND entity_id = @id", Ct, ("id", MainWarehouse))));
        // R-AUD-01: the previous default's event names the fields that changed and the version, nothing else.
        Assert.Equal(["isDefault", "version"], ((string)(await _host.ScalarAsync("SELECT string_agg(key, ',' ORDER BY key) FROM kernel.audit_event, jsonb_object_keys(changes) key WHERE command_name = 'WAREHOUSE.CREATE' AND entity_id = @id", Ct, ("id", MainWarehouse)))!).Split(','));
        Assert.Equal(("WAREHOUSE_UPDATED", "WAREHOUSE", MainWarehouse, Se01, "WAREHOUSE.CREATE"), await OrganisationBodies.EnvelopeAsync(_host, "WAREHOUSE_UPDATED", Ct));
        Assert.Equal("false", await _host.ScalarAsync("SELECT payload->>'isDefault' FROM kernel.outbox_event WHERE event_type = 'WAREHOUSE_UPDATED'", Ct));

        // WAREHOUSE.UPDATE moves it back; the holder of the previous default's old version gets 412 afterwards (R-API-02).
        CommandReply back = await UpdateWarehouseAsync(MainWarehouse, "Main warehouse", isDefault: true, ifMatch: "\"2\"", key: "warehouse-back");
        Assert.True(back.Status == 200, back.Body);
        Assert.Equal("\"3\"", back.ETag);
        Assert.Equal((true, 3L), await DefaultAndVersionAsync(MainWarehouse));
        Assert.Equal((false, 2L), await DefaultAndVersionAsync(annexId));
        CommandReply staleAnnex = await UpdateWarehouseAsync(annexId, "Annex", isDefault: true, ifMatch: "\"1\"", key: "warehouse-annex-stale");
        Assert.Equal(412, staleAnnex.Status);
        Assert.Equal("version_stale", staleAnnex.Code);
        Assert.Equal(3L, await _host.ScalarAsync("SELECT count(*) FROM kernel.outbox_event WHERE event_type = 'WAREHOUSE_UPDATED'", Ct));

        // Keeping the flag as it is touches only the row named; clearing the default leaves the site without one.
        CommandReply renamed = await UpdateWarehouseAsync(annexId, "Annex, hall B", isDefault: false, ifMatch: "\"2\"", key: "warehouse-annex-rename");
        Assert.True(renamed.Status == 200, renamed.Body);
        Assert.Equal((true, 3L), await DefaultAndVersionAsync(MainWarehouse));
        CommandReply noDefault = await UpdateWarehouseAsync(MainWarehouse, "Main warehouse", isDefault: false, ifMatch: "\"3\"", key: "warehouse-no-default");
        Assert.True(noDefault.Status == 200, noDefault.Body);
        Assert.Equal(0L, await _host.ScalarAsync("SELECT count(*) FROM kernel.warehouse WHERE site_id = @site AND is_default", Ct, ("site", MainSite)));
        CommandReply reinstated = await UpdateWarehouseAsync(MainWarehouse, "Main warehouse", isDefault: true, ifMatch: "\"4\"", key: "warehouse-reinstate");
        Assert.True(reinstated.Status == 200, reinstated.Body);
        Assert.Equal((false, 3L), await DefaultAndVersionAsync(annexId));
        Assert.Equal(1L, await _host.ScalarAsync("SELECT count(*) FROM kernel.warehouse WHERE site_id = @site AND is_default", Ct, ("site", MainSite)));
    }

    [Fact]
    public async Task R_DOC_01_only_an_Active_warehouse_becomes_the_default_a_Blocked_one_is_422_warehouse_not_active_and_stored_while_its_other_attributes_still_change()
    {
        CommandReply annex = await CreateWarehouseAsync(MainSite, "ANNEX", key: "warehouse-annex");
        Guid annexId = annex.Json!["id"]!.GetValue<Guid>();
        // BLOCK comes with step 7; the status is set as test data.
        await _host.ScalarAsync("UPDATE kernel.warehouse SET status = 'Blocked' WHERE id = @id", Ct, ("id", annexId));

        CommandReply blocked = await UpdateWarehouseAsync(annexId, "Annex", isDefault: true, ifMatch: "\"1\"", key: "warehouse-blocked");
        CommandReply replay = await UpdateWarehouseAsync(annexId, "Annex", isDefault: true, ifMatch: "\"1\"", key: "warehouse-blocked");
        CommandReply renamed = await UpdateWarehouseAsync(annexId, "Annex, closed", isDefault: false, ifMatch: "\"1\"", key: "warehouse-blocked-rename");
        CommandReply recoded = await UpdateWarehouseAsync(annexId, "Annex, closed", isDefault: false, ifMatch: "\"2\"", key: "warehouse-recode", code: "ANNEX2");

        Assert.Equal(422, blocked.Status);
        Assert.Equal(("R-DOC-01", "warehouse_not_active"), (blocked.RuleId, blocked.Code));
        Assert.Equal(422, replay.Status);
        Assert.True(replay.Replayed);
        Assert.Equal((true, 1L), await DefaultAndVersionAsync(MainWarehouse));
        Assert.Equal(200, renamed.Status);
        Assert.Equal("Blocked", renamed.Json!["status"]!.GetValue<string>());
        Assert.Equal(2, renamed.Json["version"]!.GetValue<long>());
        Assert.Equal(422, recoded.Status);
        Assert.Equal(("R-MD-06", "warehouse_code_fixed"), (recoded.RuleId, recoded.Code));
    }

    [Fact]
    public async Task R_SEC_01_D_47_the_warehouse_scope_is_the_site_from_the_row_so_a_site_limited_administrator_creates_updates_reads_and_lists_the_warehouses_of_its_site_and_nothing_else()
    {
        Guid north = await CreateSiteAsync("NORTH");
        string northAdministrator = await _users.AddAdministratorAsync(Se01, north, Ct);

        CommandReply own = await CreateWarehouseAsync(north, "N1", isDefault: true, key: "warehouse-n1", subject: northAdministrator);
        Assert.True(own.Status == 200, own.Body);
        Guid n1 = own.Json!["id"]!.GetValue<Guid>();
        CommandReply otherSite = await CreateWarehouseAsync(MainSite, "N2", key: "warehouse-n2", subject: northAdministrator);
        CommandReply ownUpdate = await UpdateWarehouseAsync(n1, "North hall", isDefault: true, ifMatch: "\"1\"", key: "warehouse-n1-update", subject: northAdministrator);
        CommandReply otherUpdate = await UpdateWarehouseAsync(MainWarehouse, "Main", isDefault: true, ifMatch: "\"1\"", key: "warehouse-main-update", subject: northAdministrator);
        CommandReply ownRead = await _host.GetAsync($"{Warehouses(Se01)}/{n1}", subject: northAdministrator, cancellationToken: Ct);
        CommandReply otherRead = await _host.GetAsync($"{Warehouses(Se01)}/{MainWarehouse}", subject: northAdministrator, cancellationToken: Ct);
        CommandReply ownList = await _host.GetAsync(Warehouses(Se01, north), subject: northAdministrator, cancellationToken: Ct);
        CommandReply companyList = await _host.GetAsync(Warehouses(Se01), subject: northAdministrator, cancellationToken: Ct);
        CommandReply siteList = await _host.GetAsync(Sites(Se01), subject: northAdministrator, cancellationToken: Ct);
        CommandReply siteCreate = await _host.PostAsync(OrganisationBodies.Sites, new { companyId = Se01, code = "EAST", name = "East" }, "site-east", subject: northAdministrator, cancellationToken: Ct);
        CommandReply clerk = await CreateWarehouseAsync(north, "N3", key: "warehouse-n3", subject: TestUsers.ClerkSubject);
        CommandReply preparerRead = await _host.GetAsync($"{Warehouses(Se01)}/{n1}", subject: TestUsers.PreparerSubject, cancellationToken: Ct);

        Assert.Equal(north, own.Json["siteId"]!.GetValue<Guid>());
        Assert.Equal(Se01, own.Json["companyId"]!.GetValue<Guid>());
        Assert.Equal(403, otherSite.Status);
        Assert.Equal(("R-SEC-01", "out_of_scope"), (otherSite.RuleId, otherSite.Code));
        Assert.True(ownUpdate.Status == 200, ownUpdate.Body);
        Assert.Equal(403, otherUpdate.Status);
        Assert.Equal("out_of_scope", otherUpdate.Code);
        Assert.Equal(200, ownRead.Status);
        Assert.Equal(403, otherRead.Status);
        Assert.Equal("out_of_scope", otherRead.Code);
        Assert.Equal(200, ownList.Status);
        Assert.Equal(["N1"], ownList.Json!.AsArray().Select(warehouse => warehouse!["code"]!.GetValue<string>()));
        // Without a site the list is company-scoped, which a site-limited assignment never satisfies (R-SEC-01); the sites are company-scoped too.
        Assert.Equal(403, companyList.Status);
        Assert.Equal("out_of_scope", companyList.Code);
        Assert.Equal(403, siteList.Status);
        Assert.Equal(403, siteCreate.Status);
        Assert.Equal("out_of_scope", siteCreate.Code);
        Assert.Equal(403, clerk.Status);
        Assert.Equal("permission_missing", clerk.Code);
        Assert.Equal(403, preparerRead.Status);
        Assert.Equal("permission_missing", preparerRead.Code);
        // The site's default moved to N1 and left the main site's alone (the default is per site).
        Assert.Equal((true, 1L), await DefaultAndVersionAsync(MainWarehouse));
    }

    [Fact]
    public async Task A_shape_error_is_400_before_the_transaction_for_a_site_or_warehouse_without_a_code_or_name()
    {
        // A missing code is the validator's code_required; an empty string never binds to a Code (request_body_invalid at the binder).
        CommandReply site = await _host.PostAsync(OrganisationBodies.Sites, new { companyId = Se01, name = "" }, "site-400", cancellationToken: Ct);
        CommandReply warehouse = await _host.PostAsync(OrganisationBodies.Warehouses, new { siteId = Guid.Empty, code = "X", name = " " }, "warehouse-400", cancellationToken: Ct);

        Assert.Equal(400, site.Status);
        Assert.Equal(["code_required", "name_required"], site.Json!["errors"]!.AsArray().Select(error => error!["code"]!.GetValue<string>()).Order());
        Assert.Equal(400, warehouse.Status);
        Assert.Equal(["name_required", "site_required"], warehouse.Json!["errors"]!.AsArray().Select(error => error!["code"]!.GetValue<string>()).Order());
        Assert.Equal(0L, await _host.CountAsync("kernel.idempotency_record", Ct));
    }

    private async Task<(bool IsDefault, long Version)> DefaultAndVersionAsync(Guid warehouse) =>
        ((bool)(await _host.ScalarAsync("SELECT is_default FROM kernel.warehouse WHERE id = @id", Ct, ("id", warehouse)))!, (long)(await _host.ScalarAsync("SELECT version FROM kernel.warehouse WHERE id = @id", Ct, ("id", warehouse)))!);
}
