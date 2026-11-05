using System.Data.Common;
using Themis.Kernel.Contracts.Organisation;
using Themis.Kernel.Contracts.Persistence;
using Themis.Kernel.Contracts.Posting;
using Themis.Kernel.Primitives;

namespace Themis.Kernel.Organisation;

/// <summary>A company whose lifetime balance on an account is not zero (D-69): the DEACTIVATE guard names each one.</summary>
internal sealed record AccountCompanyBalance(Code CompanyCode, decimal Balance);

/// <summary>
/// Raw SQL on the command's unit of work as themis_kernel (R-DB-01) for the account commands and queries of milestone 2
/// step 4: reads, the duplicate check, the insert, the versioned updates (lock class 1, R-API-02), the R-MD-06 trigger
/// checks and the DEACTIVATE balance guard. Nothing here deletes a row (R-MD-03).
/// </summary>
internal static class AccountStore
{
    private const string Columns = "id, chart_id, account_number, name, account_type, is_control, status, version";

    private static Dictionary<string, object?> Id(Guid id) => new() { ["id"] = id };

    public static async Task<AccountView?> FindAsync(IUnitOfWork unitOfWork, Guid id, CancellationToken cancellationToken)
    {
        IReadOnlyList<AccountView> rows = await unitOfWork.QueryAsync(DatabaseRole.Kernel, $"SELECT {Columns} FROM kernel.account WHERE id = @id", Id(id), Read, cancellationToken);
        return rows.Count == 0 ? null : rows[0];
    }

    /// <summary>The chart's accounts ordered by account number, at most <paramref name="limit"/> rows (step 4 brief, item 1).</summary>
    public static Task<IReadOnlyList<AccountView>> ListAsync(IUnitOfWork unitOfWork, Guid chartId, int limit, CancellationToken cancellationToken) =>
        unitOfWork.QueryAsync(DatabaseRole.Kernel, $"SELECT {Columns} FROM kernel.account WHERE chart_id = @chart ORDER BY account_number LIMIT @limit",
            new Dictionary<string, object?> { ["chart"] = chartId, ["limit"] = limit }, Read, cancellationToken);

    /// <summary>R-MD-05: the account number is unique per chart (ux_account_chart_number catches a race, RR-45).</summary>
    public static async Task<bool> NumberExistsAsync(IUnitOfWork unitOfWork, Guid chartId, Code number, CancellationToken cancellationToken) =>
        await unitOfWork.ScalarAsync(DatabaseRole.Kernel, "SELECT count(*) FROM kernel.account WHERE chart_id = @chart AND account_number = @number",
            new Dictionary<string, object?> { ["chart"] = chartId, ["number"] = number.Value }, cancellationToken) is long count && count > 0;

    public static Task InsertAsync(IUnitOfWork unitOfWork, AccountView account, DateTimeOffset now, UserId by, CancellationToken cancellationToken) =>
        unitOfWork.ExecuteAsync(DatabaseRole.Kernel,
            """
            INSERT INTO kernel.account (id, chart_id, account_number, name, account_type, is_control, status, created_at, created_by, updated_at, updated_by, version)
            VALUES (@id, @chart, @number, @name, @type, @control, @status, @now, @by, @now, @by, @version)
            """,
            new Dictionary<string, object?>
            {
                ["id"] = account.Id,
                ["chart"] = account.ChartId,
                ["number"] = account.AccountNumber.Value,
                ["name"] = account.Name,
                ["type"] = account.AccountType.ToString(),
                ["control"] = account.IsControl,
                ["status"] = account.Status.ToString(),
                ["now"] = now,
                ["by"] = by.Value,
                ["version"] = account.Version,
            },
            cancellationToken);

    /// <summary>
    /// Lock class 1 (R-LED-08): the versioned update of the account row with the name and, for the lifecycle commands,
    /// the status; 0 rows means the version is stale (R-API-02). It waits behind a posting's FOR SHARE of the account
    /// (D-69), so the guards that run after it in their own statements see a posting or rule that committed meanwhile.
    /// The type and the control flag are not written here: they follow in <see cref="SetFixedAttributesAsync"/> once the
    /// R-MD-06 checks passed, so a change the checks refuse never reaches fk_posting_profile_rule_account_control.
    /// </summary>
    public static Task<int> UpdateAsync(IUnitOfWork unitOfWork, Guid id, string name, MasterDataStatus status, long version, long expectedVersion, DateTimeOffset now, UserId by, CancellationToken cancellationToken) =>
        unitOfWork.ExecuteAsync(DatabaseRole.Kernel,
            "UPDATE kernel.account SET name = @name, status = @status, updated_at = @now, updated_by = @by, version = @version WHERE id = @id AND version = @expected",
            new Dictionary<string, object?>
            {
                ["id"] = id,
                ["name"] = name,
                ["status"] = status.ToString(),
                ["now"] = now,
                ["by"] = by.Value,
                ["version"] = version,
                ["expected"] = expectedVersion,
            },
            cancellationToken);

    /// <summary>The type and the control flag, on the row the versioned update locked, after the R-MD-06 checks passed (D-69).</summary>
    public static async Task SetFixedAttributesAsync(IUnitOfWork unitOfWork, Guid id, AccountType type, bool isControl, CancellationToken cancellationToken)
    {
        int rows = await unitOfWork.ExecuteAsync(DatabaseRole.Kernel, "UPDATE kernel.account SET account_type = @type, is_control = @control WHERE id = @id",
            new Dictionary<string, object?> { ["id"] = id, ["type"] = type.ToString(), ["control"] = isControl }, cancellationToken);
        if (rows != 1)
        {
            throw new InvalidOperationException($"Account {id} disappeared under its own row lock; accounts are never deleted (R-MD-03).");
        }
    }

    /// <summary>R-MD-06 (D-69): the account's first journal line or first posting profile rule, in any company, fixes its type and control flag.</summary>
    public static async Task<bool> HasJournalLinesOrRulesAsync(IUnitOfWork unitOfWork, Guid id, CancellationToken cancellationToken) =>
        await unitOfWork.ScalarAsync(DatabaseRole.Kernel,
            "SELECT EXISTS (SELECT 1 FROM kernel.journal_line WHERE account_id = @id) OR EXISTS (SELECT 1 FROM kernel.posting_profile_rule WHERE account_id = @id)",
            Id(id), cancellationToken) is true;

    /// <summary>D-69: the companies whose balance on the account over all history is not zero, ordered by company code.</summary>
    public static Task<IReadOnlyList<AccountCompanyBalance>> NonZeroBalancesAsync(IUnitOfWork unitOfWork, Guid id, CancellationToken cancellationToken) =>
        unitOfWork.QueryAsync(DatabaseRole.Kernel,
            """
            SELECT c.code, sum(l.amount)
            FROM kernel.journal_line l
            JOIN kernel.company c ON c.id = l.company_id
            WHERE l.account_id = @id
            GROUP BY c.code
            HAVING sum(l.amount) <> 0
            ORDER BY c.code
            """,
            Id(id),
            reader => new AccountCompanyBalance(Code.From(reader.GetString(0)), reader.GetDecimal(1)),
            cancellationToken);

    private static AccountView Read(DbDataReader reader) => new(
        reader.GetGuid(0),
        reader.GetGuid(1),
        Code.From(reader.GetString(2)),
        reader.GetString(3),
        Enum.Parse<AccountType>(reader.GetString(4)),
        reader.GetBoolean(5),
        Enum.Parse<MasterDataStatus>(reader.GetString(6)),
        reader.GetInt64(7));
}
