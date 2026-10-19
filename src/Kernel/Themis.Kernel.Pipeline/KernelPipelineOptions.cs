namespace Themis.Kernel.Pipeline;

/// <summary>
/// Pipeline settings. The idempotency retention is the specification's default of 7 days (Phase 0 specification,
/// IdempotencyRecord.expires_at). The owner approved a temporary exception to P-11: it stays a constant until the
/// settings framework of review finding RR-12 exists, and moves there before Phase 0 is declared complete. A record
/// whose expiry has passed counts as absent and is removed when its key is used again (RR-42); the purge of all
/// expired records comes with the settings step.
/// </summary>
internal sealed class KernelPipelineOptions
{
    public const int MaxAttempts = 3;

    public TimeSpan IdempotencyRetention { get; set; } = TimeSpan.FromDays(7);
}
