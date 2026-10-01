using Prosody.Tests.TestHelpers;

namespace Prosody.Tests.Integration;

/// <summary>
/// Integration test for async bridging against real Kafka and Cassandra: while
/// one handler is blocked awaiting a state op, a handler for a different key on the same partition
/// makes progress — proving a blocked state op yields the runtime rather than serializing dispatch.
/// Mirrors the JS reference C12.
/// </summary>
public sealed class StateAsyncBridgingTests(IntegrationTestFixture fixture) : IntegrationTestBase(fixture)
{
    private sealed record BObservation
    {
        public bool AStarted { get; init; }
        public bool AFinished { get; init; }
    }

    [Fact(Timeout = 60_000)]
    public async Task BlockedHandler_DoesNotBlockDifferentKeyOnSamePartition()
    {
        await using var ctx = await CreateTestContextAsync(StateTestSupport.WithAllCollections());

        // Probe: send 5 keys, collect partitions, pick two distinct keys on the same partition
        // (guaranteed with 5 keys over 4 partitions).
        var probes = new MessageChannel<(string Key, int Partition)>();
        var probeHandler = new TestProsodyHandler<TestPayload>(
            onMessage: (_, msg, _) =>
            {
                probes.Send((msg.Key, msg.Partition));
                return Task.CompletedTask;
            }
        );
        await ctx.Client.SubscribeAsync(probeHandler);
        var probeKeys = Enumerable.Range(0, 5).Select(i => $"probe-{Guid.NewGuid():N}-{i}").ToArray();
        foreach (var probeKey in probeKeys)
        {
            await ctx.Client.SendAsync(ctx.Topic, probeKey, new TestPayload(), TestContext.Current.CancellationToken);
        }

        var collected = await probes.ReceiveAsync(
            5,
            IntegrationTestFixture.DefaultTimeout,
            TestContext.Current.CancellationToken
        );
        await ctx.Client.UnsubscribeAsync();

        var seen = new Dictionary<int, string>();
        string? keyA = null;
        string? keyB = null;
        foreach (var (key, partition) in collected)
        {
            if (seen.TryGetValue(partition, out var first))
            {
                keyA = first;
                keyB = key;
                break;
            }

            seen[partition] = key;
        }

        Assert.NotNull(keyA);
        Assert.NotNull(keyB);

        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var aStarted = false;
        var aFinished = false;
        var aBlocked = new EventNotifier();
        var aDone = new EventNotifier();
        var bDone = new MessageChannel<BObservation>();

        var handler = new TestProsodyHandler<TestPayload>(
            onMessage: async (context, msg, ct) =>
            {
                if (msg.Key == keyA)
                {
                    // Do a real state op first (proves the op path yields), then block on the gate.
                    await context.State(StateTestSupport.Cart).GetAsync(ct);
                    aStarted = true;
                    aBlocked.Signal();
                    try
                    {
                        await gate.Task;
                    }
                    finally
                    {
                        aFinished = true;
                    }

                    aDone.Signal();
                    return;
                }

                if (msg.Key == keyB)
                {
                    bDone.Send(new BObservation { AStarted = aStarted, AFinished = aFinished });
                }
            }
        );

        await ctx.Client.SubscribeAsync(handler);
        try
        {
            await ctx.Client.SendAsync(
                ctx.Topic,
                keyA,
                new TestPayload { Sequence = 1 },
                TestContext.Current.CancellationToken
            );
            await aBlocked.WaitAsync(TestContext.Current.CancellationToken);
            await ctx.Client.SendAsync(
                ctx.Topic,
                keyB,
                new TestPayload { Sequence = 2 },
                TestContext.Current.CancellationToken
            );

            var bInfo = await bDone.ReceiveAsync(
                IntegrationTestFixture.DefaultTimeout,
                TestContext.Current.CancellationToken
            );

            // Serialized dispatch would make B wait for A → aFinished would be true.
            Assert.Multiple(() => Assert.True(bInfo.AStarted), () => Assert.False(bInfo.AFinished));
        }
        finally
        {
            gate.TrySetResult();
        }

        await aDone.WaitAsync(TestContext.Current.CancellationToken);
    }
}
