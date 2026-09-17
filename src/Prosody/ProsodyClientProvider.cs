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
    public Task<ProsodyClient> GetAsync() => GetAsync(CancellationToken.None);

    /// <summary>Connects the shared client if needed and returns it.</summary>
    /// <remarks>
    /// This is an overload, not a defaulted parameter. A defaulted parameter would break every
    /// <c>Func&lt;Task&lt;ProsodyClient&gt;&gt;</c> built from the <c>GetAsync</c> method group.
    /// </remarks>
    public async Task<ProsodyClient> GetAsync(CancellationToken cancellationToken)
    {
        await _client.ConnectAsync(cancellationToken).ConfigureAwait(false);
        return _client;
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync() => _client.DisposeAsync();

    /// <inheritdoc/>
    public void Dispose() => _client.Dispose();
}
