namespace Prosody.Infrastructure;

/// <summary>
/// A pooled <see cref="CancellationTokenSource"/> slot with a rental epoch.
/// </summary>
/// <remarks>
/// Invariant: <see cref="Epoch"/> advances on every rental, so a cancel
/// callback captured for an earlier rental can never cancel a later one.
/// Cancel through <see cref="CancelIfCurrent"/>, never through
/// <see cref="Cts"/> directly. <see cref="CancellationTokenSourcePool"/>
/// owns disposal; a retired slot is never reused.
/// </remarks>
internal sealed class PooledCts : IDisposable
{
    internal CancellationTokenSource Cts = new();
    internal int Epoch;

    /// <summary>
    /// Cancels the source when <paramref name="epoch"/> is still the current
    /// rental. A stale epoch or a retired source is a no-op.
    /// </summary>
    internal void CancelIfCurrent(int epoch)
    {
        if (Volatile.Read(ref Epoch) != epoch)
        {
            return;
        }

        try
        {
            Cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The slot was retired between the epoch check and Cancel.
        }
    }

    public void Dispose() => Cts.Dispose();
}
