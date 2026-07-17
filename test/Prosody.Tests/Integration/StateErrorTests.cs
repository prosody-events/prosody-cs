using Prosody.Messaging;
using Prosody.State;
using Prosody.Tests.TestHelpers;

namespace Prosody.Tests.Integration;

/// <summary>
/// Integration tests for the keyed-state error taxonomy against real Kafka and Cassandra (appendix-1
/// items 8 and 9): unregistered-name and identity-mismatch are permanent, rethrown permanent/transient
/// state errors classify correctly through the existing bridge, no state error surfaces terminal, null
/// writes reject transient with the store untouched, and missing is distinguished from a stored
/// default.
/// </summary>
public sealed class StateErrorTests(IntegrationTestFixture fixture) : IntegrationTestBase(fixture)
{
    private sealed record ErrorObservation
    {
        public bool Threw { get; init; }
        public bool Permanent { get; init; }
        public bool Transient { get; init; }
        public bool StateError { get; init; }
    }

    private sealed record NullWriteObservation
    {
        public bool ValueTransient { get; init; }
        public bool DequeTransient { get; init; }
        public bool StoreIntact { get; init; }
    }

    private sealed record MissingVsDefaultObservation
    {
        public bool DecStoredHasValue { get; init; }
        public decimal DecStored { get; init; }
        public bool DecAbsentHasValue { get; init; }
        public bool BoolStoredHasValue { get; init; }
        public bool BoolStored { get; init; }
        public bool BoolAbsentHasValue { get; init; }
    }

    [Fact(Timeout = 60_000)]
    public async Task UnregisteredName_ThrowsPermanentAtVend()
    {
        await using var ctx = await CreateTestContextAsync(StateTestSupport.WithAllCollections());
        var observations = new MessageChannel<ErrorObservation>();

        var handler = new TestProsodyHandler<TestPayload>(
            onMessage: (context, _, _) =>
            {
                try
                {
                    context.State(StateDefinition.Value<int>("never-registered-" + Guid.NewGuid().ToString("N")));
                    observations.Send(new ErrorObservation { Threw = false });
                }
                catch (StateException ex)
                {
                    observations.Send(
                        new ErrorObservation
                        {
                            Threw = true,
                            Permanent = ex is PermanentStateException,
                            StateError = ex is StateException,
                            Transient = ex is TransientStateException,
                        }
                    );
                }

                return Task.CompletedTask;
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
            () => Assert.True(obs.Threw),
            () => Assert.True(obs.Permanent),
            () => Assert.True(obs.StateError),
            () => Assert.False(obs.Transient)
        );
    }

    [Fact(Timeout = 60_000)]
    public async Task SameNameDifferentKind_TwoRuns_IdentityMismatchPermanent()
    {
        var valueDef = StateDefinition.Value<int>("idcheck");
        var mapDef = StateDefinition.Map<int>("idcheck");

        await using var ctx = await CreateTestContextAsync(o => o.StateCollections = [valueDef]);

        // Run 1: persist the value-kind identity for name "idcheck" under this group.
        var run1Done = new MessageChannel<string>();
        var run1Handler = new TestProsodyHandler<TestPayload>(
            onMessage: async (context, _, ct) =>
            {
                var value = context.State(valueDef);
                await value.SetAsync(1, ct);
                await value.CommitAsync(ct);
                run1Done.Send("run1-done");
            }
        );

        await ctx.Client.SubscribeAsync(run1Handler);
        await ctx.Client.SendAsync(
            ctx.Topic,
            TopicGenerator.GenerateKey(),
            new TestPayload(),
            TestContext.Current.CancellationToken
        );
        Assert.Equal(
            "run1-done",
            await run1Done.ReceiveAsync(IntegrationTestFixture.DefaultTimeout, TestContext.Current.CancellationToken)
        );
        await ctx.Client.UnsubscribeAsync();

        // Run 2: same topic + group, but register "idcheck" as a map. The registered identity
        // disagrees with the group's frozen value-kind identity, a permanent config error. Core
        // validates identity at partition acquire (prosody core StateManagerProvider::acquire →
        // acquire_descriptor_identities, before any handler runs — Finding B's pre-handler path), so
        // the mismatched partition never acquires: the handler is never invoked and the message is
        // neither committed nor lost (no forward progress), which is exactly how a permanent config
        // error must halt rather than discard the offset.
        var run2 = await ctx.CreateSiblingClientAsync(o => o.StateCollections = [mapDef]);
        try
        {
            var handlerRan = new MessageChannel<string>();
            var run2Handler = new TestProsodyHandler<TestPayload>(
                onMessage: (_, _, _) =>
                {
                    handlerRan.Send("ran");
                    return Task.CompletedTask;
                }
            );

            await run2.SubscribeAsync(run2Handler);
            await run2.SendAsync(
                ctx.Topic,
                TopicGenerator.GenerateKey(),
                new TestPayload(),
                TestContext.Current.CancellationToken
            );

            // A successful acquire would deliver within a few seconds (every other integration test
            // does). The identity mismatch halts the partition, so no invocation ever arrives.
            await Task.Delay(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);

            Assert.False(handlerRan.TryReceive(out _), "identity mismatch must halt delivery pre-handler");
        }
        finally
        {
            if (await run2.GetConsumerStateAsync() == ConsumerState.Running)
            {
                await run2.UnsubscribeAsync();
            }

            await run2.DisposeAsync();
        }
    }

    [Fact(Timeout = 60_000)]
    public async Task RethrownPermanent_ClassifiesPermanent_NoRetry()
    {
        await using var ctx = await CreateTestContextAsync(StateTestSupport.WithAllCollections());
        var count = 0;
        var handled = new EventNotifier();

        var handler = new TestProsodyHandler<TestPayload>(
            onMessage: (_, _, _) =>
            {
                Interlocked.Increment(ref count);
                handled.Signal();
                throw new PermanentStateException("permanent state boom");
            }
        );

        await ctx.Client.SubscribeAsync(handler);
        await ctx.Client.SendAsync(
            ctx.Topic,
            TopicGenerator.GenerateKey(),
            new TestPayload(),
            TestContext.Current.CancellationToken
        );

        await handled.WaitAsync(TestContext.Current.CancellationToken);
        await Task.Delay(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);

        Assert.Equal(1, Volatile.Read(ref count));
    }

    [Fact(Timeout = 60_000)]
    public async Task RethrownTransient_ClassifiesTransient_Retries()
    {
        await using var ctx = await CreateTestContextAsync(StateTestSupport.WithAllCollections());
        var count = 0;
        var retried = new EventNotifier();

        var handler = new TestProsodyHandler<TestPayload>(
            onMessage: (_, _, _) =>
            {
                var current = Interlocked.Increment(ref count);
                if (current == 1)
                {
                    throw new TransientStateException("transient state boom");
                }

                retried.Signal();
                return Task.CompletedTask;
            }
        );

        await ctx.Client.SubscribeAsync(handler);
        await ctx.Client.SendAsync(
            ctx.Topic,
            TopicGenerator.GenerateKey(),
            new TestPayload(),
            TestContext.Current.CancellationToken
        );

        await retried.WaitAsync(TestContext.Current.CancellationToken);

        Assert.True(Volatile.Read(ref count) >= 2);
    }

    [Fact(Timeout = 60_000)]
    public async Task NullWrite_Integration_RejectsTransient_StoreUntouched()
    {
        await using var ctx = await CreateTestContextAsync(StateTestSupport.WithAllCollections());
        var v = Guid.NewGuid().ToString("N");
        var observations = new MessageChannel<NullWriteObservation>();

        var handler = new TestProsodyHandler<TestPayload>(
            onMessage: async (context, _, ct) =>
            {
                var cart = context.State(StateTestSupport.Cart);
                var deque = context.State(StateTestSupport.Backlog);
                await cart.SetAsync(new CartState { V = v }, ct);
                await cart.CommitAsync(ct);

                var valueTransient = false;
                try
                {
                    await cart.SetAsync(null!, ct);
                }
                catch (TransientStateException)
                {
                    valueTransient = true;
                }

                var dequeTransient = false;
                try
                {
                    await deque.PushBackAsync(null!, ct);
                }
                catch (TransientStateException)
                {
                    dequeTransient = true;
                }

                var got = await cart.GetAsync(ct);
                observations.Send(
                    new NullWriteObservation
                    {
                        ValueTransient = valueTransient,
                        DequeTransient = dequeTransient,
                        StoreIntact = got.HasValue && got.Value.V == v,
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
            () => Assert.True(obs.ValueTransient),
            () => Assert.True(obs.DequeTransient),
            () => Assert.True(obs.StoreIntact)
        );
    }

    [Fact(Timeout = 60_000)]
    public async Task MissingVsDefault_Integration()
    {
        await using var ctx = await CreateTestContextAsync(StateTestSupport.WithAllCollections());
        var observations = new MessageChannel<MissingVsDefaultObservation>();

        var handler = new TestProsodyHandler<TestPayload>(
            onMessage: async (context, _, ct) =>
            {
                var decMap = context.State(StateTestSupport.DecMap);
                var boolDeque = context.State(StateTestSupport.BoolDeque);

                await decMap.SetAsync("k", 0m, ct);
                await boolDeque.PushBackAsync(false, ct);

                var decStored = await decMap.GetAsync("k", ct);
                var decAbsent = await decMap.GetAsync("absent", ct);
                var boolStored = await boolDeque.GetAsync(0, ct);
                var boolAbsent = await boolDeque.GetAsync(5, ct);

                observations.Send(
                    new MissingVsDefaultObservation
                    {
                        DecStoredHasValue = decStored.HasValue,
                        DecStored = decStored.ValueOr(-1m),
                        DecAbsentHasValue = decAbsent.HasValue,
                        BoolStoredHasValue = boolStored.HasValue,
                        BoolStored = boolStored.ValueOr(true),
                        BoolAbsentHasValue = boolAbsent.HasValue,
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
            () => Assert.True(obs.DecStoredHasValue),
            () => Assert.Equal(0m, obs.DecStored),
            () => Assert.False(obs.DecAbsentHasValue),
            () => Assert.True(obs.BoolStoredHasValue),
            () => Assert.False(obs.BoolStored),
            () => Assert.False(obs.BoolAbsentHasValue)
        );
    }
}
