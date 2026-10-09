namespace Themis.Kernel.Pipeline;

/// <summary>
/// Pipeline settings. The idempotency retention is the specification's default of 7 days (Phase 0 specification,
/// IdempotencyRecord.expires_at); it moves to the settings table when that table exists (P-11), so the host
/// configures it here only until then. Purging expired records is later work; the pipeline only stamps the expiry.
/// </summary>
internal sealed class KernelPipelineOptions
{
    public const int MaxAttempts = 3;

    public TimeSpan IdempotencyRetention { get; set; } = TimeSpan.FromDays(7);
}
