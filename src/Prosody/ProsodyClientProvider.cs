namespace Prosody;

/// <summary>Compatibility adapter over the shared <see cref="ProsodyClient"/>.</summary>
/// <remarks>
/// <c>AddProsodyClient</c> registers this type against the same singleton <see cref="ProsodyClient"/>,
/// so existing <c>GetRequiredService&lt;ProsodyClientProvider&gt;()</c> calls keep resolving. New code
/// injects <see cref="ProsodyClient"/> and calls operations directly.
/// </remarks>
[Obsolete("Inject ProsodyClient instead. Every operation awaits the connect under its own cancellation token.")]
public sealed class ProsodyClientProvider : IDisposable, IAsyncDisposable
{
    private readonly ProsodyClient _client;

    internal ProsodyClientProvider(ProsodyClient client)
    {
        _client = client;
    }

    /// <summary>Connects the shared client if needed and returns it.</summary>
    public async Task<ProsodyClient> GetAsync()
    {
        await _client.ConnectAsync().ConfigureAwait(false);
        return _client;
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync() => _client.DisposeAsync();

    /// <inheritdoc/>
    public void Dispose() => _client.Dispose();
}
