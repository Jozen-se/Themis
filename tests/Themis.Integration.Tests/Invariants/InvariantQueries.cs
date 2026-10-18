using System.Globalization;
using System.Text;
using Npgsql;

namespace Themis.Integration.Tests.Invariants;

/// <summary>
/// The invariant queries of <c>db/invariants</c> (step 11 brief): one SQL file per checkable invariant, named by its
/// ID, returning the violating rows. Written once and read here by the property-based runs and the negative tests,
/// and later by the production nightly check. The tests run them as the table owner, themis_migrator.
/// </summary>
internal static class InvariantQueries
{
    /// <summary>The invariants a query covers in milestone 1.</summary>
    public static IReadOnlyList<string> Covered { get; } = ["I-01", "I-02", "I-03", "I-04", "I-06", "I-08", "I-09", "I-15"];

    /// <summary>The invariants that are role and privilege statements, proven by the privilege matrix and role model tests (I-14 tests), not by a query.</summary>
    public static IReadOnlyList<string> HeldByPrivilegeTests { get; } = ["I-11", "I-14"];

    /// <summary>
    /// The invariants no query can check yet because their entities do not exist, each with the Foundation phase
    /// that brings them (Foundation Design, Phases). Listed, never faked as passing (step 11 brief).
    /// </summary>
    public static IReadOnlyList<(string Id, string Phase, string Needs)> NotYetApplicable { get; } =
    [
        ("I-05", "Phase 1, Purchasing and Sales", "reservations, which arrive with sales order stock allocation"),
        ("I-07", "Phase 2, Finance through a first financial close", "receivable open items, which arrive with customer invoices"),
        ("I-10", "Phase 1, Purchasing and Sales", "document links, which arrive with goods receipts against purchase orders and deliveries against sales orders"),
        ("I-12", "Phase 2, Finance through a first financial close", "open items and settlements, which arrive with customer and supplier payments"),
        ("I-13", "Phase 2, Finance through a first financial close", "ROUNDING lines, which arrive with documents in a transaction currency other than the company currency (multi-currency is outside milestone 1)"),
    ];

    public static string Directory { get; } = Path.Combine(AppContext.BaseDirectory, "db", "invariants");

    public static string PathOf(string id) => Path.Combine(Directory, id + ".sql");

    public static string Sql(string id) => File.ReadAllText(PathOf(id));

    /// <summary>Runs every covered query on the connection and returns the violations, one per violating row, rendered as the row's columns.</summary>
    public static async Task<IReadOnlyList<InvariantViolation>> ViolationsAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        List<InvariantViolation> violations = [];
        foreach (string id in Covered)
        {
            foreach (string row in await RowsAsync(connection, id, cancellationToken))
            {
                violations.Add(new InvariantViolation(id, row));
            }
        }

        return violations;
    }

    /// <summary>Runs every covered query as themis_migrator against the database of the connection string.</summary>
    public static async Task<IReadOnlyList<InvariantViolation>> ViolationsAsync(string migratorConnectionString, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await PostgresFixture.OpenAsync(migratorConnectionString, cancellationToken);
        return await ViolationsAsync(connection, cancellationToken);
    }

    /// <summary>The violating rows of one invariant's query, each rendered as "column=value" pairs.</summary>
    public static async Task<IReadOnlyList<string>> RowsAsync(NpgsqlConnection connection, string id, CancellationToken cancellationToken)
    {
        List<string> rows = [];
        await using NpgsqlCommand command = new(Sql(id), connection);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            StringBuilder row = new();
            for (int i = 0; i < reader.FieldCount; i++)
            {
                if (i > 0)
                {
                    row.Append("; ");
                }

                row.Append(reader.GetName(i)).Append('=').Append(reader.IsDBNull(i) ? "null" : Convert.ToString(reader.GetValue(i), CultureInfo.InvariantCulture));
            }

            rows.Add(row.ToString());
        }

        return rows;
    }

    public static string Describe(IEnumerable<InvariantViolation> violations) =>
        string.Join(Environment.NewLine, violations.Select(violation => $"{violation.Invariant}: {violation.Row}"));
}

internal sealed record InvariantViolation(string Invariant, string Row);
