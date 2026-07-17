using Native = Prosody.Native;

namespace Prosody.Tests.TestHelpers;

/// <summary>
/// An in-memory fake of the internal native scan cursor for infra-free tests of the
/// <c>StateScanSequence</c> adapter. Records pull/close counts, tracks the maximum number of
/// concurrently active pulls, and can gate a pull open or fault it on demand.
/// </summary>
internal sealed class FakeStateCursor : Native.IStateCursor
{
    private readonly Queue<Native.StateScanItem[]?> _chunks;
    private readonly object _maxLock = new();
    private int _activePulls;

    internal FakeStateCursor(params Native.StateScanItem[]?[] chunks)
    {
        _chunks = new Queue<Native.StateScanItem[]?>(chunks);
    }

    /// <summary>The number of <c>NextChunk</c> calls.</summary>
    public int NextChunkCalls { get; private set; }

    /// <summary>The number of <c>Close</c> calls.</summary>
    public int CloseCalls { get; private set; }

    /// <summary>The maximum number of pulls that were active simultaneously.</summary>
    public int MaxActivePulls { get; private set; }

    /// <summary>Set <see langword="true"/> if <c>Close</c> ran while a pull was active.</summary>
    public bool CloseDuringActivePull { get; private set; }

    /// <summary>Completed when a pull begins; lets a test observe that a pull is in flight.</summary>
    public TaskCompletionSource PullStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>When set, a pull awaits this before returning, holding the gate open.</summary>
    public TaskCompletionSource? PullRelease { get; set; }

    /// <summary>When set, a pull throws this instead of returning a chunk.</summary>
    public Func<Native.FfiException>? PullError { get; set; }

    /// <summary>When set, <c>Close</c> invokes this (for example to fault the close).</summary>
    public Func<Task>? CloseBehavior { get; set; }

    /// <summary>The trace-propagation carrier of the most recent pull.</summary>
    public Dictionary<string, string>? LastCarrier { get; private set; }

    public async Task<Native.StateScanItem[]?> NextChunk(Dictionary<string, string> carrier)
    {
        NextChunkCalls++;
        LastCarrier = carrier;
        var active = Interlocked.Increment(ref _activePulls);
        lock (_maxLock)
        {
            if (active > MaxActivePulls)
            {
                MaxActivePulls = active;
            }
        }

        PullStarted.TrySetResult();
        try
        {
            if (PullRelease is not null)
            {
                await PullRelease.Task.ConfigureAwait(false);
            }

            if (PullError is not null)
            {
                throw PullError();
            }

            return _chunks.Count > 0 ? _chunks.Dequeue() : null;
        }
        finally
        {
            Interlocked.Decrement(ref _activePulls);
        }
    }

    public Task Close()
    {
        CloseCalls++;
        if (Volatile.Read(ref _activePulls) > 0)
        {
            CloseDuringActivePull = true;
        }

        return CloseBehavior?.Invoke() ?? Task.CompletedTask;
    }
}
