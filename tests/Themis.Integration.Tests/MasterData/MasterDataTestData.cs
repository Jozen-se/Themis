using Npgsql;
using Themis.Kernel.Contracts.Security;
using Themis.Kernel.Organisation;

namespace Themis.Integration.Tests.MasterData;

/// <summary>
/// Rows a test adds beside the milestone 1 fixture, written as themis_migrator and removed again when the test
/// ends: a second company with its own site and warehouse (for the other-company warehouse check and the
/// company-scope reads), a second warehouse of SE01, and an item that has no row in SE01. Test scaffolding only;
/// nothing here goes through the seeder, the pipeline or the audit trail.
/// </summary>
internal sealed class MasterDataTestData : IAsyncDisposable
{
    private readonly PostgresFixture _db;
    private readonly List<string> _cleanup = [];

    private MasterDataTestData(PostgresFixture db)
    {
        _db = db;
    }

    public Guid OtherCompany { get; } = Guid.NewGuid();

    public Guid OtherSite { get; } = Guid.NewGuid();

    public Guid OtherWarehouse { get; } = Guid.NewGuid();

    /// <summary>A second warehouse of SE01 on its main site, so a test can move a document's header default between two warehouses of one company.</summary>
    public Guid SecondWarehouse { get; } = Guid.NewGuid();

    /// <summary>An installation-wide item with no ItemCompany row anywhere.</summary>
    public Guid OrphanItem { get; } = Guid.NewGuid();

    public const string OrphanItemNumber = "ORPHAN-001";

    public static async Task<MasterDataTestData> CreateAsync(PostgresFixture db, CancellationToken cancellationToken)
    {
        MasterDataTestData data = new(db);
        string system = WellKnownUsers.System.Value.ToString();
        string audit = $"now(), '{system}', now(), '{system}'";

        await data.RunAsync(
            $"""
            INSERT INTO kernel.company (id, code, party_id, legal_name, organisation_number, vat_number, country_code, base_currency, time_zone, fiscal_year_start_month, chart_of_accounts_id, rounding_mode, tax_rounding_level, payment_tolerance, status, created_at, created_by, updated_at, updated_by)
            VALUES ('{data.OtherCompany}', 'SE02', '{Milestone1OrganisationFixture.CompanyPartyId}', 'Other company', '5555555556', NULL, 'SE', 'EUR', 'Europe/Stockholm', 1, '{Milestone1OrganisationFixture.ChartOfAccountsId}', 'HalfAwayFromZero', 'Line', NULL, 'Active', {audit})
            """, cancellationToken);
        data._cleanup.Add($"DELETE FROM kernel.company WHERE id = '{data.OtherCompany}'");
        await data.RunAsync($"INSERT INTO kernel.site (id, company_id, code, name, created_at, created_by, updated_at, updated_by) VALUES ('{data.OtherSite}', '{data.OtherCompany}', 'MAIN', 'Other main', {audit})", cancellationToken);
        data._cleanup.Insert(0, $"DELETE FROM kernel.site WHERE id = '{data.OtherSite}'");
        await data.RunAsync($"INSERT INTO kernel.warehouse (id, site_id, company_id, code, name, is_default, status, created_at, created_by, updated_at, updated_by) VALUES ('{data.OtherWarehouse}', '{data.OtherSite}', '{data.OtherCompany}', 'MAIN', 'Other warehouse', true, 'Active', {audit})", cancellationToken);
        data._cleanup.Insert(0, $"DELETE FROM kernel.warehouse WHERE id = '{data.OtherWarehouse}'");
        await data.RunAsync($"INSERT INTO kernel.warehouse (id, site_id, company_id, code, name, is_default, status, created_at, created_by, updated_at, updated_by) VALUES ('{data.SecondWarehouse}', '{Milestone1OrganisationFixture.SiteId}', '{Milestone1OrganisationFixture.CompanyId}', 'SECOND', 'Second warehouse', false, 'Active', {audit})", cancellationToken);
        data._cleanup.Insert(0, $"DELETE FROM kernel.warehouse WHERE id = '{data.SecondWarehouse}'");

        await data.RunAsync($"INSERT INTO kernel.item (id, item_number, description, item_type, base_unit, lot_tracking, created_at, created_by, updated_at, updated_by) VALUES ('{data.OrphanItem}', '{OrphanItemNumber}', 'Item without a company row', 'Stocked', 'H87', 'None', {audit})", cancellationToken);
        data._cleanup.Insert(0, $"DELETE FROM kernel.item WHERE id = '{data.OrphanItem}'");
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
