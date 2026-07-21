using System.Runtime.CompilerServices;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;

namespace Prosody.Tests.TestHelpers;

/// <summary>
/// Configures the process-wide W3C trace-context propagator before any test runs. The Prosody client
/// relies on the host to configure OpenTelemetry; the default propagator is otherwise a no-op that
/// injects nothing. Running this as a module initializer guarantees it executes before the client's
/// <c>TracePropagation</c> captures the default propagator, mirroring a properly configured host so
/// carrier injection is observable in-process.
/// </summary>
internal static class TracingModuleInitializer
{
    [ModuleInitializer]
    internal static void Initialize() =>
        Sdk.SetDefaultTextMapPropagator(
            new CompositeTextMapPropagator([new TraceContextPropagator(), new BaggagePropagator()])
        );
}
