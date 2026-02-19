using Prosody.Tests.TestHelpers;

namespace Prosody.Tests.Integration;

/// <summary>
/// Tests for message sending and receiving.
/// </summary>
public sealed class MessageTests(IntegrationTestFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact(Timeout = 60_000)]
    public async Task SendsAndReceivesMessage()
    {
        await using var ctx = await CreateTestContextAsync();

        var messages = new MessageChannel<Message>();
        var handler = new TestProsodyHandler(
            onMessage: (_, msg, _) =>
            {
                messages.Send(msg);
                return Task.CompletedTask;
            }
        );

        await ctx.Client.SubscribeAsync(handler);

        var testPayload = new TestPayload { Content = "Hello, Kafka!" };
        await ctx.Client.SendAsync(ctx.Topic, "test-key", testPayload, TestContext.Current.CancellationToken);

        var received = await messages.ReceiveAsync(
            IntegrationTestFixture.DefaultTimeout,
            TestContext.Current.CancellationToken
        );

        var payload = received.GetPayload<TestPayload>();
        Assert.Multiple(
            () => Assert.Equal(ctx.Topic, received.Topic),
            () => Assert.Equal("test-key", received.Key),
            () => Assert.Equal("Hello, Kafka!", payload.Content)
        );
    }

    [Fact(Timeout = 60_000)]
    public async Task HandlesMultipleMessagesWithCorrectOrdering()
    {
        await using var ctx = await CreateTestContextAsync();

        var messages = new MessageChannel<Message>();
        var handler = new TestProsodyHandler(
            onMessage: (_, msg, _) =>
            {
                messages.Send(msg);
                return Task.CompletedTask;
            }
        );

        await ctx.Client.SubscribeAsync(handler);

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
            await ctx.Client.SendAsync(ctx.Topic, key, payload, TestContext.Current.CancellationToken);
        }

        var received = await messages.ReceiveAsync(
            messagesToSend.Length,
            IntegrationTestFixture.DefaultTimeout,
            TestContext.Current.CancellationToken
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

        Assert.All(received, m => Assert.Equal(ctx.Topic, m.Topic));
    }

    [Fact(Timeout = 60_000)]
    public async Task SupportsCancellationTokenInHandler()
    {
        await using var ctx = await CreateTestContextAsync();

        var processingStarted = new EventNotifier();
        var processingAborted = new EventNotifier();
        var wasAborted = false;

        var handler = new TestProsodyHandler(
            onMessage: async (_, _, ct) =>
            {
                processingStarted.Signal();

                try
                {
                    await Task.Delay(Timeout.Infinite, ct);
                }
                catch (OperationCanceledException)
                {
                    wasAborted = true;
                    processingAborted.Signal();
                    throw;
                }
            }
        );

        await ctx.Client.SubscribeAsync(handler);
        await ctx.Client.SendAsync(
            ctx.Topic,
            "hanging-key",
            new TestPayload { Content = "I will hang until aborted" },
            TestContext.Current.CancellationToken
        );

        await processingStarted.WaitAsync(TestContext.Current.CancellationToken);

        var unsubscribeTask = ctx.Client.UnsubscribeAsync();
        await processingAborted.WaitAsync(TestContext.Current.CancellationToken);
        await unsubscribeTask;

        var state = await ctx.Client.ConsumerStateAsync();
        Assert.Multiple(() => Assert.True(wasAborted), () => Assert.Equal(ConsumerState.Configured, state));
    }
}
