namespace Prosody.Infrastructure;

/// <summary>
/// Fixed pool of cancellation sources for concurrent handlers.
/// </summary>
/// <remarks>
/// The slot count equals the scheduler concurrency bound the native client
/// resolved. The Rust scheduler never runs more concurrent handler
/// invocations than that bound, so <see cref="Rent"/> always finds a free
/// slot in correct operation. The free-slot queue and handler lookup have the
/// same fixed capacity as the slot array. Outside pool methods, each slot is
/// either free or active. <see cref="Return"/> removes the handler lookup entry
/// before it adds the slot to the free-slot queue.
/// </remarks>
internal sealed class CancellationTokenSourcePool
{
    internal const int MaximumCapacity = 10_000;

    private readonly Dictionary<ulong, int> _activeSlots;
    private readonly Queue<int> _freeSlots;
#if NET9_0_OR_GREATER
    private readonly Lock _gate = new();
#else
    private readonly object _gate = new();
#endif
    private readonly PooledCts[] _slots;

    internal CancellationTokenSourcePool(int capacity)
    {
        if (capacity is < 1 or > MaximumCapacity)
        {
            throw new ArgumentOutOfRangeException(
                nameof(capacity),
                capacity,
                $"Capacity must be between 1 and {MaximumCapacity}."
            );
        }

        _activeSlots = new Dictionary<ulong, int>(capacity);
        _freeSlots = new Queue<int>(capacity);
        _slots = new PooledCts[capacity];
        for (var index = 0; index < _slots.Length; index++)
        {
            _slots[index] = new PooledCts(this, index);
            _freeSlots.Enqueue(index);
        }
    }

    /// <summary>Rents an uncancelled source for <paramref name="handlerId"/>.</summary>
    internal PooledCts Rent(ulong handlerId)
    {
        lock (_gate)
        {
            if (_activeSlots.ContainsKey(handlerId))
            {
                throw new InvalidOperationException("The handler already has a cancellation slot.");
            }

            if (!_freeSlots.TryDequeue(out var index))
            {
                throw new InvalidOperationException("The cancellation pool has no available slot.");
            }

            var slot = _slots[index];
            slot.Rent(handlerId);
            _activeSlots.Add(handlerId, index);
            return slot;
        }
    }

    /// <summary>
    /// Cancels the active source for <paramref name="handlerId"/>.
    /// A completed handler has no active source, so the call is a no-op.
    /// </summary>
    internal void Cancel(ulong handlerId)
    {
        PooledCts? slot;
        lock (_gate)
        {
            slot = _activeSlots.TryGetValue(handlerId, out var index) ? _slots[index] : null;
        }

        slot?.CancelIfCurrent(handlerId);
    }

    /// <summary>Returns a slot to the bounded free-slot queue.</summary>
    internal void Return(int index, ulong handlerId)
    {
        lock (_gate)
        {
            if (!_activeSlots.TryGetValue(handlerId, out var activeIndex) || activeIndex != index)
            {
                throw new InvalidOperationException("The cancellation slot is not active for this handler.");
            }

            _activeSlots.Remove(handlerId);
            _freeSlots.Enqueue(index);
        }
    }
}
