using Prosody.State;
using Prosody.Tests.TestHelpers;
using Native = Prosody.Native;

namespace Prosody.Tests.Unit;

/// <summary>
/// Verifies that <see cref="KeyQuery"/> and <see cref="PositionQuery"/> translate every option to the
/// native query, reject contradictory or invalid options, and reach the native handles unchanged.
/// </summary>
public sealed class StateQueryTests
{
    [Fact]
    public void KeyQuery_TranslatesEveryOption()
    {
        var inclusive = KeyQuery.ToNative(
            new KeyQuery
            {
                Direction = ScanDirection.Backward,
                Prefix = "user:",
                From = "user:9",
                To = "user:1",
                Limit = 3,
            }
        );
        var exclusive = KeyQuery.ToNative(new KeyQuery { After = "b", Before = "d" });

        Assert.Multiple(
            () =>
                Assert.Equal(
                    new Native.KeyQuery(
                        Native.ScanDirection.Backward,
                        "user:",
                        new Native.KeyEdge.Inclusive("user:9"),
                        new Native.KeyEdge.Inclusive("user:1"),
                        3
                    ),
                    inclusive
                ),
            () =>
                Assert.Equal(
                    new Native.KeyQuery(
                        Native.ScanDirection.Forward,
                        null,
                        new Native.KeyEdge.Exclusive("b"),
                        new Native.KeyEdge.Exclusive("d"),
                        null
                    ),
                    exclusive
                )
        );
    }

    [Fact]
    public void KeyQuery_RejectsBothEdgesOfAPair()
    {
        Assert.Multiple(
            () => Assert.Throws<ArgumentException>(() => KeyQuery.ToNative(new KeyQuery { From = "a", After = "a" })),
            () => Assert.Throws<ArgumentException>(() => KeyQuery.ToNative(new KeyQuery { To = "z", Before = "z" }))
        );
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Queries_RejectANonPositiveLimit(int limit)
    {
        Assert.Multiple(
            () =>
                Assert.Equal(
                    "Limit",
                    Assert.Throws<ArgumentOutOfRangeException>(() => new KeyQuery { Limit = limit }).ParamName
                ),
            () =>
                Assert.Equal(
                    "Limit",
                    Assert.Throws<ArgumentOutOfRangeException>(() => new PositionQuery { Limit = limit }).ParamName
                )
        );
    }

    [Fact]
    public void PositionQuery_TranslatesEveryOption()
    {
        var inclusive = PositionQuery.ToNative(
            new PositionQuery
            {
                Direction = ScanDirection.Backward,
                From = 9,
                To = 2,
                Range = 1..8,
                Limit = 3,
            }
        );
        var exclusive = PositionQuery.ToNative(new PositionQuery { After = 2, Before = 6 });

        Assert.Multiple(
            () =>
                Assert.Equal(
                    new Native.PositionQuery(
                        Native.ScanDirection.Backward,
                        new Native.PositionEdge.Inclusive(9),
                        new Native.PositionEdge.Inclusive(2),
                        new Native.PositionRange(1, 8),
                        3
                    ),
                    inclusive
                ),
            () =>
                Assert.Equal(
                    new Native.PositionQuery(
                        Native.ScanDirection.Forward,
                        new Native.PositionEdge.Exclusive(2),
                        new Native.PositionEdge.Exclusive(6),
                        null,
                        null
                    ),
                    exclusive
                )
        );
    }

    public static TheoryData<Range, ulong, ulong?> RangeForms =>
        new()
        {
            { 2..5, 2, 5 },
            { 3.., 3, null },
            { ..4, 0, 4 },
            { .., 0, null },
            { 4..4, 4, 4 },
        };

    [Theory]
    [MemberData(nameof(RangeForms))]
    public void PositionQuery_TranslatesRangeForms(Range range, ulong start, ulong? end)
    {
        Assert.Equal(
            new Native.PositionRange(start, end),
            PositionQuery.ToNative(new PositionQuery { Range = range }).Range
        );
    }

    public static TheoryData<Range> InvalidRanges => new() { ^3.., 1..^2, 5..2 };

    [Theory]
    [MemberData(nameof(InvalidRanges))]
    public void PositionQuery_RejectsFromEndAndDescendingRanges(Range range)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => new PositionQuery { Range = range });

        Assert.Equal("Range", exception.ParamName);
    }

    [Fact]
    public void PositionQuery_RejectsNegativePositionsAndBothEdgesOfAPair()
    {
        Assert.Multiple(
            () =>
                Assert.Equal(
                    "From",
                    Assert.Throws<ArgumentOutOfRangeException>(() => new PositionQuery { From = -1 }).ParamName
                ),
            () =>
                Assert.Equal(
                    "After",
                    Assert.Throws<ArgumentOutOfRangeException>(() => new PositionQuery { After = -1 }).ParamName
                ),
            () =>
                Assert.Equal(
                    "To",
                    Assert.Throws<ArgumentOutOfRangeException>(() => new PositionQuery { To = -1 }).ParamName
                ),
            () =>
                Assert.Equal(
                    "Before",
                    Assert.Throws<ArgumentOutOfRangeException>(() => new PositionQuery { Before = -1 }).ParamName
                ),
            () =>
                Assert.Throws<ArgumentException>(() =>
                    PositionQuery.ToNative(new PositionQuery { From = 1, After = 1 })
                ),
            () =>
                Assert.Throws<ArgumentException>(() => PositionQuery.ToNative(new PositionQuery { To = 1, Before = 1 }))
        );
    }

    [Fact]
    public void MapEnumerations_PassTheQueryToTheMatchingNativeCursor()
    {
        var query = new KeyQuery
        {
            Prefix = "p",
            After = "p1",
            Limit = 2,
        };
        var entries = new FakeMapStateHandle();
        var keys = new FakeMapStateHandle();
        var values = new FakeMapStateHandle();

        Assert.Throws<NotSupportedException>(() =>
            Map(entries)
                .EnumerateAsync(query, TestContext.Current.CancellationToken)
                .GetAsyncEnumerator(TestContext.Current.CancellationToken)
        );
        Assert.Throws<NotSupportedException>(() =>
            Map(keys)
                .EnumerateKeysAsync(query, TestContext.Current.CancellationToken)
                .GetAsyncEnumerator(TestContext.Current.CancellationToken)
        );
        Assert.Throws<NotSupportedException>(() =>
            Map(values)
                .EnumerateValuesAsync(query, TestContext.Current.CancellationToken)
                .GetAsyncEnumerator(TestContext.Current.CancellationToken)
        );

        Assert.Multiple(
            () => Assert.Equal(KeyQuery.ToNative(query), entries.EntriesQuery),
            () => Assert.Equal(KeyQuery.ToNative(query), keys.KeysQuery),
            () => Assert.Null(keys.EntriesQuery),
            () => Assert.Equal(KeyQuery.ToNative(query), values.EntriesQuery),
            () => Assert.Null(values.KeysQuery)
        );
    }

    [Fact]
    public void DirectionOverloads_SendADirectionOnlyQuery()
    {
        var map = new FakeMapStateHandle();
        var deque = new FakeDequeStateHandle();

        Assert.Throws<NotSupportedException>(() =>
            Map(map)
                .EnumerateValuesAsync(ScanDirection.Backward, TestContext.Current.CancellationToken)
                .GetAsyncEnumerator(TestContext.Current.CancellationToken)
        );
        Assert.Throws<NotSupportedException>(() =>
            new DequeState<int>(deque, TestJson.TypeInfo<int>())
                .EnumerateAsync(ScanDirection.Backward, TestContext.Current.CancellationToken)
                .GetAsyncEnumerator(TestContext.Current.CancellationToken)
        );

        Assert.Multiple(
            () =>
                Assert.Equal(
                    new Native.KeyQuery(Native.ScanDirection.Backward, null, null, null, null),
                    map.EntriesQuery
                ),
            () =>
                Assert.Equal(
                    new Native.PositionQuery(Native.ScanDirection.Backward, null, null, null, null),
                    deque.LastQuery
                )
        );
    }

    [Fact]
    public void DequeEnumeration_PassesTheQueryToTheNativeHandle()
    {
        var handle = new FakeDequeStateHandle();
        var deque = new DequeState<int>(handle, TestJson.TypeInfo<int>());
        var query = new PositionQuery
        {
            Direction = ScanDirection.Backward,
            Range = 2..,
            Limit = 4,
        };

        Assert.Throws<NotSupportedException>(() =>
            deque
                .EnumerateAsync(query, TestContext.Current.CancellationToken)
                .GetAsyncEnumerator(TestContext.Current.CancellationToken)
        );

        Assert.Equal(PositionQuery.ToNative(query), handle.LastQuery);
    }

    private static MapState<int> Map(FakeMapStateHandle handle) => new(handle, TestJson.TypeInfo<int>());
}
