using System.Diagnostics;
using Prosody.Errors;
using Prosody.Infrastructure;
using Prosody.Messaging;
using Prosody.Tests.TestHelpers;
using static Prosody.Tests.TestHelpers.TestDefaults;

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
    public async Task OnMessage_CreatesActivityNamed_OnMessage()
    {
        var handler = new LambdaHandler(onMessage: (_, _, _) => Task.CompletedTask);
        var bridge = new EventHandlerBridge(handler);

        await bridge.HandleMessageAsync(null!, null!, NeverCancel, EmptyCarrier);

        var activity = Assert.Single(_activities);
        Assert.Equal("OnMessage", activity.DisplayName);
        Assert.Equal(ActivityKind.Consumer, activity.Kind);
    }

    [Fact]
    public async Task OnTimer_CreatesActivityNamed_OnTimer()
    {
        var handler = new LambdaHandler(onTimer: (_, _, _) => Task.CompletedTask);
        var bridge = new EventHandlerBridge(handler);

        await bridge.HandleTimerAsync(null!, null!, NeverCancel, EmptyCarrier);

        var activity = Assert.Single(_activities);
        Assert.Equal("OnTimer", activity.DisplayName);
        Assert.Equal(ActivityKind.Consumer, activity.Kind);
    }

    [Fact]
    public async Task OnMessage_LeavesStatusUnset_OnSuccess()
    {
        var handler = new LambdaHandler(onMessage: (_, _, _) => Task.CompletedTask);
        var bridge = new EventHandlerBridge(handler);

        await bridge.HandleMessageAsync(null!, null!, NeverCancel, EmptyCarrier);

        var activity = Assert.Single(_activities);
        Assert.Equal(ActivityStatusCode.Unset, activity.Status);
        Assert.Empty(activity.Events);
    }

    [Fact]
    public async Task OnMessage_SetsStatusToError_OnTransientException()
    {
        var handler = new LambdaHandler(onMessage: (_, _, _) => throw new InvalidOperationException("boom"));
        var bridge = new EventHandlerBridge(handler);

        await bridge.HandleMessageAsync(null!, null!, NeverCancel, EmptyCarrier);

        var activity = Assert.Single(_activities);
        Assert.Equal(ActivityStatusCode.Error, activity.Status);
        Assert.Equal("boom", activity.StatusDescription);
    }

    [Fact]
    public async Task OnMessage_SetsStatusToError_OnPermanentException()
    {
        var handler = new LambdaHandler(onMessage: (_, _, _) => throw new PermanentException("nope"));
        var bridge = new EventHandlerBridge(handler);

        await bridge.HandleMessageAsync(null!, null!, NeverCancel, EmptyCarrier);

        var activity = Assert.Single(_activities);
        Assert.Equal(ActivityStatusCode.Error, activity.Status);
        Assert.Equal("nope", activity.StatusDescription);
    }

    [Fact]
    public async Task OnMessage_AddsExceptionEvent_WithSemanticTags()
    {
        var thrown = new InvalidOperationException("boom");
        var handler = new LambdaHandler(onMessage: (_, _, _) => throw thrown);
        var bridge = new EventHandlerBridge(handler);

        await bridge.HandleMessageAsync(null!, null!, NeverCancel, EmptyCarrier);

        var activity = Assert.Single(_activities);
        var exceptionEvent = Assert.Single(activity.Events, e => e.Name == "exception");
        var tags = exceptionEvent.Tags.ToDictionary(t => t.Key, t => t.Value);
        Assert.Equal(typeof(InvalidOperationException).FullName, tags["exception.type"]);
        Assert.Equal("boom", tags["exception.message"]);
        Assert.Equal(thrown.ToString(), tags["exception.stacktrace"]);
    }

    [Fact]
    public async Task OnMessage_LeavesStatusUnset_OnOperationCanceledException()
    {
        var handler = new LambdaHandler(onMessage: (_, _, _) => throw new OperationCanceledException("shutdown"));
        var bridge = new EventHandlerBridge(handler);

        await bridge.HandleMessageAsync(null!, null!, NeverCancel, EmptyCarrier);

        var activity = Assert.Single(_activities);
        Assert.Equal(ActivityStatusCode.Unset, activity.Status);
        Assert.Empty(activity.Events);
    }

    [Fact]
    public async Task OnTimer_SetsStatusToError_OnException()
    {
        var handler = new LambdaHandler(onTimer: (_, _, _) => throw new InvalidOperationException("timer boom"));
        var bridge = new EventHandlerBridge(handler);

        await bridge.HandleTimerAsync(null!, null!, NeverCancel, EmptyCarrier);

        var activity = Assert.Single(_activities);
        Assert.Equal(ActivityStatusCode.Error, activity.Status);
        Assert.Single(activity.Events, e => e.Name == "exception");
    }

    private sealed class LambdaHandler(
        Func<ProsodyContext, Message, CancellationToken, Task>? onMessage = null,
        Func<ProsodyContext, ProsodyTimer, CancellationToken, Task>? onTimer = null
    ) : IProsodyHandler
    {
        public Task OnMessageAsync(
            ProsodyContext prosodyContext,
            Message message,
            CancellationToken cancellationToken
        ) => onMessage?.Invoke(prosodyContext, message, cancellationToken) ?? Task.CompletedTask;

        public Task OnTimerAsync(
            ProsodyContext prosodyContext,
            ProsodyTimer timer,
            CancellationToken cancellationToken
        ) => onTimer?.Invoke(prosodyContext, timer, cancellationToken) ?? Task.CompletedTask;
    }
}
