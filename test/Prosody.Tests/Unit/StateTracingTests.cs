using System.Diagnostics;
using System.Text;
using Prosody.State;
using Prosody.Tests.TestHelpers;
using Native = Prosody.Native;

namespace Prosody.Tests.Unit;

/// <summary>
/// Unit tests for keyed-state trace propagation, the in-process proxy for the collector-observed
/// trace topology: each state op and each scan-chunk pull injects a carrier whose W3C
/// <c>traceparent</c> references the active handler activity (so the core span parents to the handler
/// span), and state ops open no additional .NET activities (so there are no per-op/per-chunk/binding
/// client spans). The full core-span topology (span counts, <c>map.key</c>/i64 conventions,
/// roots/parents/links) is collector-delegated and not observable
/// in-process. Runs sequentially because <see cref="ActivityListener"/> is process-global.
/// </summary>
[Collection(ActivityListenerIsolationCollection.Name)]
public sealed class StateTracingTests : IDisposable
{
    private readonly List<Activity> _started = [];
    private readonly ActivityListener _listener;
    private readonly ActivitySource _source = new("Prosody");

    public StateTracingTests()
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "Prosody",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStarted = activity => _started.Add(activity),
        };
        ActivitySource.AddActivityListener(_listener);
    }

    public void Dispose()
    {
        _listener.Dispose();
        _source.Dispose();
    }

    [Fact]
    public async Task StateOp_InjectsCarrier_ParentedToActiveActivity()
    {
        var handle = new FakeValueStateHandle();
        var state = new ValueState<int>(handle, TestJson.TypeInfo<int>());

        using var activity = _source.StartActivity("on_message", ActivityKind.Consumer);
        Assert.NotNull(activity);

        await state.GetAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(handle.LastCarrier);
        Assert.True(handle.LastCarrier.TryGetValue("traceparent", out var traceparent));
        Assert.Contains(activity.TraceId.ToHexString(), traceparent, StringComparison.Ordinal);
        Assert.Contains(activity.SpanId.ToHexString(), traceparent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Scan_InjectsCarrierPerPull_NoExtraActivities()
    {
        Native.StateScanItem[] Chunk(params string[] items) =>
            [
                .. items.Select(item =>
                    (Native.StateScanItem)new Native.StateScanItem.DequeJson(Encoding.UTF8.GetBytes(item))
                ),
            ];

        var cursor = new FakeStateCursor(Chunk("a", "b"));
        var sequence = new StateScanSequence<string>(
            cursor,
            item => Encoding.UTF8.GetString(((Native.StateScanItem.DequeJson)item).Bytes),
            CancellationToken.None
        );

        using var activity = _source.StartActivity("on_message", ActivityKind.Consumer);
        Assert.NotNull(activity);

        var drained = new List<string>();
        await foreach (var value in sequence)
        {
            drained.Add(value);
        }

        Assert.NotNull(cursor.LastCarrier);
        Assert.True(cursor.LastCarrier.TryGetValue("traceparent", out var traceparent));
        Assert.Multiple(
            () => Assert.Equal(["a", "b"], drained),
            () => Assert.Contains(activity.TraceId.ToHexString(), traceparent, StringComparison.Ordinal),
            () => Assert.Contains(activity.SpanId.ToHexString(), traceparent, StringComparison.Ordinal),
            // Only the handler-proxy span exists: the scan opens no per-chunk or binding .NET spans.
            () => Assert.Equal([activity], _started)
        );
    }
}
