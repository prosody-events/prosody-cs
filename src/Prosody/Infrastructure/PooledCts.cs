using Microsoft.Extensions.Logging;
using Prosody.Logging;

namespace Prosody.Infrastructure;

/// <summary>
/// Owns one reusable cancellation source for the handler pool.
/// </summary>
/// <remarks>
/// The gate protects the active handler ID, the source, and the cancel-pending
/// flag as one state. <see cref="CancellationTokenSource.Cancel()"/> runs token
/// callbacks and handler continuations inline, so a cancellation never executes
/// under the gate or on the caller's thread — <see cref="CancelIfCurrent"/>
/// marks the source cancel-pending and queues the cancellation to the thread
/// pool. <see cref="Return"/> retires a cancel-pending source instead of
/// resetting it, so a late cancellation can never reach the next renter.
/// </remarks>
internal sealed class PooledCts
{
    private static ILogger Logger => ProsodyLogging.CreateLogger($"Prosody.{nameof(PooledCts)}");

    private readonly int _index;
    private readonly CancellationTokenSourcePool _owner;
#if NET9_0_OR_GREATER
    private readonly Lock _gate = new();
#else
    private readonly object _gate = new();
#endif
    private ulong _handlerId;
    private bool _rented;
    private bool _cancelPending;

    internal CancellationTokenSource Cts { get; private set; } = new();

    internal PooledCts(CancellationTokenSourcePool owner, int index)
    {
        _owner = owner;
        _index = index;
    }

    internal void Rent(ulong handlerId)
    {
        lock (_gate)
        {
            if (_rented)
            {
                throw new InvalidOperationException("The cancellation slot is already rented.");
            }

            _handlerId = handlerId;
            _rented = true;
        }
    }

    /// <summary>
    /// Queues cancellation of the active source when <paramref name="handlerId"/>
    /// is the current renter. The cancellation itself runs on the thread pool.
    /// </summary>
    internal bool CancelIfCurrent(ulong handlerId)
    {
        CancellationTokenSource source;
        lock (_gate)
        {
            if (!_rented || _handlerId != handlerId)
            {
                return false;
            }

            _cancelPending = true;
            source = Cts;
        }

        ThreadPool.UnsafeQueueUserWorkItem(static s => CancelSafely(s), source, preferLocal: false);
        return true;
    }

    internal void Return()
    {
        ulong handlerId;
        lock (_gate)
        {
            if (!_rented)
            {
                throw new InvalidOperationException("The cancellation slot is not rented.");
            }

            handlerId = _handlerId;
            _rented = false;
            _handlerId = 0;

            if (_cancelPending)
            {
                // A queued cancellation may still run on the old source, and
                // Dispose is not safe concurrently with Cancel. Abandon the old
                // source to the GC; a plain source holds no unmanaged resources.
                _cancelPending = false;
                Cts = new CancellationTokenSource();
            }
            else if (!Cts.TryReset())
            {
                // The rent-time probe cancelled the source synchronously. No
                // queued cancellation exists, so Dispose is safe.
                Cts.Dispose();
                Cts = new CancellationTokenSource();
            }
        }

        _owner.Return(_index, handlerId);
    }

    private static void CancelSafely(CancellationTokenSource source)
    {
        try
        {
            source.Cancel();
        }
#pragma warning disable CA1031 // Thread-pool work item: a fault here would crash the process.
        catch (Exception ex)
        {
            LogHelper.LogCancellationCallbackFault(Logger, ex);
        }
#pragma warning restore CA1031
    }
}
