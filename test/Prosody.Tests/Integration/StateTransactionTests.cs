using Prosody.Messaging;
using Prosody.State;
using Prosody.Tests.TestHelpers;

namespace Prosody.Tests.Integration;

/// <summary>
/// Integration tests for keyed-state transactional semantics against real Kafka and Cassandra
/// (appendix-1 items 5 and 6): the committed floor survives a failed attempt and a rollback, and a
/// handle/context/read leaked past its attempt (failed or successful) fails with the terminated
/// transient error and has no store effect. Attempt fencing is owned entirely by core.
/// </summary>
public sealed class StateTransactionTests(IntegrationTestFixture fixture) : IntegrationTestBase(fixture)
{
    private sealed record CommitObservation
    {
        public int Attempt { get; init; }
        public bool HasValue { get; init; }
        public string Value { get; init; } = "";
    }

    private sealed record RollbackObservation
    {
        public string Before { get; init; } = "";
        public string After { get; init; } = "";
        public string? Error { get; init; }
    }

    private sealed record MapRollbackObservation
    {
        public bool BeforeKeptHasValue { get; init; }
        public int BeforeKept { get; init; }
        public bool BeforeDroppedHasValue { get; init; }
        public bool AfterKeptHasValue { get; init; }
        public int AfterKept { get; init; }
        public bool AfterDroppedHasValue { get; init; }
        public string? Error { get; init; }
    }

    private sealed record LeakObservation
    {
        public bool LeakedRejected { get; init; }
        public bool LeakedTransient { get; init; }
        public bool LeakedStateError { get; init; }
        public bool FreshHasValue { get; init; }
    }

    [Fact(Timeout = 60_000)]
    public async Task Commit_FloorSurvivesFailedAttempt_VisibleOnRetry()
    {
        await using var ctx = await CreateTestContextAsync(StateTestSupport.WithAllCollections());
        var v = Guid.NewGuid().ToString("N");
        var attempt = 0;
        var observations = new MessageChannel<CommitObservation>();

        var handler = new TestProsodyHandler<TestPayload>(
            onMessage: async (context, _, ct) =>
            {
                var current = Interlocked.Increment(ref attempt);
                var cart = context.State(StateTestSupport.Cart);
                if (current == 1)
                {
                    await cart.SetAsync(new CartState { V = v }, ct);
                    await cart.CommitAsync(ct);
                    throw new TransientStateException("fail after commit");
                }

                var got = await cart.GetAsync(ct);
                observations.Send(
                    new CommitObservation
                    {
                        Attempt = current,
                        HasValue = got.HasValue,
                        Value = got.ValueOr(new CartState()).V,
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
            () => Assert.Equal(2, obs.Attempt),
            () => Assert.True(obs.HasValue),
            () => Assert.Equal(v, obs.Value)
        );
    }

    [Fact(Timeout = 60_000)]
    public async Task Rollback_DiscardsUncommittedWrites()
    {
        await using var ctx = await CreateTestContextAsync(StateTestSupport.WithAllCollections());
        var a = Guid.NewGuid().ToString("N");
        var b = Guid.NewGuid().ToString("N");
        var observations = new MessageChannel<RollbackObservation>();

        var handler = new TestProsodyHandler<TestPayload>(
            onMessage: async (context, _, ct) =>
            {
                var cart = context.State(StateTestSupport.Cart);
                try
                {
                    await cart.SetAsync(new CartState { V = a }, ct);
                    await cart.CommitAsync(ct);
                    await cart.SetAsync(new CartState { V = b }, ct);
                    var before = await cart.GetAsync(ct);
                    await cart.RollbackAsync(ct);
                    var after = await cart.GetAsync(ct);
                    observations.Send(new RollbackObservation { Before = before.Value.V, After = after.Value.V });
                }
                catch (StateException ex)
                {
                    observations.Send(new RollbackObservation { Error = ex.Message });
                }
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
            () => Assert.Null(obs.Error),
            () => Assert.Equal(b, obs.Before),
            () => Assert.Equal(a, obs.After)
        );
    }

    [Fact(Timeout = 60_000)]
    public async Task Map_CommitFloorSurvivesRollback()
    {
        await using var ctx = await CreateTestContextAsync(StateTestSupport.WithAllCollections());
        var observations = new MessageChannel<MapRollbackObservation>();

        var handler = new TestProsodyHandler<TestPayload>(
            onMessage: async (context, _, ct) =>
            {
                var map = context.State(StateTestSupport.Totals);
                try
                {
                    await map.SetAsync("kept", 1, ct);
                    await map.CommitAsync(ct);
                    await map.SetAsync("kept", 2, ct);
                    await map.SetAsync("dropped", 9, ct);
                    var beforeKept = await map.GetAsync("kept", ct);
                    var beforeDropped = await map.GetAsync("dropped", ct);
                    await map.RollbackAsync(ct);
                    var afterKept = await map.GetAsync("kept", ct);
                    var afterDropped = await map.GetAsync("dropped", ct);
                    observations.Send(
                        new MapRollbackObservation
                        {
                            BeforeKeptHasValue = beforeKept.HasValue,
                            BeforeKept = beforeKept.ValueOr(-1),
                            BeforeDroppedHasValue = beforeDropped.HasValue,
                            AfterKeptHasValue = afterKept.HasValue,
                            AfterKept = afterKept.ValueOr(-1),
                            AfterDroppedHasValue = afterDropped.HasValue,
                        }
                    );
                }
                catch (StateException ex)
                {
                    observations.Send(new MapRollbackObservation { Error = ex.Message });
                }
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
            () => Assert.Null(obs.Error),
            () => Assert.Equal(2, obs.BeforeKept),
            () => Assert.True(obs.BeforeDroppedHasValue),
            () => Assert.Equal(1, obs.AfterKept),
            () => Assert.False(obs.AfterDroppedHasValue)
        );
    }

    [Fact(Timeout = 60_000)]
    public async Task LeakedHandle_AfterFailedAttempt_RejectsTransient_NoStateEffect()
    {
        await using var ctx = await CreateTestContextAsync(StateTestSupport.WithAllCollections());
        var attempt = 0;
        IValueState<CartState>? leaked = null;
        var observations = new MessageChannel<LeakObservation>();

        var handler = new TestProsodyHandler<TestPayload>(
            onMessage: async (context, _, ct) =>
            {
                var current = Interlocked.Increment(ref attempt);
                var cart = context.State(StateTestSupport.Cart);
                if (current == 1)
                {
                    leaked = cart;
                    await cart.SetAsync(new CartState { V = Guid.NewGuid().ToString("N") }, ct);
                    throw new TransientStateException("fail attempt 1");
                }

                var rejected = false;
                var transient = false;
                try
                {
                    await leaked!.GetAsync(ct);
                }
                catch (TransientStateException)
                {
                    rejected = true;
                    transient = true;
                }
                catch (StateException)
                {
                    rejected = true;
                }

                var fresh = await cart.GetAsync(ct);
                observations.Send(
                    new LeakObservation
                    {
                        LeakedRejected = rejected,
                        LeakedTransient = transient,
                        FreshHasValue = fresh.HasValue,
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
            () => Assert.True(obs.LeakedRejected),
            () => Assert.True(obs.LeakedTransient),
            () => Assert.False(obs.FreshHasValue)
        );
    }

    [Fact(Timeout = 60_000)]
    public async Task LeakedContext_AfterFailedAttempt_CannotBindWorkingHandle()
    {
        await using var ctx = await CreateTestContextAsync(StateTestSupport.WithAllCollections());
        var attempt = 0;
        ProsodyContext? leakedCtx = null;
        var observations = new MessageChannel<LeakObservation>();

        var handler = new TestProsodyHandler<TestPayload>(
            onMessage: async (context, _, ct) =>
            {
                var current = Interlocked.Increment(ref attempt);
                if (current == 1)
                {
                    leakedCtx = context;
                    throw new TransientStateException("fail attempt 1");
                }

                var rejected = false;
                var transient = false;
                var stateError = false;
                try
                {
                    var map = leakedCtx!.State(StateTestSupport.Totals);
                    await map.GetAsync("x", ct);
                }
                catch (TransientStateException)
                {
                    rejected = true;
                    transient = true;
                    stateError = true;
                }
                catch (StateException)
                {
                    rejected = true;
                    stateError = true;
                }

                observations.Send(
                    new LeakObservation
                    {
                        LeakedRejected = rejected,
                        LeakedTransient = transient,
                        LeakedStateError = stateError,
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
            () => Assert.True(obs.LeakedRejected),
            () => Assert.True(obs.LeakedStateError),
            () => Assert.True(obs.LeakedTransient)
        );
    }

    [Fact(Timeout = 60_000)]
    public async Task LeakedRead_AfterSuccessfulHandler_RejectsTransient()
    {
        await using var ctx = await CreateTestContextAsync(StateTestSupport.WithAllCollections());
        IValueState<CartState>? leaked = null;
        var events = new MessageChannel<string>();

        var handler = new TestProsodyHandler<TestPayload>(
            onMessage: async (context, msg, ct) =>
            {
                if (msg.Payload?.Sequence == 1)
                {
                    leaked = context.State(StateTestSupport.Cart);
                    await leaked.SetAsync(new CartState { V = Guid.NewGuid().ToString("N") }, ct);
                    events.Send("captured");
                    return;
                }

                events.Send("sentinel-started");
            }
        );

        await ctx.Client.SubscribeAsync(handler);
        // Same key: per-key serialization guarantees step 1 fully tears down before step 2 begins.
        var key = TopicGenerator.GenerateKey();
        await ctx.Client.SendAsync(
            ctx.Topic,
            key,
            new TestPayload { Sequence = 1 },
            TestContext.Current.CancellationToken
        );
        Assert.Equal(
            "captured",
            await events.ReceiveAsync(IntegrationTestFixture.DefaultTimeout, TestContext.Current.CancellationToken)
        );
        await ctx.Client.SendAsync(
            ctx.Topic,
            key,
            new TestPayload { Sequence = 2 },
            TestContext.Current.CancellationToken
        );
        Assert.Equal(
            "sentinel-started",
            await events.ReceiveAsync(IntegrationTestFixture.DefaultTimeout, TestContext.Current.CancellationToken)
        );

        await Assert.ThrowsAsync<TransientStateException>(async () =>
            await leaked!.GetAsync(TestContext.Current.CancellationToken)
        );
    }
}
