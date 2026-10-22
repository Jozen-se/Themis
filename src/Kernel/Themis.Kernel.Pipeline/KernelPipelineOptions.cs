namespace Themis.Kernel.Pipeline;

/// <summary>
/// Pipeline settings that are code, not configuration: the retry bound of D-41. The idempotency retention is the
/// setting idempotency.retention_days since milestone 2 step 2 (D-58; D-45 ended), read inside the command's
/// transaction and stamped on the record at insert; a record whose expiry has passed counts as absent and is removed
/// when its key is used again (RR-42), and the host's purge removes the rest (docs/development.md, Settings).
/// </summary>
internal sealed class KernelPipelineOptions
{
    public const int MaxAttempts = 3;
}
