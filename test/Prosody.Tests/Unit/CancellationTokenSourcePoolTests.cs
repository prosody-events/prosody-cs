using Prosody.Infrastructure;

namespace Prosody.Tests.Unit;

/// <summary>
/// Tests the fixed cancellation source pool and its handler identity guard.
/// </summary>
public sealed class CancellationTokenSourcePoolTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(5);

    private static Task WhenCancelled(CancellationToken token)
    {
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        token.Register(() => cancelled.TrySetResult());
        return cancelled.Task;
    }

    [Fact]
    public async Task CancelReachesTheActiveRenter()
    {
        var pool = new CancellationTokenSourcePool(capacity: 1);
        var slot = pool.Rent(handlerId: 1);

        pool.Cancel(handlerId: 1);

        await WhenCancelled(slot.Cts.Token).WaitAsync(Deadline, TestContext.Current.CancellationToken);
        slot.Return();
    }

    [Fact]
    public async Task StaleHandlerIdCannotCancelReRentedSlot()
    {
        var pool = new CancellationTokenSourcePool(capacity: 1);
        var slot = pool.Rent(handlerId: 1);
        slot.Return();
        var rented = pool.Rent(handlerId: 2);

        Assert.Same(slot, rented);
        pool.Cancel(handlerId: 1);
        Assert.False(rented.Cts.IsCancellationRequested);

        pool.Cancel(handlerId: 2);
        await WhenCancelled(rented.Cts.Token).WaitAsync(Deadline, TestContext.Current.CancellationToken);
        rented.Return();
    }

    [Fact]
    public async Task CancelPendingSourceIsRetiredOnReturn()
    {
        var pool = new CancellationTokenSourcePool(capacity: 1);
        var slot = pool.Rent(handlerId: 1);
        var retired = slot.Cts;

        pool.Cancel(handlerId: 1);
        slot.Return();
        var rented = pool.Rent(handlerId: 2);

        // The queued cancellation lands on the retired source, never the next rental.
        await WhenCancelled(retired.Token).WaitAsync(Deadline, TestContext.Current.CancellationToken);
        Assert.NotSame(retired, rented.Cts);
        Assert.False(rented.Cts.IsCancellationRequested);
        rented.Return();
    }

    [Fact]
    public void SynchronouslyCancelledSourceIsReplacedOnReturn()
    {
        var pool = new CancellationTokenSourcePool(capacity: 1);
        var slot = pool.Rent(handlerId: 1);

        // Mirrors the rent-time probe: a direct cancel with no queued work item.
        slot.Cts.Cancel();
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
    public void ReturnedHandlerIdCanRentAgain()
    {
        var pool = new CancellationTokenSourcePool(capacity: 1);
        var first = pool.Rent(handlerId: 1);
        first.Return();

        var second = pool.Rent(handlerId: 1);

        Assert.Same(first, second);
        second.Return();
    }

    [Fact]
    public void PoolRejectsCapacityAboveTheAllocationLimit()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new CancellationTokenSourcePool(CancellationTokenSourcePool.MaximumCapacity + 1)
        );
    }
}
