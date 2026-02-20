using Prosody.Messaging;
using Prosody.Tests.TestHelpers;

namespace Prosody.Tests.Integration;

/// <summary>
/// Timer scheduling and management tests.
/// </summary>
public sealed class TimerTests(IntegrationTestFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact(Timeout = 60_000)]
    public async Task SchedulesAndFiresTimersAtCorrectTime()
    {
        await using var ctx = await CreateTestContextAsync();

        var messageReceived = new EventNotifier();
        var timerFired = new MessageChannel<(ProsodyTimer Timer, DateTimeOffset ActualTime)>();
        DateTimeOffset scheduledTime = default;

        var handler = new TestProsodyHandler(
            onMessage: async (context, _, _) =>
            {
                scheduledTime = DateTimeOffset.UtcNow.AddSeconds(2);
                await context.ScheduleAsync(scheduledTime);
                messageReceived.Signal();
            },
            onTimer: (_, timer, _) =>
            {
                timerFired.Send((timer, DateTimeOffset.UtcNow));
                return Task.CompletedTask;
            }
        );

        await ctx.Client.SubscribeAsync(handler);
        await ctx.Client.SendAsync(
            ctx.Topic,
            "timer-test-key",
            new TestPayload { Content = "Trigger timer" },
            TestContext.Current.CancellationToken
        );

        await messageReceived.WaitAsync(TestContext.Current.CancellationToken);

        var (receivedTimer, actualTime) = await timerFired.ReceiveAsync(
            TimeSpan.FromSeconds(10),
            TestContext.Current.CancellationToken
        );

        Assert.Multiple(
            () => Assert.Equal("timer-test-key", receivedTimer.Key),
            () => AssertTimerApproximatelyEqual(receivedTimer.Time, scheduledTime),
            () => AssertTimerApproximatelyEqual(actualTime, scheduledTime)
        );
    }

    [Fact(Timeout = 60_000)]
    public async Task ClearsAndReschedulesTimersCorrectly()
    {
        await using var ctx = await CreateTestContextAsync();

        var messageReceived = new EventNotifier();
        var timerFired = new MessageChannel<ProsodyTimer>();
        int timerCount = 0;
        DateTimeOffset secondScheduledTime = default;

        var handler = new TestProsodyHandler(
            onMessage: async (context, _, _) =>
            {
                var firstTime = DateTimeOffset.UtcNow.AddSeconds(4);
                await context.ScheduleAsync(firstTime);

                secondScheduledTime = DateTimeOffset.UtcNow.AddSeconds(2);
                await context.ClearAndScheduleAsync(secondScheduledTime);

                messageReceived.Signal();
            },
            onTimer: (_, timer, _) =>
            {
                Interlocked.Increment(ref timerCount);
                timerFired.Send(timer);
                return Task.CompletedTask;
            }
        );

        await ctx.Client.SubscribeAsync(handler);
        await ctx.Client.SendAsync(
            ctx.Topic,
            "clear-schedule-key",
            new TestPayload { Content = "Trigger timer" },
            TestContext.Current.CancellationToken
        );

        await messageReceived.WaitAsync(TestContext.Current.CancellationToken);

        var timer = await timerFired.ReceiveAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.Multiple(
            () => Assert.Equal(1, timerCount),
            () => AssertTimerApproximatelyEqual(timer.Time, secondScheduledTime)
        );
    }

    [Fact(Timeout = 60_000)]
    public async Task UnschedulesSpecificTimers()
    {
        await using var ctx = await CreateTestContextAsync();

        var messageReceived = new EventNotifier();
        var timerFired = new MessageChannel<ProsodyTimer>();
        var timerCount = 0;
        DateTimeOffset secondScheduledTime = default;

        var handler = new TestProsodyHandler(
            onMessage: async (context, _, _) =>
            {
                var firstTime = DateTimeOffset.UtcNow.AddSeconds(2);
                secondScheduledTime = DateTimeOffset.UtcNow.AddSeconds(4);

                await context.ScheduleAsync(firstTime);
                await context.ScheduleAsync(secondScheduledTime);
                await context.UnscheduleAsync(firstTime);

                messageReceived.Signal();
            },
            onTimer: (_, timer, _) =>
            {
                Interlocked.Increment(ref timerCount);
                timerFired.Send(timer);
                return Task.CompletedTask;
            }
        );

        await ctx.Client.SubscribeAsync(handler);
        await ctx.Client.SendAsync(
            ctx.Topic,
            "unschedule-key",
            new TestPayload { Content = "Trigger timer" },
            TestContext.Current.CancellationToken
        );

        await messageReceived.WaitAsync(TestContext.Current.CancellationToken);

        var timer = await timerFired.ReceiveAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.Multiple(
            () => Assert.Equal(1, timerCount),
            () => AssertTimerApproximatelyEqual(timer.Time, secondScheduledTime)
        );
    }

    [Fact(Timeout = 60_000)]
    public async Task ClearsAllScheduledTimers()
    {
        await using var ctx = await CreateTestContextAsync();

        var messageReceived = new EventNotifier();
        var timerCount = 0;

        var handler = new TestProsodyHandler(
            onMessage: async (context, _, _) =>
            {
                await context.ScheduleAsync(DateTimeOffset.UtcNow.AddSeconds(2));
                await context.ScheduleAsync(DateTimeOffset.UtcNow.AddSeconds(3));
                await context.ScheduleAsync(DateTimeOffset.UtcNow.AddSeconds(4));
                await context.ClearScheduledAsync();

                messageReceived.Signal();
            },
            onTimer: (_, _, _) =>
            {
                Interlocked.Increment(ref timerCount);
                return Task.CompletedTask;
            }
        );

        await ctx.Client.SubscribeAsync(handler);
        await ctx.Client.SendAsync(
            ctx.Topic,
            "clear-all-key",
            new TestPayload { Content = "Trigger timer" },
            TestContext.Current.CancellationToken
        );

        await messageReceived.WaitAsync(TestContext.Current.CancellationToken);

        await Task.Delay(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.Equal(0, timerCount);
    }

    [Fact(Timeout = 60_000)]
    public async Task RetrievesScheduledTimerTimes()
    {
        await using var ctx = await CreateTestContextAsync();

        var messageReceived = new EventNotifier();
        DateTimeOffset[] expectedTimes = [];
        DateTimeOffset[] retrievedTimes = [];

        var handler = new TestProsodyHandler(
            onMessage: async (context, _, _) =>
            {
                var now = DateTimeOffset.UtcNow;
                expectedTimes = [now.AddSeconds(10), now.AddSeconds(20), now.AddSeconds(30)];

                foreach (var time in expectedTimes)
                {
                    await context.ScheduleAsync(time);
                }

                retrievedTimes = await context.ScheduledAsync();
                messageReceived.Signal();
            }
        );

        await ctx.Client.SubscribeAsync(handler);
        await ctx.Client.SendAsync(
            ctx.Topic,
            "scheduled-retrieval-key",
            new TestPayload { Content = "Check scheduled" },
            TestContext.Current.CancellationToken
        );

        await messageReceived.WaitAsync(TestContext.Current.CancellationToken);

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
        await using var ctx = await CreateTestContextAsync();

        var messageReceived = new EventNotifier();
        var timerFired = new MessageChannel<ProsodyTimer>();
        var timerCount = 0;
        DateTimeOffset scheduledTime = default;
        DateTimeOffset[] retrievedTimes = [];

        var handler = new TestProsodyHandler(
            onMessage: async (context, _, _) =>
            {
                scheduledTime = DateTimeOffset.UtcNow.AddSeconds(2);

                await context.ScheduleAsync(scheduledTime);
                await context.ScheduleAsync(scheduledTime);
                await context.ScheduleAsync(scheduledTime);

                retrievedTimes = await context.ScheduledAsync();
                messageReceived.Signal();
            },
            onTimer: (_, timer, _) =>
            {
                Interlocked.Increment(ref timerCount);
                timerFired.Send(timer);
                return Task.CompletedTask;
            }
        );

        await ctx.Client.SubscribeAsync(handler);
        await ctx.Client.SendAsync(
            ctx.Topic,
            "upsert-key",
            new TestPayload { Content = "Trigger timer" },
            TestContext.Current.CancellationToken
        );

        await messageReceived.WaitAsync(TestContext.Current.CancellationToken);

        Assert.Multiple(
            () => Assert.Single(retrievedTimes),
            () => AssertTimerApproximatelyEqual(retrievedTimes[0], scheduledTime)
        );

        _ = await timerFired.ReceiveAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.Equal(1, timerCount);
    }
}
