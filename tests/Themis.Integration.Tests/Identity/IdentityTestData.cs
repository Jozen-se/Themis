using Npgsql;
using Themis.Kernel.Identity;
using Themis.Kernel.Organisation;

namespace Themis.Integration.Tests.Identity;

/// <summary>
/// Rows a test adds beside the milestone 1 fixture, written as themis_migrator and removed again when the test
/// ends: a second site, a disabled user, users with an expired or a site-limited assignment. Test scaffolding only;
/// nothing here goes through the seeder or the audit trail.
/// </summary>
internal sealed class IdentityTestData : IAsyncDisposable
{
    public static readonly Guid Company = Milestone1OrganisationFixture.CompanyId;
    public static readonly Guid MainSite = Milestone1OrganisationFixture.SiteId;

    private readonly PostgresFixture _db;
    private readonly List<string> _cleanup = [];

    private IdentityTestData(PostgresFixture db)
    {
        _db = db;
    }

    public Guid OtherSite { get; } = Guid.NewGuid();

    public Guid DisabledUser { get; } = Guid.NewGuid();

    public string DisabledSubject { get; } = "disabled-" + Guid.NewGuid().ToString("N");

    public Guid ExpiredUser { get; } = Guid.NewGuid();

    public string ExpiredSubject { get; } = "expired-" + Guid.NewGuid().ToString("N");

    public Guid OtherSiteUser { get; } = Guid.NewGuid();

    public string OtherSiteSubject { get; } = "othersite-" + Guid.NewGuid().ToString("N");

    /// <summary>Holds the poster role only, company-wide: may post, lacks ITEM.CREATE and ITEM.READ.</summary>
    public Guid PosterOnlyUser { get; } = Guid.NewGuid();

    public string PosterOnlySubject { get; } = "posteronly-" + Guid.NewGuid().ToString("N");

    public static async Task<IdentityTestData> CreateAsync(PostgresFixture db, CancellationToken cancellationToken)
    {
        IdentityTestData data = new(db);
        string system = Themis.Kernel.Contracts.Security.WellKnownUsers.System.Value.ToString();
        string preparerRole = Milestone1IdentityFixture.PreparerRoleId.ToString();
        string posterRole = Milestone1IdentityFixture.PosterRoleId.ToString();
        string maintainerRole = Milestone1IdentityFixture.ItemMaintainerRoleId.ToString();
        string touchRole = FixtureSchema.TouchRoleId.ToString();

        await data.RunAsync($"INSERT INTO kernel.site (id, company_id, code, name, created_at, created_by, updated_at, updated_by) VALUES ('{data.OtherSite}', '{Company}', 'OTHER', 'Other site', now(), '{system}', now(), '{system}')", cancellationToken);
        data._cleanup.Add($"DELETE FROM kernel.site WHERE id = '{data.OtherSite}'");

        foreach ((Guid id, string subject, string status) in new[] { (data.DisabledUser, data.DisabledSubject, "Disabled"), (data.ExpiredUser, data.ExpiredSubject, "Active"), (data.OtherSiteUser, data.OtherSiteSubject, "Active"), (data.PosterOnlyUser, data.PosterOnlySubject, "Active") })
        {
            await data.RunAsync($"INSERT INTO kernel.user_account (id, external_subject, display_name, is_service_account, status, created_at, created_by, updated_at, updated_by) VALUES ('{id}', '{subject}', 'Test {status}', false, '{status}', now(), '{system}', now(), '{system}')", cancellationToken);
            data._cleanup.Insert(0, $"DELETE FROM kernel.user_account WHERE id = '{id}'");
        }

        // The disabled user holds the preparer role; the expired user held it until 2026-02-01; the other-site user
        // holds all three roles (preparer, poster, item maintainer) and the test-only FIXTURE_TESTER, limited to the other site.
        string assignments = $"""
            INSERT INTO kernel.role_assignment (id, user_id, role_id, company_id, site_id, valid_from, valid_to, assigned_by, created_at, created_by, updated_at, updated_by) VALUES
            ('{Guid.NewGuid()}', '{data.DisabledUser}', '{preparerRole}', '{Company}', NULL, '2026-01-01', NULL, '{system}', now(), '{system}', now(), '{system}'),
            ('{Guid.NewGuid()}', '{data.ExpiredUser}', '{preparerRole}', '{Company}', NULL, '2026-01-01', '2026-02-01', '{system}', now(), '{system}', now(), '{system}'),
            ('{Guid.NewGuid()}', '{data.OtherSiteUser}', '{preparerRole}', '{Company}', '{data.OtherSite}', '2026-01-01', NULL, '{system}', now(), '{system}', now(), '{system}'),
            ('{Guid.NewGuid()}', '{data.OtherSiteUser}', '{posterRole}', '{Company}', '{data.OtherSite}', '2026-01-01', NULL, '{system}', now(), '{system}', now(), '{system}'),
            ('{Guid.NewGuid()}', '{data.OtherSiteUser}', '{maintainerRole}', '{Company}', '{data.OtherSite}', '2026-01-01', NULL, '{system}', now(), '{system}', now(), '{system}'),
            ('{Guid.NewGuid()}', '{data.OtherSiteUser}', '{touchRole}', '{Company}', '{data.OtherSite}', '2026-01-01', NULL, '{system}', now(), '{system}', now(), '{system}'),
            ('{Guid.NewGuid()}', '{data.PosterOnlyUser}', '{posterRole}', '{Company}', NULL, '2026-01-01', NULL, '{system}', now(), '{system}', now(), '{system}')
            """;
        await data.RunAsync(assignments, cancellationToken);
        data._cleanup.Insert(0, $"DELETE FROM kernel.role_assignment WHERE user_id IN ('{data.DisabledUser}', '{data.ExpiredUser}', '{data.OtherSiteUser}', '{data.PosterOnlyUser}')");
        data._cleanup.Insert(0, $"DELETE FROM kernel.idempotency_record WHERE user_id IN ('{data.DisabledUser}', '{data.ExpiredUser}', '{data.OtherSiteUser}', '{data.PosterOnlyUser}')");
        data._cleanup.Insert(0, $"DELETE FROM kernel.audit_event WHERE user_id IN ('{data.DisabledUser}', '{data.ExpiredUser}', '{data.OtherSiteUser}', '{data.PosterOnlyUser}')");
        return data;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (string statement in _cleanup)
        {
            await RunAsync(statement, CancellationToken.None);
        }
    }

    private async Task RunAsync(string sql, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await PostgresFixture.OpenAsync(_db.ConnectionStringFor("themis_migrator"), cancellationToken);
        await using NpgsqlCommand command = new(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
