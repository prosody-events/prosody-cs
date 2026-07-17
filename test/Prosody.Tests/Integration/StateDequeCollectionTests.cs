using Prosody.State;
using Prosody.Tests.TestHelpers;

namespace Prosody.Tests.Integration;

/// <summary>
/// Integration tests for the deque keyed-state collection against real Kafka and Cassandra
/// (appendix-1 item 3): push front/back, index/count/empty, pop front/back, scan both directions, and
/// empty-deque behavior. A write event and a read event on the same key exercise cross-invocation
/// visibility.
/// </summary>
public sealed class StateDequeCollectionTests(IntegrationTestFixture fixture) : IntegrationTestBase(fixture)
{
    private sealed record DequeObservation
    {
        public long Count { get; init; }
        public bool IsEmpty { get; init; }
        public bool Get0HasValue { get; init; }
        public string Get0 { get; init; } = "";
        public bool PopFrontHasValue { get; init; }
        public string PopFront { get; init; } = "";
        public bool PopBackHasValue { get; init; }
        public string PopBack { get; init; } = "";
        public string[] Forward { get; init; } = [];
        public string[] Backward { get; init; } = [];
    }

    private static async Task SeedAndSendAsync(IntegrationTestContext ctx, TestProsodyHandler<TestPayload> handler)
    {
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
    }

    [Fact(Timeout = 60_000)]
    public async Task Deque_PushBackFront_LenGetZero()
    {
        await using var ctx = await CreateTestContextAsync(StateTestSupport.WithAllCollections());
        var observations = new MessageChannel<DequeObservation>();

        var handler = new TestProsodyHandler<TestPayload>(
            onMessage: async (context, msg, ct) =>
            {
                var deque = context.State(StateTestSupport.Backlog);
                if (msg.Payload?.Sequence == 1)
                {
                    await deque.PushBackAsync("a", ct);
                    await deque.PushBackAsync("b", ct);
                    await deque.PushFrontAsync("z", ct);
                    return;
                }

                var get0 = await deque.GetAsync(0, ct);
                observations.Send(
                    new DequeObservation
                    {
                        Count = await deque.CountAsync(ct),
                        IsEmpty = await deque.IsEmptyAsync(ct),
                        Get0HasValue = get0.HasValue,
                        Get0 = get0.ValueOr(""),
                    }
                );
            }
        );

        await SeedAndSendAsync(ctx, handler);
        var obs = await observations.ReceiveAsync(
            IntegrationTestFixture.DefaultTimeout,
            TestContext.Current.CancellationToken
        );

        Assert.Multiple(
            () => Assert.Equal(3, obs.Count),
            () => Assert.False(obs.IsEmpty),
            () => Assert.True(obs.Get0HasValue),
            () => Assert.Equal("z", obs.Get0)
        );
    }

    [Fact(Timeout = 60_000)]
    public async Task Deque_PopFrontBack()
    {
        await using var ctx = await CreateTestContextAsync(StateTestSupport.WithAllCollections());
        var observations = new MessageChannel<DequeObservation>();

        var handler = new TestProsodyHandler<TestPayload>(
            onMessage: async (context, msg, ct) =>
            {
                var deque = context.State(StateTestSupport.Backlog);
                if (msg.Payload?.Sequence == 1)
                {
                    await deque.PushBackAsync("a", ct);
                    await deque.PushBackAsync("b", ct);
                    await deque.PushFrontAsync("z", ct);
                    return;
                }

                var front = await deque.PopFrontAsync(ct);
                var back = await deque.PopBackAsync(ct);
                observations.Send(
                    new DequeObservation
                    {
                        PopFrontHasValue = front.HasValue,
                        PopFront = front.ValueOr(""),
                        PopBackHasValue = back.HasValue,
                        PopBack = back.ValueOr(""),
                    }
                );
            }
        );

        await SeedAndSendAsync(ctx, handler);
        var obs = await observations.ReceiveAsync(
            IntegrationTestFixture.DefaultTimeout,
            TestContext.Current.CancellationToken
        );

        Assert.Multiple(
            () => Assert.True(obs.PopFrontHasValue),
            () => Assert.Equal("z", obs.PopFront),
            () => Assert.True(obs.PopBackHasValue),
            () => Assert.Equal("b", obs.PopBack)
        );
    }

    [Fact(Timeout = 60_000)]
    public async Task Deque_ScanBothDirections()
    {
        await using var ctx = await CreateTestContextAsync(StateTestSupport.WithAllCollections());
        var observations = new MessageChannel<DequeObservation>();

        var handler = new TestProsodyHandler<TestPayload>(
            onMessage: async (context, msg, ct) =>
            {
                var deque = context.State(StateTestSupport.Backlog);
                if (msg.Payload?.Sequence == 1)
                {
                    await deque.PushBackAsync("a", ct);
                    await deque.PushBackAsync("b", ct);
                    await deque.PushFrontAsync("z", ct);
                    return;
                }

                var forward = new List<string>();
                await foreach (var item in deque.EnumerateAsync(ScanDirection.Forward, ct))
                {
                    forward.Add(item);
                }

                var backward = new List<string>();
                await foreach (var item in deque.EnumerateAsync(ScanDirection.Backward, ct))
                {
                    backward.Add(item);
                }

                observations.Send(new DequeObservation { Forward = [.. forward], Backward = [.. backward] });
            }
        );

        await SeedAndSendAsync(ctx, handler);
        var obs = await observations.ReceiveAsync(
            IntegrationTestFixture.DefaultTimeout,
            TestContext.Current.CancellationToken
        );

        Assert.Multiple(
            () => Assert.Equal(["z", "a", "b"], obs.Forward),
            () => Assert.Equal(["b", "a", "z"], obs.Backward)
        );
    }

    [Fact(Timeout = 60_000)]
    public async Task Deque_Empty_LenZero_IsEmpty_PopsNone()
    {
        await using var ctx = await CreateTestContextAsync(StateTestSupport.WithAllCollections());
        var observations = new MessageChannel<DequeObservation>();

        var handler = new TestProsodyHandler<TestPayload>(
            onMessage: async (context, _, ct) =>
            {
                var deque = context.State(StateTestSupport.Backlog);
                var front = await deque.PopFrontAsync(ct);
                var back = await deque.PopBackAsync(ct);
                observations.Send(
                    new DequeObservation
                    {
                        Count = await deque.CountAsync(ct),
                        IsEmpty = await deque.IsEmptyAsync(ct),
                        PopFrontHasValue = front.HasValue,
                        PopBackHasValue = back.HasValue,
                    }
                );
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

        Assert.Multiple(
            () => Assert.Equal(0, obs.Count),
            () => Assert.True(obs.IsEmpty),
            () => Assert.False(obs.PopFrontHasValue),
            () => Assert.False(obs.PopBackHasValue)
        );
    }
}
