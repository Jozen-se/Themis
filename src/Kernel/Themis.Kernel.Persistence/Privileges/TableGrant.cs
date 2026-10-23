using System.Text.RegularExpressions;

namespace Themis.Kernel.Persistence.Privileges;

/// <summary>
/// The privileges one role holds on one table. Every migration that creates a table declares its grants through
/// this type and <see cref="GrantSql"/>, and the integration test for I-14 compares the database with the declared
/// set, so a table can never silently receive more than it was given (brief, Grant hygiene).
/// </summary>
/// <param name="Schema">Schema name, snake_case.</param>
/// <param name="Table">Table name, snake_case.</param>
/// <param name="Role">The role receiving the grant, for example themis_kernel.</param>
/// <param name="Privileges">The privileges granted. UPDATE with <paramref name="UpdateColumns"/> is column-level.</param>
/// <param name="UpdateColumns">When set, UPDATE is granted on these columns only (the outbox delivery columns).</param>
internal sealed record TableGrant(
    string Schema,
    string Table,
    string Role,
    IReadOnlySet<TablePrivilege> Privileges,
    IReadOnlyList<string>? UpdateColumns = null)
{
    private static readonly Regex Identifier = new("^[a-z_][a-z0-9_]{0,62}\\z", RegexOptions.CultureInvariant);

    public string Schema { get; } = Validated(Schema);

    public string Table { get; } = Validated(Table);

    public string Role { get; } = Validated(Role);

    public IReadOnlyList<string>? UpdateColumns { get; } = UpdateColumns?.Select(Validated).ToList();

    /// <summary>Ledger and audit tables: SELECT and INSERT only (R-LED-01, R-LED-06, R-AUD-01).</summary>
    public static TableGrant AppendOnly(string schema, string table, string role) =>
        new(schema, table, role, new HashSet<TablePrivilege> { TablePrivilege.Select, TablePrivilege.Insert });

    /// <summary>System catalogues written by the migrator only, such as the document type registry (D-40): SELECT.</summary>
    public static TableGrant ReadOnly(string schema, string table, string role) =>
        new(schema, table, role, new HashSet<TablePrivilege> { TablePrivilege.Select });

    /// <summary>Master data, documents, caches and settings: SELECT, INSERT and UPDATE; never DELETE (R-MD-03, R-DOC-06).</summary>
    public static TableGrant Mutable(string schema, string table, string role) =>
        new(schema, table, role, new HashSet<TablePrivilege> { TablePrivilege.Select, TablePrivilege.Insert, TablePrivilege.Update });

    internal static string Validated(string identifier) =>
        Identifier.IsMatch(identifier)
            ? identifier
            : throw new ArgumentException($"'{identifier}' is not a valid snake_case identifier.", nameof(identifier));
}
