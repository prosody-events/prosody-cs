using System.Diagnostics;
using Prosody.State;
using Prosody.Tests.TestHelpers;

namespace Prosody.Tests.Integration;

/// <summary>
/// Integration tests for the value keyed-state collection against real Kafka and Cassandra
/// (appendix-1 item 1), plus the source-generated JSON path. Two events on the same key exercise
/// write-then-read across handler invocations. The passthrough-codec scalar/array pins live in
/// <see cref="StateDequeCollectionTests"/> because they must read through a durable scan, not a
/// cached point read, to actually exercise the codec's decode.
/// </summary>
public sealed class StateValueCollectionTests(IntegrationTestFixture fixture) : IntegrationTestBase(fixture)
{
    private sealed record ValueObservation
    {
        public int Sequence { get; init; }
        public bool HasValue { get; init; }
        public string Value { get; init; } = "";
    }

    [Fact(Timeout = 60_000)]
    public async Task Value_SetInEvent1_GetInEvent2_ReturnsIt()
    {
        await using var ctx = await CreateTestContextAsync(StateTestSupport.WithAllCollections());
        var v = Guid.NewGuid().ToString("N");
        var observations = new MessageChannel<ValueObservation>();

        var handler = new TestProsodyHandler<TestPayload>(
            onMessage: async (context, msg, ct) =>
            {
                var cart = context.State(StateTestSupport.Cart);
                if (msg.Payload?.Sequence == 1)
                {
                    await cart.SetAsync(new CartState { V = v }, ct);
                    return;
                }

                var got = await cart.GetAsync(ct);
                observations.Send(
                    new ValueObservation
                    {
                        Sequence = 2,
                        HasValue = got.HasValue,
                        Value = got.ValueOr(new CartState()).V,
                    }
                );
            }
        );

        await ctx.Client.SubscribeAsync(handler);
        var key = TopicGenerator.GenerateKey();
        await ctx.Client.SendAsync(
            ctx.Topic,
            key,
            new TestPayload { Sequence = 1 },
            TestContext.Current.CancellationToken
        );
        await ctx.Client.SendAsync(
            ctx.Topic,
            key,
            new TestPayload { Sequence = 2 },
            TestContext.Current.CancellationToken
        );

        var obs = await observations.ReceiveAsync(
            IntegrationTestFixture.DefaultTimeout,
            TestContext.Current.CancellationToken
        );

        Assert.Multiple(() => Assert.True(obs.HasValue), () => Assert.Equal(v, obs.Value));
    }

    [Fact(Timeout = 60_000)]
    public async Task Value_Clear_ThenGet_ReturnsNone()
    {
        await using var ctx = await CreateTestContextAsync(StateTestSupport.WithAllCollections());
        var observations = new MessageChannel<ValueObservation>();

        var handler = new TestProsodyHandler<TestPayload>(
            onMessage: async (context, msg, ct) =>
            {
                var cart = context.State(StateTestSupport.Cart);
                if (msg.Payload?.Sequence == 1)
                {
                    await cart.SetAsync(new CartState { V = Guid.NewGuid().ToString("N") }, ct);
                    await cart.ClearAsync(ct);
                    return;
                }

                var got = await cart.GetAsync(ct);
                observations.Send(new ValueObservation { Sequence = 2, HasValue = got.HasValue });
            }
        );

        await ctx.Client.SubscribeAsync(handler);
        var key = TopicGenerator.GenerateKey();
        await ctx.Client.SendAsync(
            ctx.Topic,
            key,
            new TestPayload { Sequence = 1 },
            TestContext.Current.CancellationToken
        );
        await ctx.Client.SendAsync(
            ctx.Topic,
            key,
            new TestPayload { Sequence = 2 },
            TestContext.Current.CancellationToken
        );

        var obs = await observations.ReceiveAsync(
            IntegrationTestFixture.DefaultTimeout,
            TestContext.Current.CancellationToken
        );

        Assert.False(obs.HasValue);
    }

    [Fact(Timeout = 60_000)]
    public async Task Value_NeverWritten_ReturnsNone()
    {
        await using var ctx = await CreateTestContextAsync(StateTestSupport.WithAllCollections());
        var observations = new MessageChannel<ValueObservation>();

        var handler = new TestProsodyHandler<TestPayload>(
            onMessage: async (context, _, ct) =>
            {
                var cart = context.State(StateTestSupport.Cart);
                var got = await cart.GetAsync(ct);
                observations.Send(new ValueObservation { HasValue = got.HasValue });
            }
        );

        await ctx.Client.SubscribeAsync(handler);
        await ctx.Client.SendAsync(
            ctx.Topic,
            TopicGenerator.GenerateKey(),
            new TestPayload(),
            TestContext.Current.CancellationToken
        );

        var obs = await observations.ReceiveAsync(
            IntegrationTestFixture.DefaultTimeout,
            TestContext.Current.CancellationToken
        );

        Assert.False(obs.HasValue);
    }

    [Fact(Timeout = 60_000)]
    public async Task Value_RichJson_RoundTripsFaithfully()
    {
        await using var ctx = await CreateTestContextAsync(StateTestSupport.WithAllCollections());
        var written = new RichState
        {
            Text = "café 😀 déjà",
            Number = 3.5,
            Flag = true,
            Items = [1, 2, 3],
            Nested = null,
        };
        var readBack = new MessageChannel<RichState>();

        var handler = new TestProsodyHandler<TestPayload>(
            onMessage: async (context, msg, ct) =>
            {
                var rich = context.State(StateTestSupport.Rich);
                if (msg.Payload?.Sequence == 1)
                {
                    await rich.SetAsync(written, ct);
                    return;
                }

                var got = await rich.GetAsync(ct);
                readBack.Send(got.Value);
            }
        );

        await ctx.Client.SubscribeAsync(handler);
        var key = TopicGenerator.GenerateKey();
        await ctx.Client.SendAsync(
            ctx.Topic,
            key,
            new TestPayload { Sequence = 1 },
            TestContext.Current.CancellationToken
        );
        await ctx.Client.SendAsync(
            ctx.Topic,
            key,
            new TestPayload { Sequence = 2 },
            TestContext.Current.CancellationToken
        );

        var got = await readBack.ReceiveAsync(
            IntegrationTestFixture.DefaultTimeout,
            TestContext.Current.CancellationToken
        );

        Assert.Multiple(
            () => Assert.Equal(written.Text, got.Text),
            () => Assert.Equal(written.Number, got.Number),
            () => Assert.Equal(written.Flag, got.Flag),
            () => Assert.Equal(written.Items, got.Items),
            () => Assert.Null(got.Nested)
        );
    }

    [Fact(Timeout = 60_000)]
    public async Task Value_SourceGenContext_RoundTrips()
    {
        // The client's whole JSON path — message payloads AND state items — resolves through the
        // source-gen context, so the subscribed message payload must be a type the context includes.
        var sourceGen = StateDefinition.Value<SourceGenState>("sourceGen");
        await using var ctx = await CreateTestContextAsync(options =>
        {
            options.StateCollections = [sourceGen];
            options.ConfigureJsonOptions = opts => opts.TypeInfoResolver = StateSerializerContext.Default;
        });
        var written = new SourceGenState { Name = "aot", Count = 7 };
        var readBack = new MessageChannel<SourceGenState>();

        var handler = new TestProsodyHandler<SourceGenState>(
            onMessage: async (context, msg, ct) =>
            {
                var state = context.State(sourceGen);
                if (msg.Payload?.Count == 1)
                {
                    await state.SetAsync(written, ct);
                    return;
                }

                var got = await state.GetAsync(ct);
                readBack.Send(got.Value);
            }
        );

        await ctx.Client.SubscribeAsync(handler);
        var key = TopicGenerator.GenerateKey();
        await ctx.Client.SendAsync(
            ctx.Topic,
            key,
            new SourceGenState { Count = 1 },
            TestContext.Current.CancellationToken
        );
        await ctx.Client.SendAsync(
            ctx.Topic,
            key,
            new SourceGenState { Count = 2 },
            TestContext.Current.CancellationToken
        );

        var got = await readBack.ReceiveAsync(
            IntegrationTestFixture.DefaultTimeout,
            TestContext.Current.CancellationToken
        );

        Assert.Multiple(() => Assert.Equal(written.Name, got.Name), () => Assert.Equal(written.Count, got.Count));
    }

    [Fact(Timeout = 60_000)]
    public async Task StateOp_RunsUnderHandlerSpan()
    {
        // In-process tracing smoke (GREEN-IS-CORRECT, mirrors JS C11): the handler activity is
        // Current while a state op runs, so the core collection span parents to it. Full core-span
        // topology is collector-delegated (02-lgtm-trace-audit.md) and not observable here. A listener
        // must be present or ActivitySource.StartActivity returns null and no activity is created.
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "Prosody",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(listener);

        await using var ctx = await CreateTestContextAsync(StateTestSupport.WithAllCollections());
        var displayNames = new MessageChannel<string>();

        var handler = new TestProsodyHandler<TestPayload>(
            onMessage: async (context, _, ct) =>
            {
                var cart = context.State(StateTestSupport.Cart);
                await cart.GetAsync(ct);
                displayNames.Send(Activity.Current?.DisplayName ?? "<none>");
            }
        );

        await ctx.Client.SubscribeAsync(handler);
        await ctx.Client.SendAsync(
            ctx.Topic,
            TopicGenerator.GenerateKey(),
            new TestPayload(),
            TestContext.Current.CancellationToken
        );

        var displayName = await displayNames.ReceiveAsync(
            IntegrationTestFixture.DefaultTimeout,
            TestContext.Current.CancellationToken
        );

        Assert.Equal("on_message", displayName);
    }
}
