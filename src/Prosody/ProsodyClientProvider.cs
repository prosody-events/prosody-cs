using System.Diagnostics.CodeAnalysis;
using Prosody.Configuration;

namespace Prosody;

/// <summary>Owns one asynchronously constructed client for dependency injection.</summary>
public sealed class ProsodyClientProvider : IAsyncDisposable
{
    private readonly Lazy<Task<ProsodyClient>> _client;

    [RequiresUnreferencedCode(
        "Uses the JSON type information configured for the Prosody client. Configure a source-generated JsonSerializerContext for trim-safe serialization."
    )]
    [RequiresDynamicCode(
        "Uses the JSON type information configured for the Prosody client. Configure a source-generated JsonSerializerContext to avoid runtime code generation."
    )]
    internal ProsodyClientProvider(ClientOptions options)
    {
        _client = new(
            () => ProsodyClient.FromValidatedOptionsAsync(options),
            LazyThreadSafetyMode.ExecutionAndPublication
        );
    }

    /// <summary>Gets the shared client without blocking the calling thread.</summary>
    public Task<ProsodyClient> GetAsync() => _client.Value;

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (_client.IsValueCreated)
        {
            var client = await _client.Value.ConfigureAwait(false);
            await client.DisposeAsync().ConfigureAwait(false);
        }
    }
}
