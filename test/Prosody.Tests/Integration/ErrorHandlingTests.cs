using Prosody.Tests.TestHelpers;

namespace Prosody.Tests.Integration;

/// <summary>
/// Error handling and retry tests.
/// </summary>
public sealed class ErrorHandlingTests(IntegrationTestFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact(Timeout = 60_000)]
    public async Task HandlesTransientErrorsWithRetry()
    {
        await using var ctx = await CreateTestContextAsync();

        var messageCount = 0;
        var retryEvent = new EventNotifier();

        var handler = new TestProsodyHandler(
            onMessage: (_, _, _) =>
            {
                messageCount++;
                if (messageCount == 1)
                {
                    throw new InvalidOperationException("Transient failure");
                }
                retryEvent.Signal();
                return Task.CompletedTask;
            }
        );

        await ctx.Client.SubscribeAsync(handler);
        await ctx.Client.SendAsync(
            ctx.Topic,
            "test-key",
            new TestPayload { Content = "Trigger transient error" },
            TestContext.Current.CancellationToken
        );

        await retryEvent.WaitAsync(TestContext.Current.CancellationToken);

        Assert.True(messageCount >= 2);
    }

    [Fact(Timeout = 60_000)]
    public async Task HandlesPermanentErrorsWithoutRetry()
    {
        await using var ctx = await CreateTestContextAsync();

        var messageCount = 0;
        var errorEvent = new EventNotifier();

        var handler = new TestProsodyHandler(
            onMessage: (_, _, _) =>
            {
                messageCount++;
                errorEvent.Signal();
                throw new PermanentException("Permanent failure");
            }
        );

        await ctx.Client.SubscribeAsync(handler);
        await ctx.Client.SendAsync(
            ctx.Topic,
            "test-key",
            new TestPayload { Content = "Trigger permanent error" },
            TestContext.Current.CancellationToken
        );

        await errorEvent.WaitAsync(TestContext.Current.CancellationToken);

        await Task.Delay(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);

        Assert.Equal(1, messageCount);
    }

    [Fact(Timeout = 60_000)]
    public async Task HandlesPermanentErrorsViaAttribute()
    {
        await using var ctx = await CreateTestContextAsync();

        var messageCount = 0;
        var errorEvent = new EventNotifier();

        var handler = new AttributeBasedHandler(onMessage: () =>
        {
            messageCount++;
            errorEvent.Signal();
            throw new FormatException("Bad format");
        });

        await ctx.Client.SubscribeAsync(handler);
        await ctx.Client.SendAsync(
            ctx.Topic,
            "test-key",
            new TestPayload { Content = "Trigger permanent error via attribute" },
            TestContext.Current.CancellationToken
        );

        await errorEvent.WaitAsync(TestContext.Current.CancellationToken);

        await Task.Delay(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);

        Assert.Equal(1, messageCount);
    }

    private sealed class AttributeBasedHandler(Action onMessage) : IProsodyHandler
    {
        [PermanentError(typeof(FormatException))]
        public Task OnMessageAsync(ProsodyContext prosodyContext, Message message, CancellationToken cancellationToken)
        {
            onMessage();
            return Task.CompletedTask;
        }

        public Task OnTimerAsync(ProsodyContext prosodyContext, Timer timer, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
