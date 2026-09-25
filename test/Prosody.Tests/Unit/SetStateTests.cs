using Prosody.State;
using Prosody.Tests.TestHelpers;
using Native = Prosody.Native;

namespace Prosody.Tests.Unit;

/// <summary>Verifies that each set method calls the matching native set operation.</summary>
public sealed class SetStateTests
{
    [Fact]
    public async Task EachMethodCallsTheMatchingNativeOperation()
    {
        var handle = new FakeSetStateHandle();
        var set = new SetState(handle);
        var cancellationToken = TestContext.Current.CancellationToken;

        await set.AddAsync("a", cancellationToken);
        await set.RemoveAsync("b", cancellationToken);
        var contains = await set.ContainsAsync("present", cancellationToken);
        var many = await set.ContainsManyAsync(["present", "absent"], cancellationToken);
        var isEmpty = await set.IsEmptyAsync(cancellationToken);
        await set.ClearAsync(cancellationToken);
        await set.CommitAsync(cancellationToken);
        await set.RollbackAsync(cancellationToken);

        Assert.Multiple(
            () =>
                Assert.Equal(
                    [
                        "insert:a",
                        "remove:b",
                        "contains:present",
                        "contains_many:present,absent",
                        "is_empty",
                        "clear",
                        "commit",
                        "rollback",
                    ],
                    handle.Calls
                ),
            () => Assert.True(contains),
            () => Assert.Equal([true, false], many),
            () => Assert.True(isEmpty)
        );
    }

    [Theory]
    [InlineData(true, StoreOutcome.Applied, StoreOutcome.NoOp)]
    [InlineData(false, StoreOutcome.NoOp, StoreOutcome.Applied)]
    public async Task CommitAndRollbackReturnTheNativeOutcome(
        bool commitApplied,
        StoreOutcome expectedCommit,
        StoreOutcome expectedRollback
    )
    {
        var handle = new FakeSetStateHandle
        {
            CommitOutcome = commitApplied ? Native.StoreOutcome.Applied : Native.StoreOutcome.NoOp,
            RollbackOutcome = commitApplied ? Native.StoreOutcome.NoOp : Native.StoreOutcome.Applied,
        };
        var set = new SetState(handle);
        var cancellationToken = TestContext.Current.CancellationToken;

        Assert.Equal(expectedCommit, await set.CommitAsync(cancellationToken));
        Assert.Equal(expectedRollback, await set.RollbackAsync(cancellationToken));
    }

    [Fact]
    public void EnumerationPassesTheQueryToTheNativeKeyCursor()
    {
        var byQuery = new FakeSetStateHandle();
        var byDirection = new FakeSetStateHandle();
        var query = new KeyQuery
        {
            Prefix = "tag:",
            From = "tag:b",
            Limit = 5,
        };

        Assert.Throws<NotSupportedException>(() =>
            new SetState(byQuery)
                .EnumerateAsync(query, TestContext.Current.CancellationToken)
                .GetAsyncEnumerator(TestContext.Current.CancellationToken)
        );
        Assert.Throws<NotSupportedException>(() =>
            new SetState(byDirection)
                .EnumerateAsync(ScanDirection.Backward, TestContext.Current.CancellationToken)
                .GetAsyncEnumerator(TestContext.Current.CancellationToken)
        );

        Assert.Multiple(
            () => Assert.Equal(KeyQuery.ToNative(query), byQuery.KeysQuery),
            () =>
                Assert.Equal(
                    KeyQuery.ToNative(new KeyQuery { Direction = ScanDirection.Backward }),
                    byDirection.KeysQuery
                )
        );
    }
}
