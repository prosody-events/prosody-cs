using System.Text;
using Prosody.State;
using Prosody.Tests.TestHelpers;
using Native = Prosody.Native;

namespace Prosody.Tests.Unit;

/// <summary>
/// Unit tests for the chunked cursor-to-<see cref="IAsyncEnumerable{T}"/> scan adapter over a fake
/// native cursor: close-exactly-once, ready-chunk flattening, concurrent serialization, disposal
/// ordering, and pull-error wrapping.
/// </summary>
public sealed class StateScanSequenceTests
{
    private static Native.StateScanItem[] Chunk(params string[] items) =>
        [
            .. items.Select(item =>
                (Native.StateScanItem)new Native.StateScanItem.DequeJson(Encoding.UTF8.GetBytes(item))
            ),
        ];

    private static string Decode(Native.StateScanItem item) =>
        Encoding.UTF8.GetString(((Native.StateScanItem.DequeJson)item).Bytes);

    private static async Task<List<string>> DrainAsync(IAsyncEnumerable<string> sequence)
    {
        var results = new List<string>();
        await foreach (var value in sequence)
        {
            results.Add(value);
        }

        return results;
    }

    [Fact]
    public async Task Exhaustion_ClosesExactlyOnce()
    {
        var cursor = new FakeStateCursor(Chunk("a"));
        var sequence = new StateScanSequence<string>(cursor, Decode, CancellationToken.None);

        var results = await DrainAsync(sequence);

        Assert.Multiple(() => Assert.Equal(["a"], results), () => Assert.Equal(1, cursor.CloseCalls));
    }

    [Fact]
    public async Task EarlyBreak_ClosesExactlyOnce()
    {
        var cursor = new FakeStateCursor(Chunk("a", "b", "c"));
        var sequence = new StateScanSequence<string>(cursor, Decode, CancellationToken.None);

        await foreach (var value in sequence)
        {
            _ = value;
            break;
        }

        Assert.Equal(1, cursor.CloseCalls);
    }

    [Fact]
    public async Task CancellationBeforeMoveNext_ClosesOnDispose_ExactlyOnce()
    {
        // The cancellation clause of the scan-iterator contract: a cancelled scan closes the cursor exactly
        // once. MoveNextAsync observes the token and throws OperationCanceledException without
        // closing; the close happens when the enumerator is disposed (as the await foreach's finally
        // does). A native-close skip in DisposeAsync drops CloseCalls to 0 and fails this.
        using var cts = new CancellationTokenSource();
        var cursor = new FakeStateCursor(Chunk("a", "b"));
        var sequence = new StateScanSequence<string>(cursor, Decode, cts.Token);
        var enumerator = sequence.GetAsyncEnumerator(TestContext.Current.CancellationToken);

        await cts.CancelAsync();
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await enumerator.MoveNextAsync());
        await enumerator.DisposeAsync();

        Assert.Equal(1, cursor.CloseCalls);
    }

    [Fact]
    public async Task ReadyChunk_FlattensWithoutPerItemPulls()
    {
        var cursor = new FakeStateCursor(Chunk("a", "b", "c"));
        var sequence = new StateScanSequence<string>(cursor, Decode, CancellationToken.None);

        var results = await DrainAsync(sequence);

        Assert.Multiple(
            () => Assert.Equal(["a", "b", "c"], results),
            () => Assert.Equal(2, cursor.NextChunkCalls),
            () => Assert.Equal(1, cursor.CloseCalls)
        );
    }

    [Fact]
    public async Task ConcurrentMoveNext_SerializeNoDupNoLoss_MaxOnePull()
    {
        var cursor = new FakeStateCursor(Chunk("a", "b"), Chunk("c"));
        var consumed = new List<string>();
        var gate = new object();

        string Recording(Native.StateScanItem item)
        {
            var value = Decode(item);
            lock (gate)
            {
                consumed.Add(value);
            }

            return value;
        }

        var sequence = new StateScanSequence<string>(cursor, Recording, CancellationToken.None);
        var enumerator = sequence.GetAsyncEnumerator(TestContext.Current.CancellationToken);

        var moves = Enumerable
            .Range(0, 4)
            .Select(_ => Task.Run(async () => await enumerator.MoveNextAsync()))
            .ToArray();
        var outcomes = await Task.WhenAll(moves);
        await enumerator.DisposeAsync();

        Assert.Multiple(
            () => Assert.Equal(["a", "b", "c"], consumed),
            () => Assert.Equal(3, outcomes.Count(result => result)),
            () => Assert.Equal(1, cursor.MaxActivePulls)
        );
    }

    [Fact]
    public async Task DisposeQueuedBehindActiveMoveNext_ClosesOnce_NoRace()
    {
        var cursor = new FakeStateCursor(Chunk("a")) { PullRelease = new TaskCompletionSource() };
        var sequence = new StateScanSequence<string>(cursor, Decode, CancellationToken.None);
        var enumerator = sequence.GetAsyncEnumerator(TestContext.Current.CancellationToken);

        var move = enumerator.MoveNextAsync().AsTask();
        await cursor.PullStarted.Task;
        var dispose = enumerator.DisposeAsync().AsTask();

        cursor.PullRelease.SetResult();
        var moved = await move;
        await dispose;

        Assert.Multiple(
            () => Assert.True(moved),
            () => Assert.Equal(1, cursor.CloseCalls),
            () => Assert.False(cursor.CloseDuringActivePull)
        );
    }

    [Fact]
    public async Task PullError_ClosesBestEffort_WrapsToStateError_Unmasked()
    {
        var cursor = new FakeStateCursor { PullError = () => new Native.FfiException.TransientState("boom") };
        var sequence = new StateScanSequence<string>(cursor, Decode, CancellationToken.None);
        var enumerator = sequence.GetAsyncEnumerator(TestContext.Current.CancellationToken);

        var exception = await Assert.ThrowsAsync<TransientStateException>(async () => await enumerator.MoveNextAsync());

        Assert.Multiple(() => Assert.Equal("boom", exception.Message), () => Assert.Equal(1, cursor.CloseCalls));
    }
}
