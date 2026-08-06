using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Prosody.Logging;

namespace Prosody.Infrastructure;

/// <summary>
/// Cancellation sources for in-flight handler invocations, keyed by handler ID.
/// </summary>
/// <remarks>
/// Each invocation registers a fresh source and removes it when the handler
/// completes. The scheduler bounds in-flight handlers, so the map never grows
/// past that bound. Sources are never disposed or reused: a plain source holds
/// no unmanaged resources, and without reuse a late cancel can only reach an
/// abandoned source, which is harmless. <see cref="Cancel"/> queues the
/// cancellation to the thread pool because
/// <see cref="CancellationTokenSource.Cancel()"/> runs token callbacks and
/// handler continuations inline, and the caller is a native runtime thread.
/// </remarks>
internal sealed class CancellationRegistry
{
    private static ILogger Logger => ProsodyLogging.CreateLogger($"Prosody.{nameof(CancellationRegistry)}");

    private readonly ConcurrentDictionary<ulong, CancellationTokenSource> _active = new();

    /// <summary>Registers a fresh source for <paramref name="handlerId"/>.</summary>
    internal CancellationTokenSource Register(ulong handlerId)
    {
        var source = new CancellationTokenSource();
        if (!_active.TryAdd(handlerId, source))
        {
            throw new InvalidOperationException("The handler already has a cancellation source.");
        }

        return source;
    }

    /// <summary>Removes the source for <paramref name="handlerId"/>.</summary>
    internal void Complete(ulong handlerId) => _active.TryRemove(handlerId, out _);

    /// <summary>
    /// Cancels the source for <paramref name="handlerId"/> on the thread pool.
    /// A completed handler has no registered source, so a late call is a no-op.
    /// </summary>
    internal void Cancel(ulong handlerId)
    {
        if (_active.TryGetValue(handlerId, out var source))
        {
            ThreadPool.UnsafeQueueUserWorkItem(static s => CancelSafely(s), source, preferLocal: false);
        }
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
