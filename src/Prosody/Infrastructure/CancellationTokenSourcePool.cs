using System.Collections.Concurrent;

namespace Prosody.Infrastructure;

/// <summary>
/// Bounded pool of reusable <see cref="CancellationTokenSource"/> slots.
/// </summary>
/// <remarks>
/// Invariant: at most <c>capacity</c> slots are pooled. The dispose branch in
/// <see cref="Return"/> and the retire loop in <see cref="Rent"/> are the
/// pool's removal paths.
/// </remarks>
internal sealed class CancellationTokenSourcePool(int capacity)
{
    private readonly ConcurrentQueue<PooledCts> _pool = new();
    private int _pooledCount;

    /// <summary>Rents a slot with an uncancelled source and a fresh epoch.</summary>
    internal PooledCts Rent()
    {
        while (_pool.TryDequeue(out var slot))
        {
            Interlocked.Decrement(ref _pooledCount);
            Interlocked.Increment(ref slot.Epoch);
            if (!slot.Cts.IsCancellationRequested)
            {
                return slot;
            }

            // A stale cancel fired while the slot was pooled. Retire the slot
            // so the next handler does not start with a cancelled token.
            slot.Dispose();
        }

        var fresh = new PooledCts();
        Interlocked.Increment(ref fresh.Epoch);
        return fresh;
    }

    /// <summary>
    /// Returns a slot to the pool. Disposes the slot instead when its source
    /// cannot reset or the pool is full.
    /// </summary>
    internal void Return(PooledCts slot)
    {
        if (slot.Cts.TryReset())
        {
            if (Interlocked.Increment(ref _pooledCount) <= capacity)
            {
                _pool.Enqueue(slot);
                return;
            }

            Interlocked.Decrement(ref _pooledCount);
        }

        slot.Dispose();
    }
}
