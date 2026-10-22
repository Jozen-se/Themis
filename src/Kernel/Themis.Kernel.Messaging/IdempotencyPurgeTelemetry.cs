using System.Diagnostics.Metrics;

namespace Themis.Kernel.Messaging;

internal sealed class IdempotencyPurgeTelemetry : IDisposable
{
    public const string MeterName = "Themis.Kernel.Idempotency";
    public const string PurgedName = "themis.idempotency.purged";

    private readonly Meter _meter = new(MeterName);

    public IdempotencyPurgeTelemetry()
    {
        Purged = _meter.CreateCounter<long>(PurgedName, unit: "{record}", description: "Expired idempotency records removed by the purge (RR-42, D-58).");
    }

    public Counter<long> Purged { get; }

    public void Dispose() => _meter.Dispose();
}
