namespace Themis.Kernel.Pipeline;

/// <summary>
/// Anchor type for the kernel capability "Pipeline": command and query dispatch with validation, authorisation (R-SEC-01), idempotency (R-API-01, R-API-04), one transaction with role switching (D-41), audit events (R-AUD-01) and telemetry (D-25).
/// Kernel capabilities reach each other only through Themis.Kernel.Contracts (ARCH-08); no public types (ARCH-07).
/// </summary>
internal static class AssemblyMarker
{
}
