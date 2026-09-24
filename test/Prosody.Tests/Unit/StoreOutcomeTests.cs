using Prosody.State;
using Prosody.Tests.TestHelpers;
using Native = Prosody.Native;

namespace Prosody.Tests.Unit;

/// <summary>Verifies that commit and rollback return the native store outcome on every collection.</summary>
public sealed class StoreOutcomeTests
{
    [Theory]
    [InlineData(true, StoreOutcome.Applied)]
    [InlineData(false, StoreOutcome.NoOp)]
    public async Task MapCommitAndRollbackReturnTheNativeOutcome(bool applied, StoreOutcome expected)
    {
        var native = applied ? Native.StoreOutcome.Applied : Native.StoreOutcome.NoOp;
        var handle = new FakeMapStateHandle { CommitOutcome = native, RollbackOutcome = native };
        var map = new MapState<int>(handle, TestJson.TypeInfo<int>());
        var cancellationToken = TestContext.Current.CancellationToken;

        var commit = await map.CommitAsync(cancellationToken);
        var rollback = await map.RollbackAsync(cancellationToken);

        Assert.Multiple(() => Assert.Equal(expected, commit), () => Assert.Equal(expected, rollback));
    }

    [Fact]
    public async Task ValueAndDequeOutcomesFollowTheNativeOutcome()
    {
        var value = new ValueState<int>(new FakeJsonValueStateHandle(), TestJson.TypeInfo<int>());
        var deque = new DequeState<int>(new FakeDequeStateHandle(), TestJson.TypeInfo<int>());
        var cancellationToken = TestContext.Current.CancellationToken;

        Assert.Equal(
            [StoreOutcome.Applied, StoreOutcome.NoOp, StoreOutcome.Applied, StoreOutcome.NoOp],
            [
                await value.CommitAsync(cancellationToken),
                await value.RollbackAsync(cancellationToken),
                await deque.CommitAsync(cancellationToken),
                await deque.RollbackAsync(cancellationToken),
            ]
        );
    }
}
