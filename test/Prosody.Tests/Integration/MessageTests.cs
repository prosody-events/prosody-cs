using Prosody.Errors;
using Prosody.Messaging;
using Prosody.Tests.TestHelpers;

namespace Prosody.Tests.Integration;

/// <summary>
/// Tests for message sending and receiving.
/// </summary>
public sealed class MessageTests(IntegrationTestFixture fixture) : IntegrationTestBase(fixture)
{
    private sealed record RequestResponse(string Key, bool Accepted);

    private sealed class RequestHandler : IProsodyRequestHandler<TestPayload, RequestResponse>
    {
        public Task<RequestResponse> OnMessageAsync(
            ProsodyContext prosodyContext,
            Message<TestPayload> message,
            CancellationToken cancellationToken
        ) => Task.FromResult(new RequestResponse(message.Key, true));

        public Task<RequestResponse> OnTimerAsync(
            ProsodyContext prosodyContext,
            ProsodyTimer timer,
            CancellationToken cancellationToken
        ) => Task.FromResult(new RequestResponse(timer.Key, true));
    }

    private sealed class RejectingRequestHandler : IProsodyRequestHandler<TestPayload, RequestResponse>
    {
        public Task<RequestResponse> OnMessageAsync(
            ProsodyContext prosodyContext,
            Message<TestPayload> message,
            CancellationToken cancellationToken
        ) => Task.FromException<RequestResponse>(new PermanentException("request rejected"));

        public Task<RequestResponse> OnTimerAsync(
            ProsodyContext prosodyContext,
            ProsodyTimer timer,
            CancellationToken cancellationToken
        ) => Task.FromException<RequestResponse>(new PermanentException("request rejected"));
    }

    [Fact(Timeout = 60_000)]
    public async Task RequestReturnsLocalHandlerResponse()
    {
        await using var ctx = await CreateTestContextAsync(options => options.Subsystem = "inventory");
        await ctx.Client.SubscribeAsync(new RequestHandler());

        var results = await ctx.Client.RequestAsync<TestPayload, RequestResponse>(
            ctx.Topic,
            "order-1",
            new TestPayload { Content = "order.created" },
            ["inventory"],
            IntegrationTestFixture.DefaultTimeout,
            cancellationToken: TestContext.Current.CancellationToken
        );

        var result = Assert.IsType<Ok<RequestResponse>>(Assert.Single(results));
        Assert.Equal(new RequestResponse("order-1", true), result.Value);
    }

    [Fact(Timeout = 60_000)]
    public async Task RequestReturnsPermanentHandlerFailure()
    {
        await using var ctx = await CreateTestContextAsync(options => options.Subsystem = "inventory");
        await ctx.Client.SubscribeAsync(new RejectingRequestHandler());

        var results = await ctx.Client.RequestAsync<TestPayload, RequestResponse>(
            ctx.Topic,
            "order-1",
            new TestPayload { Content = "order.created" },
            ["inventory"],
            IntegrationTestFixture.DefaultTimeout,
            cancellationToken: TestContext.Current.CancellationToken
        );

        var error = Assert.IsType<HandlerResponseError>(
            Assert.IsType<Err<RequestResponse>>(Assert.Single(results)).Error
        );
        Assert.Multiple(
            () => Assert.Equal(ResponseErrorCategory.Permanent, error.Category),
            () => Assert.Contains("request rejected", error.Message, StringComparison.Ordinal)
        );
    }

    [Fact(Timeout = 60_000)]
    public async Task SendsAndReceivesMessage()
    {
        await using var ctx = await CreateTestContextAsync();

        var messages = new MessageChannel<Message<TestPayload>>();
        var handler = new TestProsodyHandler<TestPayload>(
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

        Assert.Multiple(
            () => Assert.Equal(ctx.Topic, received.Topic),
            () => Assert.Equal("test-key", received.Key),
            () => Assert.Equal("Hello, Kafka!", received.Payload?.Content)
        );
    }

    [Fact(Timeout = 60_000)]
    public async Task HandlesMultipleMessagesWithCorrectOrdering()
    {
        await using var ctx = await CreateTestContextAsync();

        var messages = new MessageChannel<Message<TestPayload>>();
        var handler = new TestProsodyHandler<TestPayload>(
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
            var sequences = msgs.Select(m => m.Payload?.Sequence).ToList();
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

        var handler = new TestProsodyHandler<TestPayload>(
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
    public async Task PayloadRoundTrip_HonorsSnakeCaseOverride()
    {
        await using var ctx = await CreateTestContextAsync(o =>
            o.ConfigureJsonOptions = opts =>
                opts.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.SnakeCaseLower
        );

        var messages = new MessageChannel<Message<TestPayload>>();
        var handler = new TestProsodyHandler<TestPayload>(
            onMessage: (_, msg, _) =>
            {
                messages.Send(msg);
                return Task.CompletedTask;
            }
        );

        await ctx.Client.SubscribeAsync(handler);

        // MessageContent is a multi-word property: snake_case → "message_content"; camelCase → "messageContent".
        // This asserts that the snake_case override is actually applied end-to-end and not silently ignored.
        var testPayload = new TestPayload
        {
            Content = "snake-case-test",
            Sequence = 7,
            MessageContent = "multi-word-value",
        };
        await ctx.Client.SendAsync(ctx.Topic, "sc-key", testPayload, TestContext.Current.CancellationToken);

        var received = await messages.ReceiveAsync(
            IntegrationTestFixture.DefaultTimeout,
            TestContext.Current.CancellationToken
        );

        Assert.Multiple(
            () => Assert.Equal("snake-case-test", received.Payload?.Content),
            () => Assert.Equal(7, received.Payload?.Sequence),
            () => Assert.Equal("multi-word-value", received.Payload?.MessageContent)
        );
    }
}
