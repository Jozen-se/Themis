using System.Globalization;

namespace Themis.Kernel.Persistence.Privileges;

/// <summary>
/// Renders the GRANT statements for a <see cref="TableGrant"/>. Migrations emit exactly these statements, and the
/// I-14 integration test proves that the rendered grants produce the declared privileges and nothing else.
/// </summary>
internal static class GrantSql
{
    public static IReadOnlyList<string> Statements(TableGrant grant)
    {
        List<string> statements = [];
        string table = $"\"{grant.Schema}\".\"{grant.Table}\"";

        List<string> tableLevel = [];
        if (grant.Privileges.Contains(TablePrivilege.Select))
        {
            tableLevel.Add("SELECT");
        }

        if (grant.Privileges.Contains(TablePrivilege.Insert))
        {
            tableLevel.Add("INSERT");
        }

        if (grant.Privileges.Contains(TablePrivilege.Update) && grant.UpdateColumns is null)
        {
            tableLevel.Add("UPDATE");
        }

        if (grant.Privileges.Contains(TablePrivilege.Delete))
        {
            tableLevel.Add("DELETE");
        }

        if (tableLevel.Count > 0)
        {
            statements.Add(string.Create(CultureInfo.InvariantCulture, $"GRANT {string.Join(", ", tableLevel)} ON {table} TO \"{grant.Role}\";"));
        }

        if (grant.Privileges.Contains(TablePrivilege.Update) && grant.UpdateColumns is { Count: > 0 } columns)
        {
            string columnList = string.Join(", ", columns.Select(column => $"\"{column}\""));
            statements.Add(string.Create(CultureInfo.InvariantCulture, $"GRANT UPDATE ({columnList}) ON {table} TO \"{grant.Role}\";"));
        }

        return statements;
    }
}
