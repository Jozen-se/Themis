using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Themis.Kernel.Pipeline.Telemetry;

/// <summary>
/// Traces and metrics of the command pipeline (D-25): one activity per command, a counter of commands by outcome and
/// a counter of whole-command retries by SQLSTATE (D-41). The host subscribes its exporters to
/// <see cref="SourceName"/>.
/// </summary>
internal sealed class PipelineTelemetry : IDisposable
{
    public const string SourceName = "Themis.Kernel.Pipeline";

    private readonly Meter _meter = new(SourceName);

    public PipelineTelemetry()
    {
        Commands = _meter.CreateCounter<long>("themis.commands", unit: "{command}", description: "Commands dispatched, by command name and outcome.");
        Retries = _meter.CreateCounter<long>("themis.command.retries", unit: "{retry}", description: "Whole-command retries after SQLSTATE 40001 or 40P01 (D-41).");
    }

    public ActivitySource Source { get; } = new(SourceName);

    public Counter<long> Commands { get; }

    public Counter<long> Retries { get; }

    public void Dispose()
    {
        _meter.Dispose();
        Source.Dispose();
    }
}
