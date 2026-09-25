using System.Diagnostics;
using System.Text.Json;
using Prosody.Errors;
using Prosody.Infrastructure;
using Prosody.Messaging;
using Prosody.Tests.TestHelpers;
using static Prosody.Tests.TestHelpers.BridgeTestSupport;

namespace Prosody.Tests.Unit;

/// <summary>
/// Unit tests asserting the OpenTelemetry <see cref="Activity"/> behavior of
/// <see cref="EventHandlerBridge"/>: span naming, error status, and exception events.
/// Run sequentially because <see cref="ActivityListener"/> is process-global — concurrent
/// tests would observe each other's activities.
/// </summary>
[Collection(ActivityListenerIsolationCollection.Name)]
public sealed class EventHandlerBridgeTracingTests : IDisposable
{
    private readonly List<Activity> _activities = [];
    private readonly ActivityListener _listener;

    public EventHandlerBridgeTracingTests()
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "Prosody",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => _activities.Add(activity),
        };
        ActivitySource.AddActivityListener(_listener);
    }

    public void Dispose() => _listener.Dispose();

    [Fact]
    public async Task OnMessage_CreatesActivityNamed_on_message()
    {
        var handler = new LambdaHandler<JsonElement>(onMessage: (_, _, _) => Task.CompletedTask);
        var bridge = new EventHandlerBridge<JsonElement>(handler, TestJson.Options);

        await HandleMessageAsync(bridge);

        Activity activity = Assert.Single(_activities);
        Assert.Equal("on_message", activity.DisplayName);
        Assert.Equal(ActivityKind.Consumer, activity.Kind);
    }

    [Fact]
    public async Task OnTimer_CreatesActivityNamed_on_timer()
    {
        var handler = new LambdaHandler<JsonElement>(onTimer: (_, _, _) => Task.CompletedTask);
        var bridge = new EventHandlerBridge<JsonElement>(handler, TestJson.Options);

        await HandleTimerAsync(bridge);

        Activity activity = Assert.Single(_activities);
        Assert.Equal("on_timer", activity.DisplayName);
        Assert.Equal(ActivityKind.Consumer, activity.Kind);
    }

    [Fact]
    public async Task OnMessage_LeavesStatusUnset_OnSuccess()
    {
        var handler = new LambdaHandler<JsonElement>(onMessage: (_, _, _) => Task.CompletedTask);
        var bridge = new EventHandlerBridge<JsonElement>(handler, TestJson.Options);

        await HandleMessageAsync(bridge);

        Activity activity = Assert.Single(_activities);
        Assert.Equal(ActivityStatusCode.Unset, activity.Status);
        Assert.Empty(activity.Events);
    }

    [Fact]
    public async Task OnMessage_SetsStatusToError_OnTransientException()
    {
        var handler = new LambdaHandler<JsonElement>(
            onMessage: (_, _, _) => throw new InvalidOperationException("boom")
        );
        var bridge = new EventHandlerBridge<JsonElement>(handler, TestJson.Options);

        await HandleMessageAsync(bridge);

        Activity activity = Assert.Single(_activities);
        Assert.Equal(ActivityStatusCode.Error, activity.Status);
        Assert.Equal("boom", activity.StatusDescription);
    }

    [Fact]
    public async Task OnMessage_SetsStatusToError_OnPermanentException()
    {
        var handler = new LambdaHandler<JsonElement>(onMessage: (_, _, _) => throw new PermanentException("nope"));
        var bridge = new EventHandlerBridge<JsonElement>(handler, TestJson.Options);

        await HandleMessageAsync(bridge);

        Activity activity = Assert.Single(_activities);
        Assert.Equal(ActivityStatusCode.Error, activity.Status);
        Assert.Equal("nope", activity.StatusDescription);
    }

    [Fact]
    public async Task OnMessage_AddsExceptionEvent_WithSemanticTags()
    {
        var thrown = new InvalidOperationException("boom");
        var handler = new LambdaHandler<JsonElement>(onMessage: (_, _, _) => throw thrown);
        var bridge = new EventHandlerBridge<JsonElement>(handler, TestJson.Options);

        await HandleMessageAsync(bridge);

        Activity activity = Assert.Single(_activities);
        ActivityEvent exceptionEvent = Assert.Single(activity.Events, e => e.Name == "exception");
        var tags = exceptionEvent.Tags.ToDictionary(t => t.Key, t => t.Value);
        Assert.Equal(typeof(InvalidOperationException).FullName, tags["exception.type"]);
        Assert.Equal("boom", tags["exception.message"]);
        Assert.Equal(thrown.ToString(), tags["exception.stacktrace"]);
    }

    [Fact]
    public async Task OnMessage_LeavesStatusUnset_OnOperationCanceledException()
    {
        var handler = new LambdaHandler<JsonElement>(
            onMessage: (_, _, _) => throw new OperationCanceledException("shutdown")
        );
        var bridge = new EventHandlerBridge<JsonElement>(handler, TestJson.Options);

        await HandleMessageAsync(bridge);

        Activity activity = Assert.Single(_activities);
        Assert.Equal(ActivityStatusCode.Unset, activity.Status);
        Assert.Empty(activity.Events);
    }

    [Fact]
    public async Task OnTimer_SetsStatusToError_OnException()
    {
        var handler = new LambdaHandler<JsonElement>(
            onTimer: (_, _, _) => throw new InvalidOperationException("timer boom")
        );
        var bridge = new EventHandlerBridge<JsonElement>(handler, TestJson.Options);
        await HandleTimerAsync(bridge);

        Activity activity = Assert.Single(_activities);
        Assert.Equal(ActivityStatusCode.Error, activity.Status);
        Assert.Single(activity.Events, e => e.Name == "exception");
    }
}
