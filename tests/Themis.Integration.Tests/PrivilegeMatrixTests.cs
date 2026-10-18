using Npgsql;
using Themis.Kernel.Persistence.Privileges;

namespace Themis.Integration.Tests;

/// <summary>
/// The catalogue test of the brief (Grant hygiene, I-14): every table privilege held by a Themis role in the kernel,
/// reporting and fixture schemas must be declared in <see cref="KernelPrivilegeMatrix"/> (or, for the test-only
/// fixture table, in <see cref="FixtureSchema"/>), and every declared privilege must exist. A new ledger table can
/// therefore never silently receive UPDATE.
/// </summary>
[Collection(PostgresTestGroup.Name)]
[Trait("Category", "Integration")]
public sealed class PrivilegeMatrixTests
{
    private static readonly string[] Schemas = ["kernel", "reporting", "fixture"];
    private static readonly string[] Roles = ["themis_app", "themis_kernel", "themis_fixture", "themis_reporting"];

    private readonly PostgresFixture _db;

    public PrivilegeMatrixTests(PostgresFixture db)
    {
        _db = db;
    }

    [Fact]
    public async Task I_14_actual_privileges_equal_the_declared_matrix()
    {
        HashSet<string> actual = await ActualPrivilegesAsync();
        HashSet<string> declared = KernelPrivilegeMatrix.Declared.Concat(FixtureSchema.Declared).SelectMany(Flatten).ToHashSet(StringComparer.Ordinal);

        IEnumerable<string> undeclared = actual.Except(declared, StringComparer.Ordinal).Order(StringComparer.Ordinal);
        IEnumerable<string> missing = declared.Except(actual, StringComparer.Ordinal).Order(StringComparer.Ordinal);

        Assert.True(!undeclared.Any() && !missing.Any(),
            $"Privileges in the database but not declared:{Environment.NewLine}{string.Join(Environment.NewLine, undeclared)}" +
            $"{Environment.NewLine}Declared but missing in the database:{Environment.NewLine}{string.Join(Environment.NewLine, missing)}");
    }

    [Fact]
    public void The_declared_matrix_covers_the_tables_of_the_seven_migrations_and_the_kernel_role_never_deletes_a_document_or_changes_a_ledger_entry()
    {
        Assert.Equal(
            ["account", "audit_event", "chart_of_accounts", "company", "currency", "document", "document_line", "document_transition", "document_type", "fiscal_period", "fiscal_year", "idempotency_record", "installation", "inventory_adjustment", "inventory_entry", "item", "item_company", "item_cost_state", "item_posting_group", "journal_entry", "journal_line", "number_series", "outbox_event", "party", "permission", "posting_profile_rule", "role", "role_assignment", "role_permission", "site", "sod_conflict", "stock_balance", "unit_of_measure", "user_account", "warehouse"],
            KernelPrivilegeMatrix.Declared.Select(grant => grant.Table).Order(StringComparer.Ordinal));
        Assert.All(KernelPrivilegeMatrix.Declared, grant => Assert.Equal("themis_kernel", grant.Role));
        // I-14 and R-DOC-06: the document tables are never deleted from; the registry is read-only for the kernel role (D-40).
        Assert.All(KernelPrivilegeMatrix.Declared.Where(grant => KernelPrivilegeMatrix.DocumentTables.Contains(grant.Table)), grant => Assert.DoesNotContain(TablePrivilege.Delete, grant.Privileges));
        Assert.All(KernelPrivilegeMatrix.Declared.Where(grant => grant.Table is "document_type" or "document_transition"), grant => Assert.Equal([TablePrivilege.Select], grant.Privileges.Order()));
        // I-11, R-LED-01, R-LED-06: the ledger tables are insert-only for the kernel role; the caches are updated in place (Specification, Caches).
        Assert.All(KernelPrivilegeMatrix.Declared.Where(grant => grant.Table is "journal_entry" or "journal_line" or "inventory_entry"), grant => Assert.Equal([TablePrivilege.Select, TablePrivilege.Insert], grant.Privileges.Order()));
        Assert.All(KernelPrivilegeMatrix.Declared.Where(grant => grant.Table is "item_cost_state" or "stock_balance"), grant => Assert.DoesNotContain(TablePrivilege.Delete, grant.Privileges));
    }

    [Fact]
    public async Task R_DB_01_login_role_holds_no_table_privilege_anywhere()
    {
        HashSet<string> actual = await ActualPrivilegesAsync();

        Assert.DoesNotContain(actual, privilege => privilege.StartsWith("themis_app ", StringComparison.Ordinal));
    }

    /// <summary>One line per role, table and privilege; column-level UPDATE as "UPDATE(column)".</summary>
    private static IEnumerable<string> Flatten(TableGrant grant)
    {
        foreach (TablePrivilege privilege in grant.Privileges.Order())
        {
            if (privilege == TablePrivilege.Update && grant.UpdateColumns is { Count: > 0 } columns)
            {
                foreach (string column in columns)
                {
                    yield return $"{grant.Role} {grant.Schema}.{grant.Table} UPDATE({column})";
                }
            }
            else
            {
                yield return $"{grant.Role} {grant.Schema}.{grant.Table} {privilege.ToString().ToUpperInvariant()}";
            }
        }
    }

    private async Task<HashSet<string>> ActualPrivilegesAsync()
    {
        await using NpgsqlConnection connection = await PostgresFixture.OpenAsync(_db.SuperuserConnectionString, TestContext.Current.CancellationToken);
        await using NpgsqlCommand command = new(
            """
            SELECT grantee || ' ' || table_schema || '.' || table_name || ' ' || privilege_type
            FROM information_schema.role_table_grants
            WHERE table_schema = ANY(@schemas) AND grantee = ANY(@roles)
            UNION
            SELECT c.grantee || ' ' || c.table_schema || '.' || c.table_name || ' ' || c.privilege_type || '(' || c.column_name || ')'
            FROM information_schema.role_column_grants c
            WHERE c.table_schema = ANY(@schemas) AND c.grantee = ANY(@roles)
              AND NOT EXISTS (
                  SELECT 1 FROM information_schema.role_table_grants t
                  WHERE t.grantee = c.grantee AND t.table_schema = c.table_schema AND t.table_name = c.table_name AND t.privilege_type = c.privilege_type)
            """,
            connection);
        command.Parameters.AddWithValue("schemas", Schemas);
        command.Parameters.AddWithValue("roles", Roles);

        HashSet<string> result = new(StringComparer.Ordinal);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            result.Add(reader.GetString(0));
        }

        return result;
    }
}
