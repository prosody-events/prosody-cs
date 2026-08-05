namespace Prosody.Infrastructure;

/// <summary>
/// Owns one reusable cancellation source for the handler pool.
/// </summary>
/// <remarks>
/// The gate protects the active handler ID and the source as one state. A
/// return invalidates the ID before it resets the source. A cancellation that
/// starts first completes before the return can reset the source.
/// </remarks>
internal sealed class PooledCts
{
    private static readonly Action<CancellationTokenSource> CancelSource = static source => source.Cancel();
#if NET9_0_OR_GREATER
    private readonly Lock _gate = new();
#else
    private readonly object _gate = new();
#endif
    private ulong _handlerId;
    private bool _rented;

    internal CancellationTokenSource Cts { get; private set; } = new();

    internal bool TryRent(ulong handlerId)
    {
        lock (_gate)
        {
            if (_rented)
            {
                return false;
            }

            _handlerId = handlerId;
            _rented = true;
            return true;
        }
    }

    internal bool CancelIfCurrent(ulong handlerId) => CancelIfCurrent(handlerId, CancelSource);

    internal bool CancelIfCurrent(ulong handlerId, Action<CancellationTokenSource> cancel)
    {
        lock (_gate)
        {
            if (!_rented || _handlerId != handlerId)
            {
                return false;
            }

            try
            {
                cancel(Cts);
            }
            catch (ObjectDisposedException)
            {
                // The source was retired after a failed reset.
            }

            return true;
        }
    }

    internal void Return() => Return(beforeLock: null);

    internal void Return(Action? beforeLock)
    {
        beforeLock?.Invoke();

        lock (_gate)
        {
            _rented = false;
            _handlerId = 0;

            if (!Cts.TryReset())
            {
                Cts.Dispose();
                Cts = new CancellationTokenSource();
            }
        }
    }
}
