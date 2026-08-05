namespace Prosody.Infrastructure;

/// <summary>
/// Fixed pool of cancellation sources for concurrent handlers.
/// </summary>
/// <remarks>
/// The slot count equals the configured maximum handler concurrency. The
/// array owns every slot, so the pool has no unbounded active registry.
/// </remarks>
internal sealed class CancellationTokenSourcePool
{
    private readonly PooledCts[] _slots;

    internal CancellationTokenSourcePool(int capacity)
    {
        if (capacity < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "Capacity must be positive.");
        }

        _slots = new PooledCts[capacity];
        for (var index = 0; index < _slots.Length; index++)
        {
            _slots[index] = new PooledCts();
        }
    }

    /// <summary>Rents an uncancelled source for <paramref name="handlerId"/>.</summary>
    internal PooledCts Rent(ulong handlerId)
    {
        foreach (var slot in _slots)
        {
            if (slot.TryRent(handlerId))
            {
                return slot;
            }
        }

        throw new InvalidOperationException("The cancellation pool has no available slot.");
    }

    /// <summary>
    /// Cancels the active source for <paramref name="handlerId"/>.
    /// A completed handler has no active source, so the call is a no-op.
    /// </summary>
    internal void Cancel(ulong handlerId)
    {
        foreach (var slot in _slots)
        {
            if (slot.CancelIfCurrent(handlerId))
            {
                return;
            }
        }
    }
}
