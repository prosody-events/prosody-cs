using System.Diagnostics;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;

namespace Prosody.Infrastructure;

/// <summary>
/// Provides OpenTelemetry context propagation for distributed tracing.
/// </summary>
internal static class TracePropagation
{
    private static readonly TextMapPropagator Propagator = Propagators.DefaultTextMapPropagator;

    /// <summary>
    /// Injects the current trace context and baggage into a carrier.
    /// </summary>
    public static void Inject(Dictionary<string, string> carrier)
    {
        Propagator.Inject(
            new PropagationContext(Activity.Current?.Context ?? default, Baggage.Current),
            carrier,
            static (c, k, v) => c[k] = v
        );
    }

    /// <summary>
    /// Extracts trace context and baggage from a carrier, restoring baggage as current.
    /// Returns the propagation context so the caller can use its own ActivitySource
    /// to start an activity.
    /// </summary>
    public static PropagationContext Extract(Dictionary<string, string> carrier)
    {
        PropagationContext context = Propagator.Extract(
            default,
            carrier,
            static (c, k) => c.TryGetValue(k, out string? v) ? [v] : []
        );

        Baggage.Current = context.Baggage;

        return context;
    }
}
