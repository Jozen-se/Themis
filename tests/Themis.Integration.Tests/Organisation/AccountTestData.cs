using Npgsql;
using Themis.Integration.Tests.Pipeline;
using Themis.Kernel.Contracts.Organisation;
using Themis.Kernel.Contracts.Posting;
using Themis.Kernel.Contracts.Security;
using Themis.Kernel.Organisation;
using Themis.Kernel.Persistence.Seeding;
using Themis.Kernel.Posting;
using Themis.Kernel.Primitives;

namespace Themis.Integration.Tests.Organisation;

/// <summary>
/// The test data of the account tests (milestone 2 step 4 brief, item 10; docs/development/test-seams.md): posting
/// profile rules come from the test and development seeder (GeneralLedgerFixtureSeeder) until step 5 adds the rule
/// commands; fiscal years with Closed periods are written as rows by the migrator role; the account status seam of item
/// 4 sets an account's status without the R-PP-05 guard, on its own connection, either at once or held open in a
/// transaction. <see cref="PostgresFixture.TruncateAsync"/> removes every rule, account and fiscal year these write and
/// restores the fixture's own (RR-133).
/// </summary>
internal static class AccountTestData
{
    public const string Accounts = "/v1/kernel/accounts";

    public static readonly Guid Se01 = Milestone1OrganisationFixture.CompanyId;
    public static readonly Guid Bas = Milestone1OrganisationFixture.ChartOfAccountsId;

    public static string Update(Guid id) => $"{Accounts}/{id}/update";

    public static string Lifecycle(Guid id, string verb) => $"{Accounts}/{id}/{verb}";

    public static string ChartAccounts(Guid chart) => $"/v1/kernel/charts-of-accounts/{chart}/accounts";

    public static object Create(string number, string type = "Expense", bool isControl = false, Guid? chartId = null, string name = "Test account") =>
        new { chartId = chartId ?? Bas, accountNumber = number, name, accountType = type, isControl };

    /// <summary>
    /// Seeds posting profile rules through the test and development seeder, with the seeder's own rule check against the
    /// companies' charts unless <paramref name="checkCharts"/> is false: a rule whose account lies in another chart than its
    /// company's can be written until Gate B adds that constraint (step 4 brief: the check runs by account, not by chart).
    /// </summary>
    public static async Task SeedRulesAsync(PostgresFixture db, IReadOnlyList<PostingProfileRuleDefinition> rules, IReadOnlyDictionary<Guid, Guid> chartByCompany, bool checkCharts, CancellationToken cancellationToken)
    {
        List<AccountDefinition> accounts = [];
        foreach (Guid accountId in rules.Select(rule => rule.AccountId).Distinct())
        {
            accounts.Add(await AccountDefinitionAsync(db, accountId, cancellationToken));
        }

        GeneralLedgerFixtureDefinition fixture = new(accounts, rules);
        Result<int> seeded = await GeneralLedgerFixtureSeeder.ApplyAsync(db.ConnectionStringFor("themis_migrator"), fixture,
            definition => checkCharts ? PostingProfileValidator.CheckFixture(definition, chartByCompany) : Result.Success(),
            TimeProvider.System.GetUtcNow(), WellKnownUsers.System, cancellationToken);
        Assert.True(seeded.IsSuccess, seeded.IsFailure ? seeded.Primary!.Message : null);

        // The seeder writes a SYSTEM. audit event per rule, which the shared cleanup keeps for the fixture's own rows; these are
        // test data, so their events go now, through the cleanup seam (D-60), and the fixture's audit counts stay the seeder's.
        await db.CleanupAsync($"DELETE FROM kernel.audit_event WHERE entity_type = 'PostingProfileRule' AND entity_id IN ({string.Join(", ", rules.Select(rule => $"'{rule.Id}'"))})", cancellationToken);
    }

    /// <summary>A fiscal year of the company with its twelve monthly periods, the first <paramref name="closedPeriods"/> of them Closed.</summary>
    public static async Task InsertFiscalYearAsync(PostgresFixture db, Guid company, int year, int closedPeriods, CancellationToken cancellationToken)
    {
        Guid yearId = Guid.NewGuid();
        string system = WellKnownUsers.System.Value.ToString();
        string audit = $"now(), '{system}', now(), '{system}'";
        System.Text.StringBuilder sql = new($"INSERT INTO kernel.fiscal_year (id, company_id, code, start_date, end_date, status, created_at, created_by, updated_at, updated_by) VALUES ('{yearId}', '{company}', 'T{year}', '{year}-01-01', '{year}-12-31', 'Open', {audit}); ");
        for (int period = 1; period <= 12; period++)
        {
            DateOnly start = new(year, period, 1);
            string status = period <= closedPeriods ? "Closed" : "Open";
            sql.Append($"INSERT INTO kernel.fiscal_period (id, fiscal_year_id, company_id, period_number, start_date, end_date, status, created_at, created_by, updated_at, updated_by) VALUES (gen_random_uuid(), '{yearId}', '{company}', {period}, '{start:yyyy-MM-dd}', '{start.AddMonths(1).AddDays(-1):yyyy-MM-dd}', '{status}', {audit}); ");
        }

        await ExecuteAsync(db.ConnectionStringFor("themis_migrator"), sql.ToString(), cancellationToken);
    }

    /// <summary>The account status seam of item 4, committed at once: the status changes with no R-PP-05 guard and no audit event.</summary>
    public static Task SetStatusAsync(PostgresFixture db, Guid account, MasterDataStatus status, CancellationToken cancellationToken) =>
        ExecuteAsync(db.ConnectionStringFor("themis_migrator"), $"UPDATE kernel.account SET status = '{status}' WHERE id = '{account}'", cancellationToken);

    /// <summary>The account status seam of item 4, held: the UPDATE runs in a transaction left open until <see cref="HeldStatus.CommitAsync"/>.</summary>
    public static async Task<HeldStatus> HoldStatusAsync(PostgresFixture db, Guid account, MasterDataStatus status, CancellationToken cancellationToken)
    {
        NpgsqlConnection connection = await PostgresFixture.OpenAsync(db.ConnectionStringFor("themis_migrator"), cancellationToken);
        NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using NpgsqlCommand command = new($"UPDATE kernel.account SET status = '{status}' WHERE id = '{account}'", connection, transaction);
        await command.ExecuteNonQueryAsync(cancellationToken);
        return new HeldStatus(connection, transaction);
    }

    /// <summary>Waits until a backend of the test database waits on a lock, so the second statement is proven to queue behind the first.</summary>
    public static async Task WaitForLockWaiterAsync(PipelineTestHost host, CancellationToken cancellationToken)
    {
        for (int attempt = 0; attempt < 600; attempt++)
        {
            if ((long)(await host.ScalarAsync("SELECT count(*) FROM pg_stat_activity WHERE datname = current_database() AND wait_event_type = 'Lock'", cancellationToken))! > 0)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(25), cancellationToken);
        }

        Assert.Fail("No backend waited on a lock within 15 seconds.");
    }

    /// <summary>A company created through COMPANY.CREATE on the chart, with no fiscal year; the fixture administrator gets a company-wide assignment in it.</summary>
    public static async Task<Guid> CreateCompanyAsync(PipelineTestHost host, string code, string organisationNumber, Guid? chart, CancellationToken cancellationToken)
    {
        CommandReply reply = await host.PostAsync(OrganisationBodies.Companies, OrganisationBodies.Company(code, legalName: $"Company {code}", organisationNumber: organisationNumber, vatNumber: null, chartOfAccountsId: chart), "company-" + code, cancellationToken: cancellationToken);
        Assert.True(reply.Status == 200, reply.Body);
        Guid company = reply.Json!["id"]!.GetValue<Guid>();
        await OrganisationTestUsers.GrantFixtureAdministratorAsync(host, company, cancellationToken);
        return company;
    }

    public static async Task<(Guid Id, CommandReply Reply)> CreateAccountAsync(PipelineTestHost host, string number, string type = "Expense", bool isControl = false, Guid? chart = null, CancellationToken cancellationToken = default)
    {
        CommandReply reply = await host.PostAsync(Accounts, Create(number, type, isControl, chart), "account-" + number + "-" + chart, cancellationToken: cancellationToken);
        Assert.True(reply.Status == 200, reply.Body);
        return (reply.Json!["id"]!.GetValue<Guid>(), reply);
    }

    /// <summary>A rule of the INVENTORY_ADJUSTMENT role, which expects a non-control account (R-PP-04), keyed by item group TRADE_GOODS and a reason code.</summary>
    public static PostingProfileRuleDefinition AdjustmentRule(Guid company, Guid account, ReasonCode reason, string validFrom, string? validTo = null) =>
        new(Guid.NewGuid(), company, AccountRoles.InventoryAdjustment, account, BusinessDate.Parse(validFrom), validTo is null ? null : BusinessDate.Parse(validTo), ItemPostingGroup: Code.From(Milestone1GeneralLedgerFixture.TradeGoods), ReasonCode: reason);

    private static async Task<AccountDefinition> AccountDefinitionAsync(PostgresFixture db, Guid accountId, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await PostgresFixture.OpenAsync(db.ConnectionStringFor("themis_migrator"), cancellationToken);
        await using NpgsqlCommand command = new("SELECT chart_id, account_number, name, account_type, is_control, status FROM kernel.account WHERE id = @id", connection);
        command.Parameters.AddWithValue("id", accountId);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        Assert.True(await reader.ReadAsync(cancellationToken), $"Account {accountId} does not exist.");
        return new AccountDefinition(accountId, reader.GetGuid(0), Code.From(reader.GetString(1)), reader.GetString(2), Enum.Parse<AccountType>(reader.GetString(3)), reader.GetBoolean(4), Enum.Parse<MasterDataStatus>(reader.GetString(5)));
    }

    private static async Task ExecuteAsync(string connectionString, string sql, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await PostgresFixture.OpenAsync(connectionString, cancellationToken);
        await using NpgsqlCommand command = new(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}

/// <summary>An account status change held open in its own transaction (item 4 seam), committed or rolled back by the test.</summary>
internal sealed class HeldStatus : IAsyncDisposable
{
    private readonly NpgsqlConnection _connection;
    private readonly NpgsqlTransaction _transaction;
    private bool _done;

    public HeldStatus(NpgsqlConnection connection, NpgsqlTransaction transaction)
    {
        _connection = connection;
        _transaction = transaction;
    }

    public async Task CommitAsync(CancellationToken cancellationToken)
    {
        await _transaction.CommitAsync(cancellationToken);
        _done = true;
    }

    public async ValueTask DisposeAsync()
    {
        if (!_done)
        {
            await _transaction.RollbackAsync(CancellationToken.None);
        }

        await _transaction.DisposeAsync();
        await _connection.DisposeAsync();
    }
}
