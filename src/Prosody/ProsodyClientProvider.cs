using System.Diagnostics.CodeAnalysis;
using Prosody.Logging;
#if NET9_0_OR_GREATER
using ProviderLock = System.Threading.Lock;
#else
using ProviderLock = System.Object;
#endif

namespace Prosody;

/// <summary>Owns one shared client for dependency injection and retries failed construction.</summary>
public sealed class ProsodyClientProvider : IDisposable, IAsyncDisposable
{
    private readonly ProviderLock _gate = new();
    private readonly Func<Task<ProsodyClient>> _create;
    private Task<ProsodyClient>? _client;
    private bool _disposed;

    internal ProsodyClientProvider(Func<Task<ProsodyClient>> create)
    {
        _create = create;
    }

    /// <summary>Gets the shared client without blocking the calling thread.</summary>
    public async Task<ProsodyClient> GetAsync()
    {
        Task<ProsodyClient> pending;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            pending = _client ??= _create();
        }

        try
        {
            return await pending.ConfigureAwait(false);
        }
        catch
        {
            lock (_gate)
            {
                if (ReferenceEquals(_client, pending))
                {
                    _client = null;
                }
            }
            throw;
        }
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync() =>
        TryTakeClient(out var client) ? new ValueTask(DisposeClientAsync(client)) : ValueTask.CompletedTask;

    /// <inheritdoc/>
    public void Dispose()
    {
        if (TryTakeClient(out var client))
        {
            _ = DisposeClientAsync(client)
                .ContinueWith(
                    LogDisposalFailure,
                    CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default
                );
        }
    }

    private bool TryTakeClient([NotNullWhen(true)] out Task<ProsodyClient>? client)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                client = null;
                return false;
            }
            _disposed = true;
            client = _client;
            return client is not null;
        }
    }

    private static async Task DisposeClientAsync(Task<ProsodyClient> pending)
    {
        await ((Task)pending).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        if (pending.IsCompletedSuccessfully)
        {
            var client = await pending.ConfigureAwait(false);
            await client.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static void LogDisposalFailure(Task disposal)
    {
        if (disposal.Exception is { } error)
        {
            LogHelper.LogShutdownFailed(ProsodyLogging.CreateLogger(nameof(ProsodyClientProvider)), error);
        }
    }
}
