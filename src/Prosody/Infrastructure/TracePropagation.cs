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
    private static readonly ActivitySource Source = new("Prosody");

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
    /// Extracts trace context and baggage from a carrier, restoring them as current.
    /// Returns an Activity that should be disposed when the operation completes.
    /// </summary>
    public static Activity? Extract(Dictionary<string, string> carrier, string activityName)
    {
        PropagationContext context = Propagator.Extract(
            default,
            carrier,
            static (c, k) => c.TryGetValue(k, out string? v) ? [v] : []
        );

        Baggage.Current = context.Baggage;

        return Source.StartActivity(activityName, ActivityKind.Consumer, context.ActivityContext);
    }
}
