using System.Text.Json;
using Prosody.Configuration;
using Prosody.Messaging;
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
            () => Assert.Equal("Hello, Kafka!", payload?.Content)
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

        var byKey = received.GroupBy(m => m.Key).ToDictionary(g => g.Key, g => g.ToList());

        foreach (var (_, msgs) in byKey)
        {
            var sequences = msgs.Select(m => m.GetPayload<TestPayload>()?.Sequence).ToList();
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

        var state = await ctx.Client.GetConsumerStateAsync();
        Assert.Multiple(() => Assert.True(wasAborted), () => Assert.Equal(ConsumerState.Configured, state));
    }

    [Fact(Timeout = 60_000)]
    public async Task RawPayloadReturnsProducerEncodedBytes()
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

        var testPayload = new TestPayload { Content = "raw-payload-test", Sequence = 42 };
        await ctx.Client.SendAsync(ctx.Topic, "rp-key", testPayload, TestContext.Current.CancellationToken);

        var received = await messages.ReceiveAsync(
            IntegrationTestFixture.DefaultTimeout,
            TestContext.Current.CancellationToken
        );

        var expectedBytes = JsonSerializer.SerializeToUtf8Bytes(testPayload, ctx.Client._jsonOptions);
        Assert.Equal(expectedBytes, received.RawPayload.ToArray());

        // Two accesses must share the same backing array (zero-copy contract).
        var first = received.RawPayload;
        var second = received.RawPayload;
        Assert.True(first.Equals(second), "RawPayload instances must share the same backing array");
    }

    [Fact(Timeout = 60_000)]
    public async Task GetPayload_HonorsSnakeCaseOverride_RoundTrip()
    {
        await using var ctx = await CreateTestContextAsync(o =>
            o.ConfigureJsonSerializer = opts => opts.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
        );

        var messages = new MessageChannel<Message>();
        var handler = new TestProsodyHandler(
            onMessage: (_, msg, _) =>
            {
                messages.Send(msg);
                return Task.CompletedTask;
            }
        );

        await ctx.Client.SubscribeAsync(handler);

        var testPayload = new TestPayload { Content = "snake-case-test", Sequence = 7 };
        await ctx.Client.SendAsync(ctx.Topic, "sc-key", testPayload, TestContext.Current.CancellationToken);

        var received = await messages.ReceiveAsync(
            IntegrationTestFixture.DefaultTimeout,
            TestContext.Current.CancellationToken
        );

        var payload = received.GetPayload<TestPayload>();
        Assert.Multiple(
            () => Assert.Equal("snake-case-test", payload?.Content),
            () => Assert.Equal(7, payload?.Sequence)
        );

        // Raw bytes use snake_case keys when the client is configured with SnakeCaseLower.
        var rawJson = System.Text.Encoding.UTF8.GetString(received.RawPayload.Span);
        Assert.Contains("\"content\":", rawJson, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Content\":", rawJson, StringComparison.Ordinal);
    }
}
