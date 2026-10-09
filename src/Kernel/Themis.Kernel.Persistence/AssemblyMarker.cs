namespace Themis.Kernel.Persistence;

/// <summary>
/// Anchor type for the kernel capability "Persistence": the one kernel data context, unit of work, the shared
/// connection and transaction, role switching (R-DB-01) and the grant helper every migration uses (Privileges/).
/// Kernel capabilities reach each other only through Themis.Kernel.Contracts (ARCH-08); no public types (ARCH-07).
/// </summary>
internal static class AssemblyMarker
{
}
