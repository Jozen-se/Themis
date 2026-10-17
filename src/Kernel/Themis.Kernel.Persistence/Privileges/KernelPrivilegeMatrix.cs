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

        // Security entities (milestone 1 step 4): SELECT, INSERT, UPDATE; never DELETE. Users are Disabled and
        // assignments end by valid_to (step 4 brief; R-MD-03).
        TableGrant.Mutable(Kernel, "user_account", KernelRole),
        TableGrant.Mutable(Kernel, "permission", KernelRole),
        TableGrant.Mutable(Kernel, "role", KernelRole),
        TableGrant.Mutable(Kernel, "role_permission", KernelRole),
        TableGrant.Mutable(Kernel, "role_assignment", KernelRole),
        TableGrant.Mutable(Kernel, "sod_conflict", KernelRole),

        // Master data (milestone 1 step 5): reference lists, posting groups, items and their company rows; never DELETE (R-MD-03).
        TableGrant.Mutable(Kernel, "unit_of_measure", KernelRole),
        TableGrant.Mutable(Kernel, "currency", KernelRole),
        TableGrant.Mutable(Kernel, "item_posting_group", KernelRole),
        TableGrant.Mutable(Kernel, "item", KernelRole),
        TableGrant.Mutable(Kernel, "item_company", KernelRole),

        // General ledger core (milestone 1 step 6): accounts and posting profile rules; never DELETE (R-MD-03).
        TableGrant.Mutable(Kernel, "account", KernelRole),
        TableGrant.Mutable(Kernel, "posting_profile_rule", KernelRole),

        // Document framework (milestone 1 step 7): the registry is system-owned and written by the migrator only, so
        // the kernel role reads it (D-40); documents, lines, extensions and series are never deleted (R-DOC-06, R-MD-03).
        TableGrant.ReadOnly(Kernel, "document_type", KernelRole),
        TableGrant.ReadOnly(Kernel, "document_transition", KernelRole),
        TableGrant.Mutable(Kernel, "document", KernelRole),
        TableGrant.Mutable(Kernel, "document_line", KernelRole),
        TableGrant.Mutable(Kernel, "inventory_adjustment", KernelRole),
        TableGrant.Mutable(Kernel, "number_series", KernelRole),
    ];

    /// <summary>The tables the organisation migration creates, in dependency order.</summary>
    public static IReadOnlyList<string> OrganisationTables { get; } =
        ["installation", "party", "chart_of_accounts", "company", "site", "warehouse", "fiscal_year", "fiscal_period"];

    /// <summary>The tables the identity migration creates, in dependency order.</summary>
    public static IReadOnlyList<string> IdentityTables { get; } =
        ["user_account", "permission", "role", "role_permission", "role_assignment", "sod_conflict"];

    /// <summary>The tables the master data migration creates, in dependency order.</summary>
    public static IReadOnlyList<string> MasterDataTables { get; } =
        ["unit_of_measure", "currency", "item_posting_group", "item", "item_company"];

    /// <summary>The tables the general ledger migration creates, in dependency order.</summary>
    public static IReadOnlyList<string> GeneralLedgerTables { get; } = ["account", "posting_profile_rule"];

    /// <summary>The tables the document framework migration creates, in dependency order.</summary>
    public static IReadOnlyList<string> DocumentTables { get; } =
        ["document_type", "document_transition", "document", "document_line", "inventory_adjustment", "number_series"];

    /// <summary>The grant statements a migration emits for the tables it creates.</summary>
    public static IEnumerable<string> GrantsFor(params string[] tables) =>
        Declared.Where(grant => tables.Contains(grant.Table, StringComparer.Ordinal)).SelectMany(GrantSql.Statements);
}
