using System.Security.Claims;
using Microsoft.Extensions.Logging;
using Themis.Integration.Tests.Pipeline;
using Themis.Kernel.Contracts.Security;
using Themis.Kernel.Identity;
using Themis.Kernel.Organisation;

namespace Themis.Integration.Tests.Identity;

/// <summary>
/// Step 4 end to end through the test token issuer: tokens are validated (issuer, audience, signature, lifetime;
/// 401 when not), the subject is the only claim read, an unknown subject and a disabled user get 403 and a log
/// line, a site-limited or expired assignment is out of scope or missing (R-SEC-01, D-47), and the query of the
/// company needs COMPANY.READ in that company.
/// </summary>
[Collection(PostgresTestGroup.Name)]
[Trait("Category", "Integration")]
public sealed class TokenAuthenticationTests : IAsyncLifetime
{
    private const string Counters = "/v1/fixture/counters";

    private readonly PostgresFixture _db;
    private PipelineTestHost _host = null!;
    private IdentityTestData _data = null!;

    public TokenAuthenticationTests(PostgresFixture db)
    {
        _db = db;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string CompanyUrl => $"/v1/kernel/companies/{Milestone1OrganisationFixture.CompanyId}";

    public async ValueTask InitializeAsync()
    {
        await _db.TruncateAsync(Ct);
        _data = await IdentityTestData.CreateAsync(_db, Ct);
        _host = await PipelineTestHost.StartAsync(_db);
    }

    public async ValueTask DisposeAsync()
    {
        await _host.DisposeAsync();
        await _data.DisposeAsync();
    }

    [Fact]
    public async Task An_invalid_expired_wrong_audience_or_wrong_issuer_token_is_401_and_a_valid_unknown_subject_is_403()
    {
        TimeProvider clock = TimeProvider.System;
        string subject = Milestone1IdentityFixture.ClerkSubject;

        CommandReply wrongKey = await _host.GetAsync(CompanyUrl, token: TestTokens.WrongKey(subject, clock), cancellationToken: Ct);
        CommandReply expired = await _host.GetAsync(CompanyUrl, token: TestTokens.Expired(subject, clock), cancellationToken: Ct);
        CommandReply wrongAudience = await _host.GetAsync(CompanyUrl, token: TestTokens.For(subject, clock, audience: "other-api"), cancellationToken: Ct);
        CommandReply wrongIssuer = await _host.GetAsync(CompanyUrl, token: TestTokens.For(subject, clock, issuer: "https://someone-else"), cancellationToken: Ct);
        CommandReply garbage = await _host.GetAsync(CompanyUrl, token: "not-a-token", cancellationToken: Ct);
        CommandReply unknown = await _host.GetAsync(CompanyUrl, subject: "stranger-" + Guid.NewGuid().ToString("N"), cancellationToken: Ct);

        Assert.All(new[] { wrongKey, expired, wrongAudience, wrongIssuer, garbage }, reply =>
        {
            Assert.Equal(401, reply.Status);
            Assert.Equal("authentication_required", reply.Code);
        });
        Assert.Equal(403, unknown.Status);
        Assert.Equal(AccessDenied.SubjectUnknown, unknown.Code);
        Assert.Equal("R-SEC-01", unknown.RuleId);
        Assert.Contains(_host.Logs.Lines, line => line.Level == LogLevel.Warning && line.Message.Contains("subject_unknown", StringComparison.Ordinal) && line.Message.Contains("stranger-", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_disabled_user_is_403_with_its_own_code_and_a_log_line_and_no_audit_event()
    {
        CommandReply reply = await _host.PostAsync(Counters, new { companyId = IdentityTestData.Company, initialValue = 1, sample = "1" }, "disabled-1", subject: _data.DisabledSubject, cancellationToken: Ct);

        Assert.Equal(403, reply.Status);
        Assert.Equal(AccessDenied.UserDisabled, reply.Code);
        Assert.Equal("R-SEC-01", reply.RuleId);
        Assert.Equal(0L, await _host.ScalarAsync("SELECT count(*) FROM kernel.audit_event WHERE user_id = @user", Ct, ("user", _data.DisabledUser)));
        Assert.Equal(0L, await _host.ScalarAsync("SELECT count(*) FROM kernel.idempotency_record WHERE key = 'disabled-1'", Ct));
        Assert.Contains(_host.Logs.Lines, line => line.Level == LogLevel.Warning && line.Message.Contains("user_disabled", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Roles_and_groups_in_the_token_are_ignored_permissions_come_from_the_tables_only()
    {
        Claim[] claims = [new Claim("roles", "admin"), new Claim("groups", "/everything"), new Claim("realm_access", "{\"roles\":[\"admin\"]}"), new Claim("email", Milestone1IdentityFixture.ClerkSubject)];
        string preparerWithRoles = TestTokens.For(Milestone1IdentityFixture.PreparerSubject, TimeProvider.System, claims);
        string strangerWithRoles = TestTokens.For("stranger-" + Guid.NewGuid().ToString("N"), TimeProvider.System, claims);
        Guid id = await CreateAsync("roles-create");

        // FIXTURE.TOUCH belongs to the clerk only; a token claiming roles does not add it.
        CommandReply preparer = await _host.PostAsync($"{Counters}/{id}/touch", new { increment = 1 }, "roles-1", ifMatch: "\"1\"", token: preparerWithRoles, cancellationToken: Ct);
        CommandReply stranger = await _host.GetAsync(CompanyUrl, token: strangerWithRoles, cancellationToken: Ct);

        Assert.Equal(403, preparer.Status);
        Assert.Equal(AccessDenied.PermissionMissing, preparer.Code);
        Assert.Equal(403, stranger.Status);
        Assert.Equal(AccessDenied.SubjectUnknown, stranger.Code);
    }

    [Fact]
    public async Task D_47_a_site_limited_assignment_covers_its_site_only_and_never_a_company_wide_command()
    {
        object atOtherSite = new { companyId = IdentityTestData.Company, siteId = _data.OtherSite, initialValue = 1, sample = "1" };
        object atMainSite = new { companyId = IdentityTestData.Company, siteId = IdentityTestData.MainSite, initialValue = 1, sample = "1" };
        object companyWide = new { companyId = IdentityTestData.Company, initialValue = 1, sample = "1" };

        CommandReply ownSite = await _host.PostAsync(Counters, atOtherSite, "site-1", subject: _data.OtherSiteSubject, cancellationToken: Ct);
        CommandReply otherSite = await _host.PostAsync(Counters, atMainSite, "site-2", subject: _data.OtherSiteSubject, cancellationToken: Ct);
        CommandReply noSite = await _host.PostAsync(Counters, companyWide, "site-3", subject: _data.OtherSiteSubject, cancellationToken: Ct);
        Guid id = ownSite.Json!["id"]!.GetValue<Guid>();
        // The scope of a command on the counter comes from the row, loaded inside the transaction (the other-site user holds FIXTURE.TOUCH at its site).
        CommandReply postOwn = await _host.PostAsync($"{Counters}/{id}/touch", new { increment = 1 }, "site-4", ifMatch: "\"1\"", subject: _data.OtherSiteSubject, cancellationToken: Ct);
        CommandReply clerkEverywhere = await _host.PostAsync(Counters, atOtherSite, "site-5", cancellationToken: Ct);

        Assert.Equal(200, ownSite.Status);
        Assert.Equal(403, otherSite.Status);
        Assert.Equal(AccessDenied.OutOfScope, otherSite.Code);
        Assert.Equal("R-SEC-01", otherSite.RuleId);
        Assert.Equal(403, noSite.Status);
        Assert.Equal(AccessDenied.OutOfScope, noSite.Code);
        Assert.Equal(200, postOwn.Status);
        Assert.Equal(200, clerkEverywhere.Status);
    }

    [Fact]
    public async Task R_TIME_01_an_expired_assignment_is_permission_missing_and_the_query_needs_COMPANY_READ_in_the_company()
    {
        CommandReply expired = await _host.PostAsync(Counters, new { companyId = IdentityTestData.Company, initialValue = 1, sample = "1" }, "expired-1", subject: _data.ExpiredSubject, cancellationToken: Ct);
        CommandReply expiredRead = await _host.GetAsync(CompanyUrl, subject: _data.ExpiredSubject, cancellationToken: Ct);
        CommandReply otherSiteRead = await _host.GetAsync(CompanyUrl, subject: _data.OtherSiteSubject, cancellationToken: Ct);
        CommandReply clerkRead = await _host.GetAsync(CompanyUrl, cancellationToken: Ct);

        Assert.Equal(403, expired.Status);
        Assert.Equal(AccessDenied.PermissionMissing, expired.Code);
        Assert.Equal(403, expiredRead.Status);
        Assert.Equal(AccessDenied.PermissionMissing, expiredRead.Code);
        // D-47: a site-limited assignment never satisfies a query without a site.
        Assert.Equal(403, otherSiteRead.Status);
        Assert.Equal(AccessDenied.OutOfScope, otherSiteRead.Code);
        Assert.Equal(200, clerkRead.Status);
    }

    [Fact]
    public async Task R_AUD_01_every_audit_event_carries_the_acting_user_and_seeded_rows_carry_the_system_user()
    {
        Guid id = await CreateAsync("audit-create");
        await _host.PostAsync($"{Counters}/{id}/touch", new { increment = 1 }, "audit-touch", ifMatch: "\"1\"", cancellationToken: Ct);

        Assert.Equal(0L, await _host.ScalarAsync("SELECT count(*) FROM kernel.audit_event e LEFT JOIN kernel.user_account u ON u.id = e.user_id WHERE u.id IS NULL", Ct));
        Assert.Equal(Milestone1IdentityFixture.ClerkId, await _host.ScalarAsync("SELECT DISTINCT user_id FROM kernel.audit_event WHERE command_name IN ('ITEM.CREATE', 'FIXTURE.TOUCH')", Ct));
        Assert.Equal(WellKnownUsers.System.Value, await _host.ScalarAsync("SELECT DISTINCT user_id FROM kernel.audit_event WHERE command_name LIKE 'SYSTEM.%'", Ct));
        Assert.Equal(WellKnownUsers.System.Value, await _host.ScalarAsync("SELECT DISTINCT created_by FROM kernel.role_assignment", Ct));
    }

    private async Task<Guid> CreateAsync(string key)
    {
        CommandReply reply = await _host.PostAsync(Counters, new { companyId = IdentityTestData.Company, initialValue = 5, sample = "1.5" }, key, cancellationToken: Ct);
        Assert.Equal(200, reply.Status);
        return reply.Json!["id"]!.GetValue<Guid>();
    }
}
