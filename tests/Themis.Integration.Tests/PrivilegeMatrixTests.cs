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
    public void The_declared_matrix_covers_the_pipeline_organisation_identity_and_master_data_tables_of_the_first_four_migrations()
    {
        Assert.Equal(
            ["audit_event", "chart_of_accounts", "company", "currency", "fiscal_period", "fiscal_year", "idempotency_record", "installation", "item", "item_company", "item_posting_group", "outbox_event", "party", "permission", "role", "role_assignment", "role_permission", "site", "sod_conflict", "unit_of_measure", "user_account", "warehouse"],
            KernelPrivilegeMatrix.Declared.Select(grant => grant.Table).Order(StringComparer.Ordinal));
        Assert.All(KernelPrivilegeMatrix.Declared, grant => Assert.Equal("themis_kernel", grant.Role));
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
