using System.Diagnostics;
using BenchmarkDotNet.Attributes;
using Prosody.Infrastructure;

namespace Prosody.Benchmarks;

/// <summary>
/// Benchmarks for <see cref="TracePropagation"/> inject and extract operations.
/// These happen on every message send and receive, making them a critical hot path.
/// </summary>
[MemoryDiagnoser]
[JsonExporterAttribute.FullCompressed]
public class TracePropagationBenchmarks
{
    private Dictionary<string, string> _populatedCarrier = null!;
    private Dictionary<string, string> _emptyCarrier = null!;

    [GlobalSetup]
    public void Setup()
    {
        // Pre-populate a carrier with a sample W3C trace context headers
        _populatedCarrier = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["traceparent"] = "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
            ["tracestate"] = "congo=t61rcWkgMzE",
        };
        _emptyCarrier = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    [Benchmark]
    public Dictionary<string, string> Inject_NoActiveTrace()
    {
        TracePropagation.Inject(_emptyCarrier);
        return _emptyCarrier;
    }

    [Benchmark]
    public Dictionary<string, string> Inject_WithActiveTrace()
    {
        using Activity activity = new Activity("BenchmarkSend").Start();
        var carrier = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        TracePropagation.Inject(carrier);
        return carrier;
    }

    [Benchmark]
    public Activity? Extract_WithTraceHeaders()
    {
        var carrier = new Dictionary<string, string>(_populatedCarrier, StringComparer.OrdinalIgnoreCase);
        Activity? activity = TracePropagation.Extract(carrier);
        activity?.Dispose();
        return activity;
    }

    [Benchmark]
    public Activity? Extract_EmptyCarrier()
    {
        Activity? activity = TracePropagation.Extract(_emptyCarrier);
        activity?.Dispose();
        return activity;
    }
}
