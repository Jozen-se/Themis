using System.Diagnostics;
using Themis.Kernel.Primitives;

namespace Themis.Kernel.Pipeline.Http;

/// <summary>Creates a correlation id when no request supplies one: the current W3C trace id, else a fresh GUID (D-25).</summary>
internal static class CorrelationIds
{
    public static CorrelationId Fresh() =>
        CorrelationId.TryFrom(Activity.Current?.TraceId.ToString(), out CorrelationId traced) ? traced : CorrelationId.From(Guid.NewGuid().ToString("N"));
}
