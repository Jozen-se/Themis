using Themis.Kernel.Contracts.Posting;

namespace Themis.Kernel.Persistence.Privileges;

/// <summary>
/// The database constraints generated from the account role catalogue (<see cref="AccountRoles"/>), so the role
/// list, R-PP-06 and R-PP-04 are enforced by the database without a table (step 6 brief, D-46). The generated SQL
/// is embedded in the migration; the integration drift test proves the database still agrees with the catalogue.
/// </summary>
internal static class AccountRoleConstraints
{
    /// <summary>The account_role check: one of the catalogue's codes.</summary>
    public static string RoleListSql() => "account_role IN (" + string.Join(", ", AccountRoles.All.Select(role => Quote(role.Code))) + ")";

    /// <summary>R-PP-06: per role, the key columns the role does not use must be NULL.</summary>
    public static string KeysByRoleSql() => string.Join(" OR ", AccountRoles.All.Select(role =>
    {
        List<string> terms = ["account_role = " + Quote(role.Code)];
        foreach ((MappingKeys key, string column) in KeyColumns)
        {
            if (!role.Uses(key))
            {
                terms.Add(column + " IS NULL");
            }
        }

        return "(" + string.Join(" AND ", terms) + ")";
    }));

    /// <summary>R-PP-04: the stored expression of expects_control, true for the roles that must map to a control account.</summary>
    public static string ExpectsControlSql() =>
        "account_role IN (" + string.Join(", ", AccountRoles.All.Where(role => role.ExpectsControlAccount).Select(role => Quote(role.Code))) + ")";

    /// <summary>The key columns in catalogue order, shared by the generated SQL and the drift test.</summary>
    public static IReadOnlyList<(MappingKeys Key, string Column)> KeyColumns { get; } =
    [
        (MappingKeys.ItemPostingGroup, "item_posting_group"),
        (MappingKeys.PartyPostingGroup, "party_posting_group"),
        (MappingKeys.ShipToScope, "ship_to_scope"),
        (MappingKeys.TaxCode, "tax_code_id"),
        (MappingKeys.ReasonCode, "reason_code"),
    ];

    private static string Quote(string code) => "'" + code.Replace("'", "''", StringComparison.Ordinal) + "'";
}
