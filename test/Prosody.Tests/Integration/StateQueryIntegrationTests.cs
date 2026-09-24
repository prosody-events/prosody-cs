using Prosody.Messaging;
using Prosody.State;
using Prosody.Tests.TestHelpers;

namespace Prosody.Tests.Integration;

/// <summary>
/// Integration tests for the native wiring of keyed-state queries, set collections, map emptiness
/// and batch presence, commit outcomes, and the handler demand. Core owns the query semantics; these
/// tests check that each C# call reaches the matching core operation.
/// </summary>
public sealed class StateQueryIntegrationTests(IntegrationTestFixture fixture) : IntegrationTestBase(fixture)
{
    private sealed record QueryObservation
    {
        public bool MapEmpty { get; init; }
        public bool[] MapPresence { get; init; } = [];
        public string[] KeyPage { get; init; } = [];
        public int[] Values { get; init; } = [];
        public string[] Entries { get; init; } = [];
        public int[] Positions { get; init; } = [];
        public bool SetEmpty { get; init; }
        public bool SetContains { get; init; }
        public bool[] SetPresence { get; init; } = [];
        public string[] MemberPage { get; init; } = [];
        public string[] MembersAfterRemove { get; init; } = [];
        public StoreOutcome[] Outcomes { get; init; } = [];
        public Demand Demand { get; init; }
    }

    private static readonly string[] Members = ["a", "b", "c", "d"];

    [Fact(Timeout = 60_000)]
    public async Task QueriesSetsAndOutcomesReachCore()
    {
        await using var ctx = await CreateTestContextAsync(StateTestSupport.WithAllCollections());
        var observations = new MessageChannel<QueryObservation>();

        var handler = new TestProsodyHandler<TestPayload>(
            onMessage: async (context, _, ct) =>
            {
                var map = context.State(StateTestSupport.Totals);
                var deque = context.State(StateTestSupport.BoundedDeque);
                var set = context.State(StateTestSupport.Tags);

                var mapEmpty = await map.IsEmptyAsync(ct);
                var setEmpty = await set.IsEmptyAsync(ct);
                for (var i = 1; i <= 5; i++)
                {
                    await map.SetAsync($"k{i}", i, ct);
                }

                await map.SetAsync("other", 9, ct);
                foreach (var member in Members)
                {
                    await set.AddAsync(member, ct);
                }

                await deque.PushBackAsync(10, ct);
                await deque.PushBackAsync(11, ct);
                await deque.PushBackAsync(12, ct);

                var memberPage = await ToListAsync(set.EnumerateAsync(new KeyQuery { After = "a", Limit = 2 }, ct));
                await set.RemoveAsync("b", ct);

                var observation = new QueryObservation
                {
                    MapEmpty = mapEmpty,
                    MapPresence = [.. await map.ContainsManyAsync(["k1", "missing", "k5"], ct)],
                    KeyPage = await ToListAsync(
                        map.EnumerateKeysAsync(
                            new KeyQuery
                            {
                                Direction = ScanDirection.Backward,
                                Prefix = "k",
                                After = "k4",
                                Limit = 2,
                            },
                            ct
                        )
                    ),
                    Values = await ToListAsync(
                        map.EnumerateValuesAsync(new KeyQuery { From = "k2", Before = "k4" }, ct)
                    ),
                    Entries = await ToListAsync(Keys(map.EnumerateAsync(new KeyQuery { Prefix = "o" }, ct))),
                    Positions = await ToListAsync(
                        deque.EnumerateAsync(new PositionQuery { Direction = ScanDirection.Backward, Range = 1.. }, ct)
                    ),
                    SetEmpty = setEmpty,
                    SetContains = await set.ContainsAsync("c", ct),
                    SetPresence = [.. await set.ContainsManyAsync(["a", "z"], ct)],
                    MemberPage = memberPage,
                    MembersAfterRemove = await ToListAsync(set.EnumerateAsync(ScanDirection.Forward, ct)),
                    Outcomes = [await set.CommitAsync(ct), await set.CommitAsync(ct), await set.RollbackAsync(ct)],
                    Demand = context.Demand,
                };
                observations.Send(observation);
            }
        );

        await ctx.Client.SubscribeAsync(handler);
        await ctx.Client.SendAsync(
            ctx.Topic,
            TopicGenerator.GenerateKey(),
            new TestPayload { Sequence = 1 },
            TestContext.Current.CancellationToken
        );
        var obs = await observations.ReceiveAsync(
            IntegrationTestFixture.DefaultTimeout,
            TestContext.Current.CancellationToken
        );

        Assert.Multiple(
            () => Assert.True(obs.MapEmpty),
            () => Assert.Equal([true, false, true], obs.MapPresence),
            () => Assert.Equal(["k3", "k2"], obs.KeyPage),
            () => Assert.Equal([2, 3], obs.Values),
            () => Assert.Equal(["other"], obs.Entries),
            () => Assert.Equal([12, 11], obs.Positions),
            () => Assert.True(obs.SetEmpty),
            () => Assert.True(obs.SetContains),
            () => Assert.Equal([true, false], obs.SetPresence),
            () => Assert.Equal(["b", "c"], obs.MemberPage),
            () => Assert.Equal(["a", "c", "d"], obs.MembersAfterRemove),
            () => Assert.Equal([StoreOutcome.Applied, StoreOutcome.NoOp, StoreOutcome.NoOp], obs.Outcomes),
            () => Assert.Equal(new Demand(DemandKind.Normal, 0), obs.Demand)
        );
    }

    [Fact(Timeout = 60_000)]
    public async Task DemandReportsTheRetryOrdinal()
    {
        await using var ctx = await CreateTestContextAsync();
        var demands = new MessageChannel<Demand>();

        var handler = new TestProsodyHandler<TestPayload>(
            onMessage: (context, _, _) =>
            {
                demands.Send(context.Demand);
                return context.Demand.Kind == DemandKind.Normal
                    ? throw new InvalidOperationException("Transient failure")
                    : Task.CompletedTask;
            }
        );

        await ctx.Client.SubscribeAsync(handler);
        await ctx.Client.SendAsync(
            ctx.Topic,
            TopicGenerator.GenerateKey(),
            new TestPayload { Sequence = 1 },
            TestContext.Current.CancellationToken
        );
        var observed = await demands.ReceiveAsync(
            2,
            IntegrationTestFixture.DefaultTimeout,
            TestContext.Current.CancellationToken
        );

        Assert.Equal([new Demand(DemandKind.Normal, 0), new Demand(DemandKind.Failure, 1)], observed);
    }

    private static async IAsyncEnumerable<string> Keys<T>(IAsyncEnumerable<KeyValuePair<string, T>> entries)
    {
        await foreach (var entry in entries.ConfigureAwait(false))
        {
            yield return entry.Key;
        }
    }

    private static async Task<T[]> ToListAsync<T>(IAsyncEnumerable<T> source)
    {
        var items = new List<T>();
        await foreach (var item in source.ConfigureAwait(false))
        {
            items.Add(item);
        }

        return [.. items];
    }
}
