using Themis.Kernel.Persistence.Privileges;

namespace Themis.Kernel.Tests;

/// <summary>The grant helper renders exactly the privileges a migration declares (brief, Grant hygiene; R-LED-06).</summary>
[Trait("Category", "Unit")]
public sealed class GrantSqlTests
{
    [Fact]
    public void R_LED_06_append_only_grant_renders_select_and_insert_only()
    {
        TableGrant grant = TableGrant.AppendOnly("kernel", "journal_entry", "themis_kernel");

        Assert.Equal(["GRANT SELECT, INSERT ON \"kernel\".\"journal_entry\" TO \"themis_kernel\";"], GrantSql.Statements(grant));
    }

    [Fact]
    public void Mutable_grant_never_includes_delete()
    {
        TableGrant grant = TableGrant.Mutable("kernel", "item", "themis_kernel");

        Assert.Equal(["GRANT SELECT, INSERT, UPDATE ON \"kernel\".\"item\" TO \"themis_kernel\";"], GrantSql.Statements(grant));
    }

    [Fact]
    public void Column_level_update_is_rendered_for_the_named_columns_only()
    {
        TableGrant grant = new("kernel", "outbox_event", "themis_kernel",
            new HashSet<TablePrivilege> { TablePrivilege.Select, TablePrivilege.Insert, TablePrivilege.Update },
            UpdateColumns: ["published_at", "attempts"]);

        Assert.Equal(
            [
                "GRANT SELECT, INSERT ON \"kernel\".\"outbox_event\" TO \"themis_kernel\";",
                "GRANT UPDATE (\"published_at\", \"attempts\") ON \"kernel\".\"outbox_event\" TO \"themis_kernel\";",
            ],
            GrantSql.Statements(grant));
    }

    [Fact]
    public void Delete_is_rendered_only_when_declared()
    {
        TableGrant grant = new("kernel", "idempotency_record", "themis_kernel",
            new HashSet<TablePrivilege> { TablePrivilege.Select, TablePrivilege.Insert, TablePrivilege.Delete });

        Assert.Equal(["GRANT SELECT, INSERT, DELETE ON \"kernel\".\"idempotency_record\" TO \"themis_kernel\";"], GrantSql.Statements(grant));
    }

    [Theory]
    [InlineData("Kernel")]
    [InlineData("journal entry")]
    [InlineData("x; DROP TABLE y")]
    [InlineData("")]
    public void Identifiers_outside_snake_case_are_rejected(string identifier)
    {
        Assert.Throws<ArgumentException>(() => TableGrant.AppendOnly("kernel", identifier, "themis_kernel"));
    }

    [Fact]
    public void The_declared_matrix_is_empty_until_the_first_migration()
    {
        Assert.Empty(KernelPrivilegeMatrix.Declared);
    }
}
