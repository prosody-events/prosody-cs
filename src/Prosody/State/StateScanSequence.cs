using Prosody.Infrastructure;

namespace Prosody.State;

/// <summary>
/// A hand-written <see cref="IAsyncEnumerable{T}"/> over a native scan cursor. It drives the cursor
/// in ready-chunks: it retains the returned vector, yields and clears individual entries, and pulls
/// again only once the retained vector is drained.
/// </summary>
/// <remarks>
/// The complete <c>MoveNextAsync</c>/<c>DisposeAsync</c> protocol is serialized by one async gate
/// protecting the retained vector, the offset, and the native pulls. Concurrent moves preserve
/// invocation order across chunk boundaries with no duplicates or loss and at most one active native
/// pull; disposal cannot race a pull; and the native cursor is closed exactly once on clean
/// exhaustion, early exit, cancellation, or failure. This is a hand-written enumerator rather than a
/// compiler <c>yield</c> iterator because that state machine is single-consumer and cannot serialize
/// the protocol the way this contract requires.
/// </remarks>
/// <typeparam name="T">The yielded element type.</typeparam>
internal sealed class StateScanSequence<T> : IAsyncEnumerable<T>
{
    private readonly Func<Native.IStateCursor>? _cursorFactory;
    private readonly Func<Task<Native.IStateCursor>>? _asyncCursorFactory;
    private readonly Func<Native.StateScanItem, T> _transform;
    private readonly CancellationToken _cancellationToken;

    internal StateScanSequence(
        Func<Native.IStateCursor> cursorFactory,
        Func<Native.StateScanItem, T> transform,
        CancellationToken cancellationToken
    )
    {
        _cursorFactory = cursorFactory;
        _transform = transform;
        _cancellationToken = cancellationToken;
    }

    internal StateScanSequence(
        Func<Task<Native.IStateCursor>> cursorFactory,
        Func<Native.StateScanItem, T> transform,
        CancellationToken cancellationToken
    )
    {
        _asyncCursorFactory = cursorFactory;
        _transform = transform;
        _cancellationToken = cancellationToken;
    }

    public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
    {
        return _cursorFactory is { } cursorFactory
            ? new Enumerator(cursorFactory(), _transform, _cancellationToken, cancellationToken)
            : new Enumerator(
                _asyncCursorFactory ?? throw new InvalidOperationException("A state scan must have a cursor factory."),
                _transform,
                _cancellationToken,
                cancellationToken
            );
    }

    private sealed class Enumerator : IAsyncEnumerator<T>
    {
        private readonly Func<Task<Native.IStateCursor>> _cursorFactory;
        private Native.IStateCursor? _cursor;
        private readonly Func<Native.StateScanItem, T> _transform;
        private readonly SemaphoreSlim _gate = new(1, 1);
        private readonly CancellationTokenSource? _linkedCts;
        private readonly CancellationToken _cancellationToken;
        private Native.StateScanItem[] _chunk = [];
        private int _offset;
        private bool _finished;

        internal Enumerator(
            Native.IStateCursor cursor,
            Func<Native.StateScanItem, T> transform,
            CancellationToken sequenceToken,
            CancellationToken enumeratorToken
        )
            : this(() => Task.FromResult(cursor), transform, sequenceToken, enumeratorToken)
        {
            _cursor = cursor;
        }

        internal Enumerator(
            Func<Task<Native.IStateCursor>> cursorFactory,
            Func<Native.StateScanItem, T> transform,
            CancellationToken sequenceToken,
            CancellationToken enumeratorToken
        )
        {
            _cursorFactory = cursorFactory;
            _transform = transform;

            if (sequenceToken.CanBeCanceled && enumeratorToken.CanBeCanceled)
            {
                _linkedCts = CancellationTokenSource.CreateLinkedTokenSource(sequenceToken, enumeratorToken);
                _cancellationToken = _linkedCts.Token;
            }
            else if (enumeratorToken.CanBeCanceled)
            {
                _cancellationToken = enumeratorToken;
            }
            else
            {
                _cancellationToken = sequenceToken;
            }
        }

        public T Current { get; private set; } = default!;

        public async ValueTask<bool> MoveNextAsync()
        {
            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                _cancellationToken.ThrowIfCancellationRequested();

                if (_finished)
                {
                    return false;
                }

                while (_offset >= _chunk.Length)
                {
                    _chunk = [];
                    _offset = 0;

                    Native.StateScanItem[]? pulled;
                    try
                    {
                        _cursor ??= await _cursorFactory().ConfigureAwait(false);
                        pulled = await _cursor.NextChunk(StateInterop.CreateCarrier()).ConfigureAwait(false);
                    }
                    catch (Native.FfiException ex)
                    {
                        _finished = true;
                        await CloseQuietlyAsync().ConfigureAwait(false);
                        throw StateInterop.Translate(ex);
                    }

                    if (pulled is null)
                    {
                        _finished = true;
                        await CloseOrThrowAsync().ConfigureAwait(false);
                        return false;
                    }

                    _chunk = pulled;
                }

                var item = _chunk[_offset];
                _chunk[_offset] = null!;
                _offset++;

                try
                {
                    Current = _transform(item);
                    return true;
                }
                catch
                {
                    // Preserve the transform failure after closing best-effort.
                    _finished = true;
                    await CloseQuietlyAsync().ConfigureAwait(false);
                    throw;
                }
            }
            finally
            {
                _gate.Release();
            }
        }

        public async ValueTask DisposeAsync()
        {
            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (!_finished)
                {
                    _finished = true;
                    _chunk = [];
                    _offset = 0;
                    await CloseOrThrowAsync().ConfigureAwait(false);
                }
            }
            finally
            {
                _gate.Release();
                _gate.Dispose();
                _linkedCts?.Dispose();
            }
        }

        private ValueTask CloseQuietlyAsync() =>
            _cursor is null ? ValueTask.CompletedTask : BestEffort.RunAsync(_cursor.Close);

        private async ValueTask CloseOrThrowAsync()
        {
            try
            {
                if (_cursor is not null)
                {
                    await _cursor.Close().ConfigureAwait(false);
                }
            }
            catch (Native.FfiException ex)
            {
                throw StateInterop.Translate(ex);
            }
        }
    }
}
