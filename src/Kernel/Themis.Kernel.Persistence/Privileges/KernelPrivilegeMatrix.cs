namespace Themis.Kernel.Persistence.Privileges;

/// <summary>
/// The declared privilege matrix for the kernel and reporting schemas: one entry per table and role. Each migration
/// that creates a table adds its entry here and emits <see cref="GrantSql.Statements"/> for it; the I-14 integration
/// test fails on any privilege in the database that is not declared here, and on any declared one that is missing.
/// The rules the entries follow: ledger and audit tables are append-only (R-LED-06, R-AUD-01); DELETE exists only on
/// kernel.idempotency_record (table-level; the expiry condition belongs to the purge code); UPDATE on
/// kernel.outbox_event covers published_at and attempts only; themis_reporting holds SELECT on reporting views and
/// nothing in kernel.
/// </summary>
internal static class KernelPrivilegeMatrix
{
    private const string Kernel = "kernel";
    private const string KernelRole = "themis_kernel";

    public static IReadOnlyList<TableGrant> Declared { get; } =
    [
        new(Kernel, "idempotency_record", KernelRole, new HashSet<TablePrivilege> { TablePrivilege.Select, TablePrivilege.Insert, TablePrivilege.Delete }),
        TableGrant.AppendOnly(Kernel, "audit_event", KernelRole),
        new(Kernel, "outbox_event", KernelRole, new HashSet<TablePrivilege> { TablePrivilege.Select, TablePrivilege.Insert, TablePrivilege.Update }, ["published_at", "attempts"]),

        // Organisation master data (milestone 1 step 3): SELECT, INSERT, UPDATE; never DELETE (R-MD-03).
        TableGrant.Mutable(Kernel, "installation", KernelRole),
        TableGrant.Mutable(Kernel, "party", KernelRole),
        TableGrant.Mutable(Kernel, "chart_of_accounts", KernelRole),
        TableGrant.Mutable(Kernel, "company", KernelRole),
        TableGrant.Mutable(Kernel, "site", KernelRole),
        TableGrant.Mutable(Kernel, "warehouse", KernelRole),
        TableGrant.Mutable(Kernel, "fiscal_year", KernelRole),
        TableGrant.Mutable(Kernel, "fiscal_period", KernelRole),
    ];

    /// <summary>The tables the organisation migration creates, in dependency order.</summary>
    public static IReadOnlyList<string> OrganisationTables { get; } =
        ["installation", "party", "chart_of_accounts", "company", "site", "warehouse", "fiscal_year", "fiscal_period"];

    /// <summary>The grant statements a migration emits for the tables it creates.</summary>
    public static IEnumerable<string> GrantsFor(params string[] tables) =>
        Declared.Where(grant => tables.Contains(grant.Table, StringComparer.Ordinal)).SelectMany(GrantSql.Statements);
}
