using OpenTelemetry;
using OpenTelemetry.Trace;
using Prosody.Tests.TestHelpers;

namespace Prosody.Tests.Integration;

/// <summary>
/// Integration tests for ProsodyClient.
/// </summary>
/// <remarks>
/// These tests require Kafka and Cassandra to be running locally.
/// Tests mirror the integration tests from prosody-js, prosody-py, and prosody-rb.
/// </remarks>
[Collection(IntegrationTestCollection.Name)]
public sealed class ProsodyClientTests(IntegrationTestFixture fixture) : IAsyncLifetime, IAsyncDisposable
{
    private readonly TracerProvider _tracerProvider = Sdk.CreateTracerProviderBuilder()
        .AddSource("prosody-cs-test")
        .Build();
    private readonly Tracer _tracer = TracerProvider.Default.GetTracer("prosody-cs-test");

    private string _topic = null!;
    private string _groupId = null!;
    private ProsodyClient _client = null!;

    public async Task InitializeAsync()
    {
        _topic = TopicGenerator.GenerateTopicName();
        _groupId = TopicGenerator.GenerateGroupId();

        await fixture.Admin.CreateTopicAsync(_topic, 4, 1);

        _client = new ProsodyClient(
            new ClientOptions
            {
                BootstrapServers = [IntegrationTestFixture.BootstrapServers],
                GroupId = _groupId,
                SourceSystem = "test-source",
                SubscribedTopics = [_topic],
                ProbePort = 0,
                Mode = ClientMode.Pipeline,
                CassandraNodes = [IntegrationTestFixture.CassandraNodes],
                CassandraKeyspace = IntegrationTestFixture.CassandraKeyspace,
            }
        );
    }

    public async Task DisposeAsync()
    {
        if (await _client.ConsumerStateAsync() == ConsumerState.Running)
        {
            await _client.UnsubscribeAsync();
        }
        _client.Dispose();

        try
        {
            await fixture.Admin.DeleteTopicAsync(_topic);
        }
        catch (InvalidOperationException)
        {
            // Topic may not exist or already be deleted
        }
        catch (TimeoutException)
        {
            // Kafka cluster may be slow during cleanup
        }

        _tracerProvider.Dispose();
    }

    /// <inheritdoc />
    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await DisposeAsync();
    }

    [Fact(Timeout = 30_000)]
    public async Task InitializesCorrectly()
    {
        using var span = _tracer.StartActiveSpan("test.initialize");

        Assert.NotNull(_client);
        Assert.Equal(ConsumerState.Configured, await _client.ConsumerStateAsync());
    }

    [Fact]
    public void ExposesSourceSystemIdentifier()
    {
        using var span = _tracer.StartActiveSpan("test.source_system");

        Assert.Equal("test-source", _client.SourceSystem);
    }

    [Fact(Timeout = 30_000)]
    public async Task SubscribesAndUnsubscribes()
    {
        using var span = _tracer.StartActiveSpan("test.subscribe_unsubscribe");

        var handler = new TestEventHandler();

        await _client.SubscribeAsync(handler);
        Assert.Equal(ConsumerState.Running, await _client.ConsumerStateAsync());

        await _client.UnsubscribeAsync();
        Assert.Equal(ConsumerState.Configured, await _client.ConsumerStateAsync());
    }

    [Fact(Timeout = 60_000)]
    public async Task SendsAndReceivesMessage()
    {
        using var span = _tracer.StartActiveSpan("test.send_receive");

        var messages = new MessageChannel<Message>();
        var handler = new TestEventHandler(
            onMessage: (_, msg, _) =>
            {
                messages.Send(msg);
                return Task.FromResult(HandlerResultCode.Success);
            }
        );

        await _client.SubscribeAsync(handler);

        var testPayload = new TestPayload { Content = "Hello, Kafka!" };
        await _client.SendAsync(_topic, "test-key", testPayload);

        var received = await messages.ReceiveAsync(IntegrationTestFixture.DefaultTimeout);

        Assert.Equal(_topic, received.Topic);
        Assert.Equal("test-key", received.Key);
        var payload = received.GetPayload<TestPayload>();
        Assert.Equal("Hello, Kafka!", payload.Content);
    }

    [Fact(Timeout = 60_000)]
    public async Task HandlesMultipleMessagesWithCorrectOrdering()
    {
        using var span = _tracer.StartActiveSpan("test.multiple_messages");

        var messages = new MessageChannel<Message>();
        var handler = new TestEventHandler(
            onMessage: (_, msg, _) =>
            {
                messages.Send(msg);
                return Task.FromResult(HandlerResultCode.Success);
            }
        );

        await _client.SubscribeAsync(handler);

        var messagesToSend = new[]
        {
            ("key1", new TestPayload { Content = "Message 1", Sequence = 1 }),
            ("key2", new TestPayload { Content = "Message 2", Sequence = 1 }),
            ("key1", new TestPayload { Content = "Message 3", Sequence = 2 }),
            ("key3", new TestPayload { Content = "Message 4", Sequence = 1 }),
            ("key2", new TestPayload { Content = "Message 5", Sequence = 2 }),
        };

        foreach (var (key, payload) in messagesToSend)
        {
            await _client.SendAsync(_topic, key, payload);
        }

        var received = await messages.ReceiveAsync(
            messagesToSend.Length,
            IntegrationTestFixture.DefaultTimeout
        );

        Assert.Equal(messagesToSend.Length, received.Count);

        // Group by key and verify ordering within each key
        var byKey = received.GroupBy(m => m.Key).ToDictionary(g => g.Key, g => g.ToList());

        foreach (var (_, msgs) in byKey)
        {
            var sequences = msgs.Select(m => m.GetPayload<TestPayload>().Sequence).ToList();
            var sorted = sequences.OrderBy(s => s).ToList();
            Assert.Equal(sorted, sequences);
        }

        Assert.All(received, m => Assert.Equal(_topic, m.Topic));
    }

    [Fact(Timeout = 60_000)]
    public async Task SupportsCancellationTokenInHandler()
    {
        using var span = _tracer.StartActiveSpan("test.cancellation_token");

        var processingStarted = new EventNotifier();
        var processingAborted = new EventNotifier();
        var wasAborted = false;

        var handler = new TestEventHandler(
            onMessage: async (_, _, ct) =>
            {
                processingStarted.Signal();

                try
                {
                    // Wait indefinitely until cancelled
                    await Task.Delay(Timeout.Infinite, ct);
                }
                catch (OperationCanceledException)
                {
                    wasAborted = true;
                    processingAborted.Signal();
                }

                return HandlerResultCode.Cancelled;
            }
        );

        await _client.SubscribeAsync(handler);
        await _client.SendAsync(
            _topic,
            "hanging-key",
            new TestPayload { Content = "I will hang until aborted" }
        );

        using var cts = new CancellationTokenSource(IntegrationTestFixture.DefaultTimeout);
        await processingStarted.WaitAsync(cts.Token);

        var unsubscribeTask = _client.UnsubscribeAsync();
        await processingAborted.WaitAsync(cts.Token);
        await unsubscribeTask;

        Assert.True(wasAborted);
        Assert.Equal(ConsumerState.Configured, await _client.ConsumerStateAsync());
    }

    [Fact(Timeout = 60_000)]
    public async Task HandlesTransientErrorsWithRetry()
    {
        using var span = _tracer.StartActiveSpan("test.transient_error");

        var messageCount = 0;
        var retryEvent = new EventNotifier();

        var handler = new TestEventHandler(
            onMessage: (_, _, _) =>
            {
                messageCount++;
                if (messageCount == 1)
                {
                    return Task.FromResult(HandlerResultCode.TransientError);
                }
                retryEvent.Signal();
                return Task.FromResult(HandlerResultCode.Success);
            }
        );

        await _client.SubscribeAsync(handler);
        await _client.SendAsync(
            _topic,
            "test-key",
            new TestPayload { Content = "Trigger transient error" }
        );

        using var cts = new CancellationTokenSource(IntegrationTestFixture.DefaultTimeout);
        await retryEvent.WaitAsync(cts.Token);

        Assert.True(messageCount >= 2);
    }

    [Fact(Timeout = 60_000)]
    public async Task HandlesPermanentErrorsWithoutRetry()
    {
        using var span = _tracer.StartActiveSpan("test.permanent_error");

        var messageCount = 0;
        var errorEvent = new EventNotifier();

        var handler = new TestEventHandler(
            onMessage: (_, _, _) =>
            {
                messageCount++;
                errorEvent.Signal();
                return Task.FromResult(HandlerResultCode.PermanentError);
            }
        );

        await _client.SubscribeAsync(handler);
        await _client.SendAsync(
            _topic,
            "test-key",
            new TestPayload { Content = "Trigger permanent error" }
        );

        using var cts = new CancellationTokenSource(IntegrationTestFixture.DefaultTimeout);
        await errorEvent.WaitAsync(cts.Token);

        // Wait a bit to ensure no retries happen
        await Task.Delay(TimeSpan.FromSeconds(3));

        Assert.Equal(1, messageCount);
    }

    [Fact(Timeout = 60_000)]
    public async Task SchedulesAndFiresTimersAtCorrectTime()
    {
        using var span = _tracer.StartActiveSpan("test.timer_scheduling");

        var messageReceived = new EventNotifier();
        var timerFired = new MessageChannel<(Timer Timer, DateTimeOffset ActualTime)>();
        DateTimeOffset scheduledTime = default;

        var handler = new TestEventHandler(
            onMessage: async (ctx, _, _) =>
            {
                scheduledTime = DateTimeOffset.UtcNow.AddSeconds(2);
                await ctx.ScheduleAsync(scheduledTime);
                messageReceived.Signal();
                return HandlerResultCode.Success;
            },
            onTimer: (_, timer, _) =>
            {
                timerFired.Send((timer, DateTimeOffset.UtcNow));
                return Task.FromResult(HandlerResultCode.Success);
            }
        );

        await _client.SubscribeAsync(handler);
        await _client.SendAsync(
            _topic,
            "timer-test-key",
            new TestPayload { Content = "Trigger timer" }
        );

        using var cts = new CancellationTokenSource(IntegrationTestFixture.DefaultTimeout);
        await messageReceived.WaitAsync(cts.Token);

        var (receivedTimer, actualTime) = await timerFired.ReceiveAsync(TimeSpan.FromSeconds(10));

        Assert.Equal("timer-test-key", receivedTimer.Key);
        AssertTimerApproximatelyEqual(receivedTimer.Time, scheduledTime);
        AssertTimerApproximatelyEqual(actualTime, scheduledTime);
    }

    [Fact(Timeout = 60_000)]
    public async Task ClearsAndReschedulesTimersCorrectly()
    {
        using var span = _tracer.StartActiveSpan("test.clear_and_schedule");

        var messageReceived = new EventNotifier();
        var timerFired = new MessageChannel<Timer>();
        var timerCount = 0;
        DateTimeOffset secondScheduledTime = default;

        var handler = new TestEventHandler(
            onMessage: async (ctx, _, _) =>
            {
                // Schedule first timer 4 seconds from now
                var firstTime = DateTimeOffset.UtcNow.AddSeconds(4);
                await ctx.ScheduleAsync(firstTime);

                // Clear and schedule new timer 2 seconds from now (sooner)
                secondScheduledTime = DateTimeOffset.UtcNow.AddSeconds(2);
                await ctx.ClearAndScheduleAsync(secondScheduledTime);

                messageReceived.Signal();
                return HandlerResultCode.Success;
            },
            onTimer: (_, timer, _) =>
            {
                Interlocked.Increment(ref timerCount);
                timerFired.Send(timer);
                return Task.FromResult(HandlerResultCode.Success);
            }
        );

        await _client.SubscribeAsync(handler);
        await _client.SendAsync(
            _topic,
            "clear-schedule-key",
            new TestPayload { Content = "Trigger timer" }
        );

        using var cts = new CancellationTokenSource(IntegrationTestFixture.DefaultTimeout);
        await messageReceived.WaitAsync(cts.Token);

        var timer = await timerFired.ReceiveAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(1, timerCount);
        AssertTimerApproximatelyEqual(timer.Time, secondScheduledTime);
    }

    [Fact(Timeout = 60_000)]
    public async Task UnschedulesSpecificTimers()
    {
        using var span = _tracer.StartActiveSpan("test.unschedule");

        var messageReceived = new EventNotifier();
        var timerFired = new MessageChannel<Timer>();
        var timerCount = 0;
        DateTimeOffset secondScheduledTime = default;

        var handler = new TestEventHandler(
            onMessage: async (ctx, _, _) =>
            {
                var firstTime = DateTimeOffset.UtcNow.AddSeconds(2);
                secondScheduledTime = DateTimeOffset.UtcNow.AddSeconds(4);

                await ctx.ScheduleAsync(firstTime);
                await ctx.ScheduleAsync(secondScheduledTime);

                // Unschedule the first timer
                await ctx.UnscheduleAsync(firstTime);

                messageReceived.Signal();
                return HandlerResultCode.Success;
            },
            onTimer: (_, timer, _) =>
            {
                Interlocked.Increment(ref timerCount);
                timerFired.Send(timer);
                return Task.FromResult(HandlerResultCode.Success);
            }
        );

        await _client.SubscribeAsync(handler);
        await _client.SendAsync(
            _topic,
            "unschedule-key",
            new TestPayload { Content = "Trigger timer" }
        );

        using var cts = new CancellationTokenSource(IntegrationTestFixture.DefaultTimeout);
        await messageReceived.WaitAsync(cts.Token);

        var timer = await timerFired.ReceiveAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(1, timerCount);
        AssertTimerApproximatelyEqual(timer.Time, secondScheduledTime);
    }

    [Fact(Timeout = 60_000)]
    public async Task ClearsAllScheduledTimers()
    {
        using var span = _tracer.StartActiveSpan("test.clear_scheduled");

        var messageReceived = new EventNotifier();
        var timerCount = 0;

        var handler = new TestEventHandler(
            onMessage: async (ctx, _, _) =>
            {
                await ctx.ScheduleAsync(DateTimeOffset.UtcNow.AddSeconds(2));
                await ctx.ScheduleAsync(DateTimeOffset.UtcNow.AddSeconds(3));
                await ctx.ScheduleAsync(DateTimeOffset.UtcNow.AddSeconds(4));

                // Clear all timers
                await ctx.ClearScheduledAsync();

                messageReceived.Signal();
                return HandlerResultCode.Success;
            },
            onTimer: (_, _, _) =>
            {
                Interlocked.Increment(ref timerCount);
                return Task.FromResult(HandlerResultCode.Success);
            }
        );

        await _client.SubscribeAsync(handler);
        await _client.SendAsync(
            _topic,
            "clear-all-key",
            new TestPayload { Content = "Trigger timer" }
        );

        using var cts = new CancellationTokenSource(IntegrationTestFixture.DefaultTimeout);
        await messageReceived.WaitAsync(cts.Token);

        // Wait longer than all timers would have fired
        await Task.Delay(TimeSpan.FromSeconds(5));

        Assert.Equal(0, timerCount);
    }

    [Fact(Timeout = 60_000)]
    public async Task RetrievesScheduledTimerTimes()
    {
        using var span = _tracer.StartActiveSpan("test.scheduled_retrieval");

        var messageReceived = new EventNotifier();
        DateTimeOffset[] expectedTimes = [];
        DateTimeOffset[] retrievedTimes = [];

        var handler = new TestEventHandler(
            onMessage: async (ctx, _, _) =>
            {
                var now = DateTimeOffset.UtcNow;
                expectedTimes = [now.AddSeconds(10), now.AddSeconds(20), now.AddSeconds(30)];

                foreach (var time in expectedTimes)
                {
                    await ctx.ScheduleAsync(time);
                }

                retrievedTimes = await ctx.ScheduledAsync();
                messageReceived.Signal();
                return HandlerResultCode.Success;
            }
        );

        await _client.SubscribeAsync(handler);
        await _client.SendAsync(
            _topic,
            "scheduled-retrieval-key",
            new TestPayload { Content = "Check scheduled" }
        );

        using var cts = new CancellationTokenSource(IntegrationTestFixture.DefaultTimeout);
        await messageReceived.WaitAsync(cts.Token);

        Assert.Equal(3, retrievedTimes.Length);

        var sortedExpected = expectedTimes.OrderBy(t => t).ToArray();
        var sortedRetrieved = retrievedTimes.OrderBy(t => t).ToArray();

        for (var i = 0; i < sortedExpected.Length; i++)
        {
            AssertTimerApproximatelyEqual(sortedRetrieved[i], sortedExpected[i]);
        }
    }

    [Fact(Timeout = 60_000)]
    public async Task DemonstratesUpsertBehaviorForTimersAtSameTime()
    {
        using var span = _tracer.StartActiveSpan("test.timer_upsert");

        var messageReceived = new EventNotifier();
        var timerFired = new MessageChannel<Timer>();
        var timerCount = 0;
        DateTimeOffset scheduledTime = default;
        DateTimeOffset[] retrievedTimes = [];

        var handler = new TestEventHandler(
            onMessage: async (ctx, _, _) =>
            {
                // Schedule multiple timers at the exact same time
                // Due to upsert behavior (one timer per key per second), only one should remain
                scheduledTime = DateTimeOffset.UtcNow.AddSeconds(2);

                await ctx.ScheduleAsync(scheduledTime);
                await ctx.ScheduleAsync(scheduledTime);
                await ctx.ScheduleAsync(scheduledTime);

                retrievedTimes = await ctx.ScheduledAsync();
                messageReceived.Signal();
                return HandlerResultCode.Success;
            },
            onTimer: (_, timer, _) =>
            {
                Interlocked.Increment(ref timerCount);
                timerFired.Send(timer);
                return Task.FromResult(HandlerResultCode.Success);
            }
        );

        await _client.SubscribeAsync(handler);
        await _client.SendAsync(
            _topic,
            "upsert-key",
            new TestPayload { Content = "Trigger timer" }
        );

        using var cts = new CancellationTokenSource(IntegrationTestFixture.DefaultTimeout);
        await messageReceived.WaitAsync(cts.Token);

        // Due to upsert behavior, only one timer should remain
        Assert.Single(retrievedTimes);
        AssertTimerApproximatelyEqual(retrievedTimes[0], scheduledTime);

        _ = await timerFired.ReceiveAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(1, timerCount);
    }

    private static void AssertTimerApproximatelyEqual(
        DateTimeOffset actual,
        DateTimeOffset expected
    )
    {
        // Round both times to seconds for comparison (timer precision)
        var actualSeconds = (long)Math.Floor(actual.ToUnixTimeMilliseconds() / 1000.0);
        var expectedSeconds = (long)Math.Floor(expected.ToUnixTimeMilliseconds() / 1000.0);
        var diff = Math.Abs(actualSeconds - expectedSeconds);

        Assert.True(
            diff <= 1,
            $"Timer times differ by {diff} seconds. Expected ~{expected:O}, got {actual:O}"
        );
    }

    /// <summary>
    /// Test payload for messages.
    /// </summary>
    private sealed record TestPayload
    {
        public string Content { get; init; } = "";
        public int Sequence { get; init; }
    }

    /// <summary>
    /// Configurable test event handler.
    /// </summary>
    private sealed class TestEventHandler : IProsodyHandler
    {
        private readonly Func<
            Context,
            Message,
            CancellationToken,
            Task<HandlerResultCode>
        >? _onMessage;
        private readonly Func<Context, Timer, CancellationToken, Task<HandlerResultCode>>? _onTimer;

        public TestEventHandler(
            Func<Context, Message, CancellationToken, Task<HandlerResultCode>>? onMessage = null,
            Func<Context, Timer, CancellationToken, Task<HandlerResultCode>>? onTimer = null
        )
        {
            _onMessage = onMessage;
            _onTimer = onTimer;
        }

        public Task<HandlerResultCode> OnMessageAsync(
            Context context,
            Message message,
            CancellationToken cancellationToken
        )
        {
            return _onMessage?.Invoke(context, message, cancellationToken)
                ?? Task.FromResult(HandlerResultCode.Success);
        }

        public Task<HandlerResultCode> OnTimerAsync(
            Context context,
            Timer timer,
            CancellationToken cancellationToken
        )
        {
            return _onTimer?.Invoke(context, timer, cancellationToken)
                ?? Task.FromResult(HandlerResultCode.Success);
        }
    }
}
