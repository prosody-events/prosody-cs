using Prosody.Infrastructure;

namespace Prosody.Tests.Unit;

/// <summary>
/// Tests the fixed cancellation source pool and its handler identity guard.
/// </summary>
public sealed class CancellationTokenSourcePoolTests
{
    [Fact]
    public void StaleHandlerIdCannotCancelReRentedSlot()
    {
        var pool = new CancellationTokenSourcePool(capacity: 1);
        var slot = pool.Rent(handlerId: 1);
        slot.Return();
        var rented = pool.Rent(handlerId: 2);

        Assert.Same(slot, rented);
        pool.Cancel(handlerId: 1);
        Assert.False(rented.Cts.IsCancellationRequested);

        pool.Cancel(handlerId: 2);
        Assert.True(rented.Cts.IsCancellationRequested);
        rented.Return();
    }

    [Fact]
    public void CancelledSourceIsReplacedBeforeNextRental()
    {
        var pool = new CancellationTokenSourcePool(capacity: 1);
        var slot = pool.Rent(handlerId: 1);
        pool.Cancel(handlerId: 1);
        slot.Return();

        var rented = pool.Rent(handlerId: 2);

        Assert.False(rented.Cts.IsCancellationRequested);
        rented.Return();
    }

    [Fact]
    public void PoolRetainsConfiguredCapacity()
    {
        var pool = new CancellationTokenSourcePool(capacity: 2);
        var first = pool.Rent(handlerId: 1);
        var second = pool.Rent(handlerId: 2);

        Assert.Throws<InvalidOperationException>(() => pool.Rent(handlerId: 3));

        first.Return();
        second.Return();
        var reused = pool.Rent(handlerId: 3);

        Assert.Contains(reused, new[] { first, second });
        reused.Return();
    }

    [Fact]
    public async Task ReturnWaitsForCancellationBeforeReset()
    {
        var pool = new CancellationTokenSourcePool(capacity: 1);
        var slot = pool.Rent(handlerId: 1);
        var cancellationEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseCancellation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var cancelTask = Task.Run(
            () =>
                slot.CancelIfCurrent(
                    handlerId: 1,
                    cancel: _ =>
                    {
                        cancellationEntered.TrySetResult();
                        releaseCancellation.Task.GetAwaiter().GetResult();
                    }
                ),
            TestContext.Current.CancellationToken
        );

        await cancellationEntered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        var returnStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var returnTask = Task.Run(
            () => slot.Return(() => returnStarted.TrySetResult()),
            TestContext.Current.CancellationToken
        );

        try
        {
            await returnStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.False(returnTask.IsCompleted);
        }
        finally
        {
            releaseCancellation.TrySetResult();
            await Task.WhenAll(cancelTask, returnTask);
        }

        var rented = pool.Rent(handlerId: 2);
        Assert.False(rented.Cts.IsCancellationRequested);
        rented.Return();
    }
}
