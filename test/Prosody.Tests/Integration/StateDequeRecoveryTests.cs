using Prosody.Messaging;
using Prosody.State;
using Prosody.Tests.TestHelpers;

namespace Prosody.Tests.Integration;

/// <summary>
/// Integration tests for deque items that round-trip across a consumer restart. A fresh client reads
/// back a bare scalar and a bare array, so each item travels the full serialize, store, recover, and
/// deserialize path. A read in the writer's session never decodes the stored item again. Core owns the
/// codec that accepts a bare <c>42</c> or <c>[1,2,3]</c>.
/// </summary>
public sealed class StateDequeRecoveryTests(IntegrationTestFixture fixture) : IntegrationTestBase(fixture)
{
    /// <summary>
    /// Run 1 pushes and commits scalar and array items, then the writer unsubscribes. Run 2 is a
    /// sibling client with empty memory, so it must read and deserialize the stored items.
    /// </summary>
    /// <remarks>
    /// To prove this test can fail, make the scan projection in <c>DequeState.EnumerateAsync</c>
    /// return <c>default!</c>. Run 2 then reads the wrong items.
    /// </remarks>
    [Fact(Timeout = 60_000)]
    public async Task Deque_TopLevelScalarAndArrayItems_RoundTripViaColdRecovery()
    {
        await using var ctx = await CreateTestContextAsync(StateTestSupport.WithAllCollections());
        var key = TopicGenerator.GenerateKey();
        var cancellationToken = TestContext.Current.CancellationToken;

        var written = new MessageChannel<string>();
        await ctx.Client.SubscribeAsync(
            new TestProsodyHandler<TestPayload>(
                onMessage: async (context, _, ct) =>
                {
                    var scalars = context.State(StateTestSupport.ScalarDeque);
                    await scalars.PushBackAsync(42, ct);
                    await scalars.PushBackAsync(7, ct);
                    await scalars.PushBackAsync(13, ct);
                    await scalars.CommitAsync(ct);
                    var arrays = context.State(StateTestSupport.ArrayDeque);
                    await arrays.PushBackAsync([1, 2, 3], ct);
                    await arrays.PushBackAsync([4, 5], ct);
                    await arrays.CommitAsync(ct);
                    written.Send("written");
                }
            )
        );
        await ctx.Client.SendAsync(ctx.Topic, key, new TestPayload { Sequence = 1 }, cancellationToken);
        Assert.Equal("written", await written.ReceiveAsync(IntegrationTestFixture.DefaultTimeout, cancellationToken));
        await ctx.Client.UnsubscribeAsync();

        var readBack = new MessageChannel<(int[] Scalars, int[][] Arrays)>();
        await using var reader = await ctx.CreateSiblingClientAsync(StateTestSupport.WithAllCollections());
        await reader.SubscribeAsync(
            new TestProsodyHandler<TestPayload>(
                onMessage: async (context, _, ct) =>
                {
                    var scalars = new List<int>();
                    await foreach (
                        var item in context
                            .State(StateTestSupport.ScalarDeque)
                            .EnumerateAsync(ScanDirection.Forward, ct)
                    )
                    {
                        scalars.Add(item);
                    }

                    var arrays = new List<int[]>();
                    await foreach (
                        var item in context.State(StateTestSupport.ArrayDeque).EnumerateAsync(ScanDirection.Forward, ct)
                    )
                    {
                        arrays.Add(item);
                    }

                    readBack.Send(([.. scalars], [.. arrays]));
                }
            )
        );
        await reader.SendAsync(ctx.Topic, key, new TestPayload { Sequence = 2 }, cancellationToken);

        var (gotScalars, gotArrays) = await readBack.ReceiveAsync(
            IntegrationTestFixture.DefaultTimeout,
            cancellationToken
        );

        Assert.Multiple(
            () => Assert.Equal([42, 7, 13], gotScalars),
            () =>
                Assert.Equal(
                    (int[][])
                        [
                            [1, 2, 3],
                            [4, 5],
                        ],
                    gotArrays
                )
        );
    }
}
