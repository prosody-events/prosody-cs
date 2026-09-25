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
/// <typeparam name="TCursor">The native cursor type.</typeparam>
/// <typeparam name="TNative">The native chunk element type.</typeparam>
/// <typeparam name="T">The public element type.</typeparam>
internal sealed class StateScanSequence<TCursor, TNative, T> : IAsyncEnumerable<T>
    where TCursor : class
    where TNative : class
{
    private readonly Func<TCursor> _cursorFactory;
    private readonly Func<TCursor, Dictionary<string, string>, Task<TNative[]?>> _nextChunk;
    private readonly Func<TCursor, Task> _close;
    private readonly Func<TNative, T> _transform;
    private readonly CancellationToken _cancellationToken;

    internal StateScanSequence(
        Func<TCursor> cursorFactory,
        Func<TCursor, Dictionary<string, string>, Task<TNative[]?>> nextChunk,
        Func<TCursor, Task> close,
        Func<TNative, T> transform,
        CancellationToken cancellationToken
    )
    {
        _cursorFactory = cursorFactory;
        _nextChunk = nextChunk;
        _close = close;
        _transform = transform;
        _cancellationToken = cancellationToken;
    }

    public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default) =>
        new Enumerator(_cursorFactory(), _nextChunk, _close, _transform, _cancellationToken, cancellationToken);

    private sealed class Enumerator : IAsyncEnumerator<T>
    {
        private readonly TCursor _cursor;
        private readonly Func<TCursor, Dictionary<string, string>, Task<TNative[]?>> _nextChunk;
        private readonly Func<TCursor, Task> _close;
        private readonly Func<TNative, T> _transform;
        private readonly SemaphoreSlim _gate = new(1, 1);
        private readonly CancellationTokenSource? _linkedCts;
        private readonly CancellationToken _cancellationToken;
        private TNative[] _chunk = [];
        private int _offset;
        private bool _finished;

        internal Enumerator(
            TCursor cursor,
            Func<TCursor, Dictionary<string, string>, Task<TNative[]?>> nextChunk,
            Func<TCursor, Task> close,
            Func<TNative, T> transform,
            CancellationToken sequenceToken,
            CancellationToken enumeratorToken
        )
        {
            _cursor = cursor;
            _nextChunk = nextChunk;
            _close = close;
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

                    TNative[]? pulled;
                    try
                    {
                        pulled = await _nextChunk(_cursor, StateInterop.CreateCarrier()).ConfigureAwait(false);
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

        private ValueTask CloseQuietlyAsync() => BestEffort.RunAsync(() => _close(_cursor));

        private async ValueTask CloseOrThrowAsync()
        {
            try
            {
                await _close(_cursor).ConfigureAwait(false);
            }
            catch (Native.FfiException ex)
            {
                throw StateInterop.Translate(ex);
            }
        }
    }
}
