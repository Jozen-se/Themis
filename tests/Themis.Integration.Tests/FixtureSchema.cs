using Themis.FixtureModule.Contracts;
using Themis.Kernel.Persistence.Privileges;

namespace Themis.Integration.Tests;

/// <summary>
/// The fixture module's table, created the way a module migration will create it: as themis_migrator, with the
/// grants of a declared privilege matrix rendered by the grant helper (I-14). The matrix is the one
/// <see cref="PrivilegeMatrixTests"/> adds to the kernel's declared set.
/// </summary>
internal static class FixtureSchema
{
    public static IReadOnlyList<TableGrant> Declared { get; } =
    [
        TableGrant.Mutable(FixtureContracts.SchemaName, "counter", FixtureContracts.RoleName),
    ];

    public static string CreateSql =>
        $"""
        CREATE TABLE IF NOT EXISTS {FixtureContracts.SchemaName}.counter (
            id uuid PRIMARY KEY,
            company_id uuid NOT NULL,
            site_id uuid NULL,
            value bigint NOT NULL,
            version bigint NOT NULL,
            sample numeric(20, 4) NOT NULL,
            posted boolean NOT NULL DEFAULT false
        );
        {string.Join("\n", Declared.SelectMany(GrantSql.Statements))}
        """;
}
