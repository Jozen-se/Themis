using System.Diagnostics.Metrics;

namespace Themis.Kernel.Messaging;

/// <summary>
/// The relay's metrics (step 9 brief; D-25): a counter of published events, incremented after a pass commits, and a
/// gauge of the age of the oldest unpublished event in whole seconds (0 when none), recorded at the start of every pass
/// before claiming, so a failing pass still updates it. The host subscribes its exporters to <see cref="MeterName"/>.
/// </summary>
internal sealed class OutboxRelayTelemetry : IDisposable
{
    public const string MeterName = "Themis.Kernel.Messaging";
    public const string PublishedName = "themis.outbox.published";
    public const string OldestPendingAgeName = "themis.outbox.oldest_pending_age";

    private readonly Meter _meter = new(MeterName);

    public OutboxRelayTelemetry()
    {
        Published = _meter.CreateCounter<long>(PublishedName, unit: "{event}", description: "Outbox events marked published by the relay (D-53).");
        OldestPendingAge = _meter.CreateGauge<long>(OldestPendingAgeName, unit: "s", description: "Age of the oldest unpublished outbox event at the start of the last pass; 0 when none.");
    }

    public Counter<long> Published { get; }

    public Gauge<long> OldestPendingAge { get; }

    public void Dispose() => _meter.Dispose();
}
