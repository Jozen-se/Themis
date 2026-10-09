namespace Themis.Kernel.Persistence.Privileges;

/// <summary>
/// The declared privilege matrix for the kernel and reporting schemas: one entry per table and role. Each migration
/// that creates a table adds its entry here and emits <see cref="GrantSql.Statements"/> for it; the I-14 integration
/// test fails on any privilege in the database that is not declared here, and on any declared one that is missing.
/// Empty until the first migration (milestone 1 step 2). The rules the entries must follow:
/// ledger and audit tables are append-only (R-LED-06, R-AUD-01); DELETE exists only on kernel.idempotency_record;
/// UPDATE on kernel.outbox_event covers published_at and attempts only; themis_reporting holds SELECT on reporting
/// views and nothing in kernel.
/// </summary>
internal static class KernelPrivilegeMatrix
{
    public static IReadOnlyList<TableGrant> Declared { get; } = [];
}
