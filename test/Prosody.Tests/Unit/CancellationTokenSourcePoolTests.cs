using CsCheck;
using Prosody.Infrastructure;

namespace Prosody.Tests.Unit;

/// <summary>
/// Unit tests for <see cref="CancellationTokenSourcePool"/> and the
/// <see cref="PooledCts"/> epoch guard.
/// </summary>
public sealed class CancellationTokenSourcePoolTests
{
    [Fact]
    public void StaleEpochCannotCancelReRentedSlot()
    {
        var pool = new CancellationTokenSourcePool(capacity: 4);
        var slot = pool.Rent();
        int stale = slot.Epoch;
        pool.Return(slot);
        var rented = pool.Rent();

        Assert.Same(slot, rented);
        slot.CancelIfCurrent(stale);
        Assert.False(rented.Cts.IsCancellationRequested);

        // The current epoch does cancel — the guard blocks stale pushes only.
        rented.CancelIfCurrent(rented.Epoch);
        Assert.True(rented.Cts.IsCancellationRequested);
    }

    [Fact]
    public void CancelledSourceIsDisposedNotPooled()
    {
        var pool = new CancellationTokenSourcePool(capacity: 4);
        var slot = pool.Rent();
        slot.CancelIfCurrent(slot.Epoch);

        pool.Return(slot);

        Assert.Throws<ObjectDisposedException>(() => slot.Cts.Token);
        Assert.NotSame(slot, pool.Rent());

        // A stale push against the retired slot must not throw.
        slot.CancelIfCurrent(slot.Epoch);
    }

    [Fact]
    public void StaleCancelWhilePooledDoesNotLeakACancelledToken()
    {
        var pool = new CancellationTokenSourcePool(capacity: 4);
        var slot = pool.Rent();
        int stale = slot.Epoch;
        pool.Return(slot);

        // The Rust teardown race can push a cancel while the slot sits in the
        // pool. The next rental must still start uncancelled.
        slot.CancelIfCurrent(stale);
        var rented = pool.Rent();

        Assert.False(rented.Cts.IsCancellationRequested);
        Assert.NotSame(slot, rented);
    }

    [Fact]
    public void PoolRetainsAtMostCapacity() =>
        Gen.Int[1, 8]
            .Select(Gen.Int[1, 8])
            .Sample(
                (capacity, extra) =>
                {
                    var pool = new CancellationTokenSourcePool(capacity);
                    PooledCts[] first = [.. Enumerable.Range(0, capacity + extra).Select(_ => pool.Rent())];
                    foreach (var slot in first)
                    {
                        pool.Return(slot);
                    }

                    PooledCts[] second = [.. Enumerable.Range(0, capacity + extra).Select(_ => pool.Rent())];

                    Assert.Equal(capacity, second.Count(first.Contains));
                    foreach (var disposed in first.Except(second))
                    {
                        Assert.Throws<ObjectDisposedException>(() => disposed.Cts.Token);
                    }
                }
            );

    [Fact]
    public void RentedSourceIsNeverCancelledUnderAnyOpSequence() =>
        Gen.Int[1, 4]
            .Select(Gen.Int[0, 3].List[1, 40])
            .Sample(
                (capacity, ops) =>
                {
                    var pool = new CancellationTokenSourcePool(capacity);
                    var outstanding = new List<(PooledCts Slot, int Epoch)>();
                    foreach (int op in ops)
                    {
                        if (op == 0 || outstanding.Count == 0)
                        {
                            var slot = pool.Rent();
                            Assert.False(slot.Cts.IsCancellationRequested);
                            outstanding.Add((slot, slot.Epoch));
                            continue;
                        }

                        (PooledCts slot2, int epoch) = outstanding[0];
                        outstanding.RemoveAt(0);
                        switch (op)
                        {
                            case 1: // plain return
                                pool.Return(slot2);
                                break;
                            case 2: // cancel mid-handler, then return
                                slot2.CancelIfCurrent(epoch);
                                pool.Return(slot2);
                                break;
                            default: // return, then a stale push lands
                                pool.Return(slot2);
                                slot2.CancelIfCurrent(epoch);
                                break;
                        }
                    }
                }
            );
}
