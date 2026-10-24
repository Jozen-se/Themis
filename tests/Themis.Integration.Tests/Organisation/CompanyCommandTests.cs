using Themis.Integration.Tests.Pipeline;
using Themis.Kernel.Organisation;

namespace Themis.Integration.Tests.Organisation;

/// <summary>
/// COMPANY.CREATE and COMPANY.UPDATE end to end (milestone 2 step 3 brief, items 3 and 4): the company and its own
/// party from the installation-level PARTY series with two audit and two outbox events (D-64, D-50), the rounding mode
/// default from the setting definition (P-11), the code and the organisation number per country unique (R-MD-05), the
/// 400s stored as the key's result, the shape errors before the transaction, the update of every changeable attribute
/// before the D-65 triggers with the party following (D-64), the attributes the triggers fix (R-MD-06), If-Match
/// (R-API-02) and the installation and company scopes (D-47, RR-52). The attributes the first posting fixes are proven
/// in <see cref="CompanyPostingTests"/>.
/// </summary>
[Collection(PostgresTestGroup.Name)]
[Trait("Category", "Integration")]
public sealed class CompanyCommandTests : IAsyncLifetime
{
    private static readonly Guid Se01 = Milestone1OrganisationFixture.CompanyId;
    private static readonly Guid Bas = Milestone1OrganisationFixture.ChartOfAccountsId;

    private readonly PostgresFixture _db;
    private PipelineTestHost _host = null!;
    private OrganisationTestUsers _users = null!;

    public CompanyCommandTests(PostgresFixture db)
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

    [Fact]
    public async Task D_64_COMPANY_CREATE_writes_the_company_and_its_own_party_numbered_from_the_PARTY_series_with_two_audit_events_and_two_D_50_envelopes_and_P_11_the_rounding_mode_default_comes_from_the_setting()
    {
        object body = OrganisationBodies.Company("se02");
        CommandReply reply = await _host.PostAsync(OrganisationBodies.Companies, body, "company-1", correlationId: "corr-company-1", cancellationToken: Ct);
        CommandReply replay = await _host.PostAsync(OrganisationBodies.Companies, body, "company-1", cancellationToken: Ct);

        Assert.True(reply.Status == 200, reply.Body);
        Assert.Equal("\"1\"", reply.ETag);
        Guid id = reply.Json!["id"]!.GetValue<Guid>();
        Guid partyId = reply.Json["partyId"]!.GetValue<Guid>();
        Assert.Equal("SE02", reply.Json["code"]!.GetValue<string>());
        Assert.Equal("Nordic Bolts AB", reply.Json["legalName"]!.GetValue<string>());
        Assert.Equal("556677-8899", reply.Json["organisationNumber"]!.GetValue<string>());
        Assert.Equal("SE556677889901", reply.Json["vatNumber"]!.GetValue<string>());
        Assert.Equal("SE", reply.Json["countryCode"]!.GetValue<string>());
        Assert.Equal("EUR", reply.Json["baseCurrency"]!.GetValue<string>());
        Assert.Equal("Europe/Stockholm", reply.Json["timeZone"]!.GetValue<string>());
        Assert.Equal(1, reply.Json["fiscalYearStartMonth"]!.GetValue<int>());
        Assert.Equal(Bas, reply.Json["chartOfAccountsId"]!.GetValue<Guid>());
        // P-11, D-58: left out of the body, the rounding mode is the documented default of company.rounding_mode, read as a setting.
        Assert.Equal("HalfAwayFromZero", reply.Json["roundingMode"]!.GetValue<string>());
        Assert.Equal("Line", reply.Json["taxRoundingLevel"]!.GetValue<string>());
        Assert.Equal("Active", reply.Json["status"]!.GetValue<string>());
        Assert.Equal(1, reply.Json["version"]!.GetValue<long>());

        // The company row: the organisation number normalised like the duplicate key (R-MD-05, R-MD-01).
        Assert.Equal(2L, await _host.CountAsync("kernel.company", Ct));
        Assert.Equal("5566778899", await _host.ScalarAsync("SELECT organisation_number_normalised FROM kernel.company WHERE id = @id", Ct, ("id", id)));
        Assert.Equal(partyId, await _host.ScalarAsync("SELECT party_id FROM kernel.company WHERE id = @id", Ct, ("id", id)));
        // D-64: the party from the legal name, organisation number, VAT number and country, in the installation's locale, numbered P-000002 (the fixture took P-000001).
        Assert.Equal(2L, await _host.CountAsync("kernel.party", Ct));
        Assert.Equal("P-000002", await _host.ScalarAsync("SELECT party_number FROM kernel.party WHERE id = @id", Ct, ("id", partyId)));
        Assert.Equal("Organisation", await _host.ScalarAsync("SELECT party_type FROM kernel.party WHERE id = @id", Ct, ("id", partyId)));
        Assert.Equal("Nordic Bolts AB", await _host.ScalarAsync("SELECT name FROM kernel.party WHERE id = @id", Ct, ("id", partyId)));
        Assert.Equal("556677-8899", await _host.ScalarAsync("SELECT organisation_number FROM kernel.party WHERE id = @id", Ct, ("id", partyId)));
        Assert.Equal("SE556677889901", await _host.ScalarAsync("SELECT vat_number FROM kernel.party WHERE id = @id", Ct, ("id", partyId)));
        Assert.Equal("SE", await _host.ScalarAsync("SELECT country_code FROM kernel.party WHERE id = @id", Ct, ("id", partyId)));
        Assert.Equal("sv-SE", await _host.ScalarAsync("SELECT language FROM kernel.party WHERE id = @id", Ct, ("id", partyId)));
        Assert.Equal("Active", await _host.ScalarAsync("SELECT status FROM kernel.party WHERE id = @id", Ct, ("id", partyId)));
        Assert.Equal("5566778899|SE556677889901|NORDICBOLTSAB|", await _host.ScalarAsync("SELECT duplicate_key FROM kernel.party WHERE id = @id", Ct, ("id", partyId)));
        Assert.Equal(3L, await _host.ScalarAsync("SELECT next_number FROM kernel.number_series WHERE company_id IS NULL AND document_type = 'PARTY'", Ct));

        // R-AUD-01, D-44: one audit event per row; D-50: the party is an installation-scope aggregate, the company carries its own id.
        const string audit = "FROM kernel.audit_event WHERE command_name = 'COMPANY.CREATE'";
        Assert.Equal(2L, await _host.ScalarAsync($"SELECT count(*) {audit}", Ct));
        Assert.Equal(id, await _host.ScalarAsync($"SELECT entity_id {audit} AND entity_type = 'Company'", Ct));
        Assert.Equal(partyId, await _host.ScalarAsync($"SELECT entity_id {audit} AND entity_type = 'Party'", Ct));
        Assert.Equal(TestUsers.OrganisationAdministrator, await _host.ScalarAsync($"SELECT DISTINCT user_id {audit}", Ct));
        Assert.Equal("corr-company-1", await _host.ScalarAsync($"SELECT DISTINCT correlation_id {audit}", Ct));
        Assert.Equal("SE02", await _host.ScalarAsync($"SELECT changes->'code'->>'after' {audit} AND entity_type = 'Company'", Ct));
        Assert.Equal("HalfAwayFromZero", await _host.ScalarAsync($"SELECT changes->'roundingMode'->>'after' {audit} AND entity_type = 'Company'", Ct));
        Assert.Null(await _host.ScalarAsync($"SELECT changes->'code'->>'before' {audit} AND entity_type = 'Company'", Ct));
        Assert.Equal("P-000002", await _host.ScalarAsync($"SELECT changes->'partyNumber'->>'after' {audit} AND entity_type = 'Party'", Ct));
        Assert.Equal(2L, await _host.CountAsync("kernel.outbox_event", Ct));
        Assert.Equal(("COMPANY_CREATED", "COMPANY", id, id, "COMPANY.CREATE"), await OrganisationBodies.EnvelopeAsync(_host, "COMPANY_CREATED", Ct));
        Assert.Equal(("PARTY_CREATED", "PARTY", partyId, null, "COMPANY.CREATE"), await OrganisationBodies.EnvelopeAsync(_host, "PARTY_CREATED", Ct));
        Assert.Equal("SE02", await _host.ScalarAsync("SELECT payload->>'code' FROM kernel.outbox_event WHERE event_type = 'COMPANY_CREATED'", Ct));
        Assert.Equal("P-000002", await _host.ScalarAsync("SELECT payload->>'partyNumber' FROM kernel.outbox_event WHERE event_type = 'PARTY_CREATED'", Ct));

        // R-API-01: the replay returns the stored result and writes nothing, the series included.
        Assert.Equal(200, replay.Status);
        Assert.True(replay.Replayed);
        Assert.Equal(reply.Body, replay.Body);
        Assert.Equal(2L, await _host.CountAsync("kernel.company", Ct));
        Assert.Equal(2L, await _host.CountAsync("kernel.party", Ct));
        Assert.Equal(3L, await _host.ScalarAsync("SELECT next_number FROM kernel.number_series WHERE company_id IS NULL AND document_type = 'PARTY'", Ct));

        // A stated rounding mode is kept as stated.
        CommandReply halfEven = await _host.PostAsync(OrganisationBodies.Companies, OrganisationBodies.Company("SE03", organisationNumber: "556677-8800", vatNumber: null, roundingMode: "HalfEven"), "company-2", cancellationToken: Ct);
        Assert.True(halfEven.Status == 200, halfEven.Body);
        Assert.Equal("HalfEven", halfEven.Json!["roundingMode"]!.GetValue<string>());
        Assert.Null(halfEven.Json["vatNumber"]);
        Assert.Equal("P-000003", await _host.ScalarAsync("SELECT party_number FROM kernel.party WHERE id = @id", Ct, ("id", halfEven.Json["partyId"]!.GetValue<Guid>())));
    }

    [Theory]
    [InlineData("code", "company_code_duplicate")]
    [InlineData("organisation-number", "organisation_number_duplicate")]
    public async Task R_MD_05_a_duplicate_company_code_in_any_case_or_organisation_number_in_the_same_country_compared_normalised_is_422_and_stored_while_the_same_number_in_another_country_is_accepted(string which, string code)
    {
        object body = which == "code"
            ? OrganisationBodies.Company("se01", organisationNumber: "556677-8899")
            : OrganisationBodies.Company("SE03", organisationNumber: "5555 5555-55");
        string key = "company-dup-" + which;

        CommandReply duplicate = await _host.PostAsync(OrganisationBodies.Companies, body, key, cancellationToken: Ct);
        CommandReply replay = await _host.PostAsync(OrganisationBodies.Companies, body, key, cancellationToken: Ct);
        CommandReply otherCountry = await _host.PostAsync(OrganisationBodies.Companies, OrganisationBodies.Company("FI01", organisationNumber: "555555-5555", vatNumber: null, countryCode: "FI"), "company-fi", cancellationToken: Ct);

        Assert.Equal(422, duplicate.Status);
        Assert.Equal("application/problem+json", duplicate.MediaType);
        Assert.Equal(("R-MD-05", code), (duplicate.RuleId, duplicate.Code));
        Assert.Equal(422, replay.Status);
        Assert.True(replay.Replayed);
        Assert.Equal(code, replay.Code);
        // The uniqueness of the organisation number is per country (Specification, Company).
        Assert.True(otherCountry.Status == 200, otherCountry.Body);
        Assert.Equal(2L, await _host.CountAsync("kernel.company", Ct));
        Assert.Equal(2L, await _host.CountAsync("kernel.party", Ct));
        // The rejected create took no number: the series stands after the fixture's party and the accepted one.
        Assert.Equal(3L, await _host.ScalarAsync("SELECT next_number FROM kernel.number_series WHERE company_id IS NULL AND document_type = 'PARTY'", Ct));
    }

    [Theory]
    [InlineData("currency", "base_currency_unknown", "baseCurrency")]
    [InlineData("chart", "chart_of_accounts_unknown", "chartOfAccountsId")]
    public async Task An_unknown_currency_or_chart_is_400_with_its_own_code_no_ruleId_and_is_stored_as_the_keys_result_with_nothing_written(string which, string code, string field)
    {
        object body = which == "currency" ? OrganisationBodies.Company("SE02", baseCurrency: "XXX") : OrganisationBodies.Company("SE02", chartOfAccountsId: Guid.NewGuid());
        string key = "company-400-" + which;

        CommandReply reply = await _host.PostAsync(OrganisationBodies.Companies, body, key, cancellationToken: Ct);
        CommandReply replay = await _host.PostAsync(OrganisationBodies.Companies, body, key, cancellationToken: Ct);

        Assert.Equal(400, reply.Status);
        Assert.Equal("application/problem+json", reply.MediaType);
        Assert.Equal(code, reply.Code);
        Assert.Null(reply.RuleId);
        Assert.Equal(field, reply.Json!["errors"]![0]!["details"]!["field"]!.GetValue<string>());
        Assert.Equal(1L, await _host.CountAsync("kernel.company", Ct));
        Assert.Equal(1L, await _host.CountAsync("kernel.party", Ct));
        Assert.Equal(0L, await _host.CountAsync("kernel.outbox_event", Ct));
        Assert.Equal(2L, await _host.ScalarAsync("SELECT next_number FROM kernel.number_series WHERE company_id IS NULL AND document_type = 'PARTY'", Ct));
        Assert.Equal(400, replay.Status);
        Assert.True(replay.Replayed);
        Assert.Equal(code, replay.Code);
        Assert.Equal(1L, await _host.ScalarAsync("SELECT count(*) FROM kernel.idempotency_record WHERE key = @key", Ct, ("key", key)));
    }

    [Fact]
    public async Task A_shape_error_is_400_before_the_transaction_RR_51_a_Windows_time_zone_id_and_a_missing_VAT_rounding_level_D_65_among_them_and_the_corrected_retry_runs()
    {
        CommandReply invalid = await _host.PostAsync(OrganisationBodies.Companies, OrganisationBodies.Company("SE02", legalName: " ", timeZone: "W. Europe Standard Time", fiscalYearStartMonth: 13, taxRoundingLevel: null), "company-400", cancellationToken: Ct);
        long storedAfterRejection = await _host.CountAsync("kernel.idempotency_record", Ct);
        CommandReply retry = await _host.PostAsync(OrganisationBodies.Companies, OrganisationBodies.Company("SE02"), "company-400", cancellationToken: Ct);

        Assert.Equal(400, invalid.Status);
        Assert.Null(invalid.RuleId);
        Assert.Equal(["fiscal_year_start_month_out_of_range", "legal_name_required", "tax_rounding_level_required", "time_zone_invalid"], invalid.Json!["errors"]!.AsArray().Select(error => error!["code"]!.GetValue<string>()).Order());
        Assert.Equal(0L, storedAfterRejection);
        Assert.Equal(200, retry.Status);
        Assert.False(retry.Replayed);
    }

    [Fact]
    public async Task COMPANY_UPDATE_before_any_D_65_trigger_changes_every_attribute_and_D_64_the_party_follows_the_shared_fields_with_its_own_audit_event_and_event_while_R_MD_06_refuses_a_differing_code()
    {
        CommandReply chart = await _host.PostAsync(OrganisationBodies.Charts, new { code = "K3", name = "K3-kontoplan" }, "chart-k3", cancellationToken: Ct);
        Guid k3 = chart.Json!["id"]!.GetValue<Guid>();
        CommandReply created = await _host.PostAsync(OrganisationBodies.Companies, OrganisationBodies.Company("SE04"), "company-4", cancellationToken: Ct);
        Guid id = created.Json!["id"]!.GetValue<Guid>();
        Guid partyId = created.Json["partyId"]!.GetValue<Guid>();
        await OrganisationTestUsers.GrantFixtureAdministratorAsync(_host, id, Ct);

        var changed = new { legalName = "Nordic Bolts Holding Oy", organisationNumber = "1234567-8", vatNumber = (string?)null, countryCode = "FI", baseCurrency = "SEK", timeZone = "Europe/Helsinki", fiscalYearStartMonth = 7, chartOfAccountsId = k3, roundingMode = "HalfEven", taxRoundingLevel = "Document" };
        CommandReply updated = await _host.PostAsync(OrganisationBodies.CompanyUpdate(id), changed, "company-4-update", ifMatch: "\"1\"", cancellationToken: Ct);

        Assert.True(updated.Status == 200, updated.Body);
        Assert.Equal("\"2\"", updated.ETag);
        Assert.Equal("SE04", updated.Json!["code"]!.GetValue<string>());
        Assert.Equal("Nordic Bolts Holding Oy", updated.Json["legalName"]!.GetValue<string>());
        Assert.Equal("1234567-8", updated.Json["organisationNumber"]!.GetValue<string>());
        Assert.Null(updated.Json["vatNumber"]);
        Assert.Equal("FI", updated.Json["countryCode"]!.GetValue<string>());
        Assert.Equal("SEK", updated.Json["baseCurrency"]!.GetValue<string>());
        Assert.Equal("Europe/Helsinki", updated.Json["timeZone"]!.GetValue<string>());
        Assert.Equal(7, updated.Json["fiscalYearStartMonth"]!.GetValue<int>());
        Assert.Equal(k3, updated.Json["chartOfAccountsId"]!.GetValue<Guid>());
        Assert.Equal("HalfEven", updated.Json["roundingMode"]!.GetValue<string>());
        Assert.Equal("Document", updated.Json["taxRoundingLevel"]!.GetValue<string>());
        Assert.Equal(partyId, updated.Json["partyId"]!.GetValue<Guid>());
        Assert.Equal(2, updated.Json["version"]!.GetValue<long>());
        Assert.Equal("12345678", await _host.ScalarAsync("SELECT organisation_number_normalised FROM kernel.company WHERE id = @id", Ct, ("id", id)));

        // D-64: the party follows the legal name, organisation number, VAT number and country; its duplicate key is recomputed.
        Assert.Equal("Nordic Bolts Holding Oy", await _host.ScalarAsync("SELECT name FROM kernel.party WHERE id = @id", Ct, ("id", partyId)));
        Assert.Equal("1234567-8", await _host.ScalarAsync("SELECT organisation_number FROM kernel.party WHERE id = @id", Ct, ("id", partyId)));
        Assert.Null(await _host.ScalarAsync("SELECT vat_number FROM kernel.party WHERE id = @id", Ct, ("id", partyId)));
        Assert.Equal("FI", await _host.ScalarAsync("SELECT country_code FROM kernel.party WHERE id = @id", Ct, ("id", partyId)));
        Assert.Equal("12345678||NORDICBOLTSHOLDINGOY|", await _host.ScalarAsync("SELECT duplicate_key FROM kernel.party WHERE id = @id", Ct, ("id", partyId)));
        Assert.Equal("P-000002", await _host.ScalarAsync("SELECT party_number FROM kernel.party WHERE id = @id", Ct, ("id", partyId)));
        Assert.Equal(2L, await _host.ScalarAsync("SELECT version FROM kernel.party WHERE id = @id", Ct, ("id", partyId)));

        // R-AUD-01: the changed fields of each row; D-44, D-50: COMPANY_UPDATED with the company, PARTY_UPDATED without one.
        const string audit = "FROM kernel.audit_event WHERE command_name = 'COMPANY.UPDATE'";
        Assert.Equal(2L, await _host.ScalarAsync($"SELECT count(*) {audit}", Ct));
        Assert.Equal(("Nordic Bolts AB", "Nordic Bolts Holding Oy"), ((string?)await _host.ScalarAsync($"SELECT changes->'legalName'->>'before' {audit} AND entity_type = 'Company'", Ct), (string?)await _host.ScalarAsync($"SELECT changes->'legalName'->>'after' {audit} AND entity_type = 'Company'", Ct)));
        Assert.Equal(("HalfAwayFromZero", "HalfEven"), ((string?)await _host.ScalarAsync($"SELECT changes->'roundingMode'->>'before' {audit} AND entity_type = 'Company'", Ct), (string?)await _host.ScalarAsync($"SELECT changes->'roundingMode'->>'after' {audit} AND entity_type = 'Company'", Ct)));
        Assert.Null(await _host.ScalarAsync($"SELECT changes->'code' {audit} AND entity_type = 'Company'", Ct));
        Assert.Equal(("Nordic Bolts AB", "Nordic Bolts Holding Oy"), ((string?)await _host.ScalarAsync($"SELECT changes->'name'->>'before' {audit} AND entity_type = 'Party'", Ct), (string?)await _host.ScalarAsync($"SELECT changes->'name'->>'after' {audit} AND entity_type = 'Party'", Ct)));
        Assert.Equal(("1", "2"), ((string?)await _host.ScalarAsync($"SELECT changes->'version'->>'before' {audit} AND entity_type = 'Party'", Ct), (string?)await _host.ScalarAsync($"SELECT changes->'version'->>'after' {audit} AND entity_type = 'Party'", Ct)));
        Assert.Equal(("COMPANY_UPDATED", "COMPANY", id, id, "COMPANY.UPDATE"), await OrganisationBodies.EnvelopeAsync(_host, "COMPANY_UPDATED", Ct));
        Assert.Equal(("PARTY_UPDATED", "PARTY", partyId, null, "COMPANY.UPDATE"), await OrganisationBodies.EnvelopeAsync(_host, "PARTY_UPDATED", Ct));

        // R-MD-06: a differing code is refused and stored; an equal code in another case is ignored, and a change outside the shared fields leaves the party alone.
        CommandReply recoded = await _host.PostAsync(OrganisationBodies.CompanyUpdate(id), new { changed.legalName, changed.organisationNumber, changed.vatNumber, changed.countryCode, changed.baseCurrency, changed.timeZone, changed.fiscalYearStartMonth, changed.chartOfAccountsId, changed.roundingMode, changed.taxRoundingLevel, code = "SE99" }, "company-4-recode", ifMatch: "\"2\"", cancellationToken: Ct);
        CommandReply sameCode = await _host.PostAsync(OrganisationBodies.CompanyUpdate(id), new { changed.legalName, changed.organisationNumber, changed.vatNumber, changed.countryCode, changed.baseCurrency, timeZone = "Europe/Mariehamn", changed.fiscalYearStartMonth, changed.chartOfAccountsId, changed.roundingMode, changed.taxRoundingLevel, code = "se04" }, "company-4-same", ifMatch: "\"2\"", cancellationToken: Ct);
        Assert.Equal(422, recoded.Status);
        Assert.Equal(("R-MD-06", "company_code_fixed"), (recoded.RuleId, recoded.Code));
        Assert.Equal(1L, await _host.ScalarAsync("SELECT count(*) FROM kernel.idempotency_record WHERE key = 'company-4-recode'", Ct));
        Assert.True(sameCode.Status == 200, sameCode.Body);
        Assert.Equal(3, sameCode.Json!["version"]!.GetValue<long>());
        Assert.Equal(2L, await _host.ScalarAsync("SELECT version FROM kernel.party WHERE id = @id", Ct, ("id", partyId)));
        Assert.Equal(1L, await _host.ScalarAsync("SELECT count(*) FROM kernel.outbox_event WHERE event_type = 'PARTY_UPDATED'", Ct));
        Assert.Equal(2L, await _host.ScalarAsync("SELECT count(*) FROM kernel.outbox_event WHERE event_type = 'COMPANY_UPDATED'", Ct));
    }

    [Fact]
    public async Task R_MD_06_D_65_the_first_fiscal_year_fixes_the_start_month_and_the_first_posting_profile_rule_fixes_the_chart_one_error_per_attribute_stored_as_the_keys_result()
    {
        CommandReply chart = await _host.PostAsync(OrganisationBodies.Charts, new { code = "K3", name = "K3-kontoplan" }, "chart-k3", cancellationToken: Ct);
        Guid k3 = chart.Json!["id"]!.GetValue<Guid>();

        // SE01 has fiscal year 2026 and the posting profile rules of the fixture; both triggers have fired.
        CommandReply blocked = await _host.PostAsync(OrganisationBodies.CompanyUpdate(Se01), OrganisationBodies.Se01Update(fiscalYearStartMonth: 7, chartOfAccountsId: k3), "se01-fixed", ifMatch: "\"1\"", cancellationToken: Ct);
        CommandReply replay = await _host.PostAsync(OrganisationBodies.CompanyUpdate(Se01), OrganisationBodies.Se01Update(fiscalYearStartMonth: 7, chartOfAccountsId: k3), "se01-fixed", ifMatch: "\"1\"", cancellationToken: Ct);
        CommandReply monthOnly = await _host.PostAsync(OrganisationBodies.CompanyUpdate(Se01), OrganisationBodies.Se01Update(fiscalYearStartMonth: 7), "se01-month", ifMatch: "\"1\"", cancellationToken: Ct);
        CommandReply chartOnly = await _host.PostAsync(OrganisationBodies.CompanyUpdate(Se01), OrganisationBodies.Se01Update(chartOfAccountsId: k3), "se01-chart", ifMatch: "\"1\"", cancellationToken: Ct);

        Assert.Equal(422, blocked.Status);
        Assert.Equal("R-MD-06", blocked.RuleId);
        Assert.Equal(["company_chart_of_accounts_fixed", "company_fiscal_year_start_month_fixed"], blocked.Json!["errors"]!.AsArray().Select(error => error!["code"]!.GetValue<string>()).Order());
        Assert.Equal(422, replay.Status);
        Assert.True(replay.Replayed);
        Assert.Equal(422, monthOnly.Status);
        Assert.Equal(("R-MD-06", "company_fiscal_year_start_month_fixed"), (monthOnly.RuleId, monthOnly.Code));
        Assert.Equal(422, chartOnly.Status);
        Assert.Equal(("R-MD-06", "company_chart_of_accounts_fixed"), (chartOnly.RuleId, chartOnly.Code));
        Assert.Equal(1L, await _host.ScalarAsync("SELECT version FROM kernel.company WHERE id = @id", Ct, ("id", Se01)));

        // The other attributes stay changeable until the first posting (proven in CompanyPostingTests): the legal name here.
        CommandReply allowed = await _host.PostAsync(OrganisationBodies.CompanyUpdate(Se01), OrganisationBodies.Se01Update(legalName: "Themis Fictitious Fasteners Holding AB"), "se01-name", ifMatch: "\"1\"", cancellationToken: Ct);
        Assert.True(allowed.Status == 200, allowed.Body);
        Assert.Equal(2, allowed.Json!["version"]!.GetValue<long>());
        Assert.Equal("Themis Fictitious Fasteners Holding AB", await _host.ScalarAsync("SELECT name FROM kernel.party WHERE id = @id", Ct, ("id", Milestone1OrganisationFixture.CompanyPartyId)));

        // A company without a fiscal year or a rule changes both (D-65: the trigger, not the attribute, decides).
        CommandReply created = await _host.PostAsync(OrganisationBodies.Companies, OrganisationBodies.Company("SE05"), "company-5", cancellationToken: Ct);
        Guid id = created.Json!["id"]!.GetValue<Guid>();
        await OrganisationTestUsers.GrantFixtureAdministratorAsync(_host, id, Ct);
        CommandReply fresh = await _host.PostAsync(OrganisationBodies.CompanyUpdate(id), new { legalName = "Nordic Bolts AB", organisationNumber = "556677-8899", vatNumber = "SE556677889901", countryCode = "SE", baseCurrency = "EUR", timeZone = "Europe/Stockholm", fiscalYearStartMonth = 7, chartOfAccountsId = k3, roundingMode = "HalfAwayFromZero", taxRoundingLevel = "Line" }, "company-5-update", ifMatch: "\"1\"", cancellationToken: Ct);
        Assert.True(fresh.Status == 200, fresh.Body);
        Assert.Equal(7, fresh.Json!["fiscalYearStartMonth"]!.GetValue<int>());
        Assert.Equal(k3, fresh.Json["chartOfAccountsId"]!.GetValue<Guid>());
    }

    [Fact]
    public async Task R_MD_05_UPDATE_rechecks_the_organisation_number_against_the_other_companies_of_the_country_and_an_unknown_currency_or_chart_is_400()
    {
        CommandReply created = await _host.PostAsync(OrganisationBodies.Companies, OrganisationBodies.Company("SE02"), "company-2", cancellationToken: Ct);
        Guid id = created.Json!["id"]!.GetValue<Guid>();
        await OrganisationTestUsers.GrantFixtureAdministratorAsync(_host, id, Ct);

        CommandReply taken = await _host.PostAsync(OrganisationBodies.CompanyUpdate(id), new { legalName = "Nordic Bolts AB", organisationNumber = "5555555555", vatNumber = (string?)null, countryCode = "SE", baseCurrency = "EUR", timeZone = "Europe/Stockholm", fiscalYearStartMonth = 1, chartOfAccountsId = Bas, roundingMode = "HalfAwayFromZero", taxRoundingLevel = "Line" }, "company-2-taken", ifMatch: "\"1\"", cancellationToken: Ct);
        CommandReply currency = await _host.PostAsync(OrganisationBodies.CompanyUpdate(id), new { legalName = "Nordic Bolts AB", organisationNumber = "556677-8899", vatNumber = (string?)null, countryCode = "SE", baseCurrency = "XXX", timeZone = "Europe/Stockholm", fiscalYearStartMonth = 1, chartOfAccountsId = Bas, roundingMode = "HalfAwayFromZero", taxRoundingLevel = "Line" }, "company-2-currency", ifMatch: "\"1\"", cancellationToken: Ct);
        CommandReply chart = await _host.PostAsync(OrganisationBodies.CompanyUpdate(id), new { legalName = "Nordic Bolts AB", organisationNumber = "556677-8899", vatNumber = (string?)null, countryCode = "SE", baseCurrency = "EUR", timeZone = "Europe/Stockholm", fiscalYearStartMonth = 1, chartOfAccountsId = Guid.NewGuid(), roundingMode = "HalfAwayFromZero", taxRoundingLevel = "Line" }, "company-2-chart", ifMatch: "\"1\"", cancellationToken: Ct);
        CommandReply noMode = await _host.PostAsync(OrganisationBodies.CompanyUpdate(id), new { legalName = "Nordic Bolts AB", organisationNumber = "556677-8899", countryCode = "SE", baseCurrency = "EUR", timeZone = "Europe/Stockholm", fiscalYearStartMonth = 1, chartOfAccountsId = Bas, taxRoundingLevel = "Line" }, "company-2-mode", ifMatch: "\"1\"", cancellationToken: Ct);

        Assert.Equal(422, taken.Status);
        Assert.Equal(("R-MD-05", "organisation_number_duplicate"), (taken.RuleId, taken.Code));
        Assert.Equal(400, currency.Status);
        Assert.Equal("base_currency_unknown", currency.Code);
        Assert.Equal(400, chart.Status);
        Assert.Equal("chart_of_accounts_unknown", chart.Code);
        // The update restates the rounding mode; without it the shape check refuses before the transaction (RR-51).
        Assert.Equal(400, noMode.Status);
        Assert.Equal("rounding_mode_required", noMode.Json!["errors"]![0]!["code"]!.GetValue<string>());
        // Stored: the create and the three rejections found inside the transaction; the shape error is not (R-API-03, RR-51).
        Assert.Equal(4L, await _host.CountAsync("kernel.idempotency_record", Ct));
        Assert.Equal(1L, await _host.ScalarAsync("SELECT version FROM kernel.company WHERE id = @id", Ct, ("id", id)));
    }

    [Fact]
    public async Task R_API_02_D_47_UPDATE_needs_If_Match_a_stale_version_is_412_a_missing_company_is_404_and_the_scope_is_the_company_of_the_record_which_a_command_wide_assignment_gives()
    {
        CommandReply created = await _host.PostAsync(OrganisationBodies.Companies, OrganisationBodies.Company("SE02"), "company-2", cancellationToken: Ct);
        Guid id = created.Json!["id"]!.GetValue<Guid>();
        object body = new { legalName = "Nordic Bolts AB", organisationNumber = "556677-8899", vatNumber = "SE556677889901", countryCode = "SE", baseCurrency = "EUR", timeZone = "Europe/Stockholm", fiscalYearStartMonth = 1, chartOfAccountsId = Bas, roundingMode = "HalfAwayFromZero", taxRoundingLevel = "Document" };

        // The creator's installation-scope assignment does not reach the new company: COMPANY.UPDATE and COMPANY.READ on it are out of scope until step 13's administration, or a test grant.
        CommandReply outOfScope = await _host.PostAsync(OrganisationBodies.CompanyUpdate(id), body, "company-2-scope", ifMatch: "\"1\"", cancellationToken: Ct);
        CommandReply readOutOfScope = await _host.GetAsync($"{OrganisationBodies.Companies}/{id}", subject: TestUsers.OrganisationAdministratorSubject, cancellationToken: Ct);
        await OrganisationTestUsers.GrantFixtureAdministratorAsync(_host, id, Ct);
        CommandReply missing = await _host.PostAsync(OrganisationBodies.CompanyUpdate(id), body, "company-2-428", cancellationToken: Ct);
        CommandReply stale = await _host.PostAsync(OrganisationBodies.CompanyUpdate(id), body, "company-2-412", ifMatch: "\"3\"", cancellationToken: Ct);
        CommandReply unknown = await _host.PostAsync(OrganisationBodies.CompanyUpdate(Guid.NewGuid()), body, "company-404", ifMatch: "\"1\"", cancellationToken: Ct);
        CommandReply preparer = await _host.PostAsync(OrganisationBodies.CompanyUpdate(Se01), OrganisationBodies.Se01Update(), "se01-preparer", ifMatch: "\"1\"", subject: TestUsers.PreparerSubject, cancellationToken: Ct);
        CommandReply updated = await _host.PostAsync(OrganisationBodies.CompanyUpdate(id), body, "company-2-412", ifMatch: "\"1\"", cancellationToken: Ct);
        CommandReply read = await _host.GetAsync($"{OrganisationBodies.Companies}/{id}", subject: TestUsers.OrganisationAdministratorSubject, cancellationToken: Ct);

        Assert.Equal(403, outOfScope.Status);
        Assert.Equal(("R-SEC-01", "out_of_scope"), (outOfScope.RuleId, outOfScope.Code));
        Assert.Equal(403, readOutOfScope.Status);
        Assert.Equal(428, missing.Status);
        Assert.Equal("if_match_required", missing.Code);
        Assert.Equal(412, stale.Status);
        Assert.Equal(("R-API-02", "version_stale"), (stale.RuleId, stale.Code));
        Assert.Equal(404, unknown.Status);
        Assert.Equal("company_not_found", unknown.Code);
        Assert.Equal(403, preparer.Status);
        Assert.Equal("permission_missing", preparer.Code);
        Assert.Equal(200, updated.Status);
        Assert.False(updated.Replayed);
        Assert.Equal("\"2\"", updated.ETag);
        Assert.Equal("Document", updated.Json!["taxRoundingLevel"]!.GetValue<string>());
        Assert.Equal(200, read.Status);
        Assert.Equal(updated.Body, read.Body);
        Assert.Equal("\"2\"", read.ETag);
        // Only the create and the update were stored (R-API-03); 403, 404, 412 and 428 are not the key's result.
        Assert.Equal(2L, await _host.CountAsync("kernel.idempotency_record", Ct));
    }

    [Fact]
    public async Task R_SEC_01_RR_52_COMPANY_CREATE_needs_the_permission_and_a_company_wide_assignment_a_site_limited_administrator_and_the_clerk_are_403_before_anything_is_stored()
    {
        string siteLimited = await _users.AddAdministratorAsync(Se01, Milestone1OrganisationFixture.SiteId, Ct);

        CommandReply clerk = await _host.PostAsync(OrganisationBodies.Companies, OrganisationBodies.Company("SE02"), "company-clerk", subject: TestUsers.ClerkSubject, cancellationToken: Ct);
        CommandReply limited = await _host.PostAsync(OrganisationBodies.Companies, OrganisationBodies.Company("SE02"), "company-limited", subject: siteLimited, cancellationToken: Ct);
        CommandReply anonymous = await _host.PostAnonymousAsync(OrganisationBodies.Companies, OrganisationBodies.Company("SE02"), "company-anon", cancellationToken: Ct);

        Assert.Equal(403, clerk.Status);
        Assert.Equal(("R-SEC-01", "permission_missing"), (clerk.RuleId, clerk.Code));
        Assert.Equal(403, limited.Status);
        Assert.Equal(("R-SEC-01", "out_of_scope"), (limited.RuleId, limited.Code));
        Assert.Equal(401, anonymous.Status);
        Assert.Equal(1L, await _host.CountAsync("kernel.company", Ct));
        Assert.Equal(0L, await _host.CountAsync("kernel.idempotency_record", Ct));
        Assert.Equal(2L, await _host.ScalarAsync("SELECT next_number FROM kernel.number_series WHERE company_id IS NULL AND document_type = 'PARTY'", Ct));
    }
}
