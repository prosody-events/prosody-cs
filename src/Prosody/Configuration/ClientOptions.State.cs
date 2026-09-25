using Prosody.State;

namespace Prosody.Configuration;

// This file owns the keyed-state options.

public sealed partial class ClientOptions
{
    /// <summary>
    /// The keyed-state collections to register, declared with <see cref="StateDefinition"/> factories.
    /// </summary>
    /// <remarks>
    /// Set programmatically only — not bindable from
    /// <see cref="Microsoft.Extensions.Configuration.IConfiguration"/>. Prefer
    /// <see cref="ProsodyClientBuilder.WithStateCollections"/>. Prosody validates collection names,
    /// identities, and semantic limits when the client is built.
    /// </remarks>
    public StateDefinition[]? StateCollections { get; set; }

    /// <summary>
    /// Disk workspace for the local keyed-state cache. Each live client needs its own directory.
    /// Falls back to <c>PROSODY_STATE_CACHE_DIR</c>, then a per-client temporary directory.
    /// Must not be an empty string when set.
    /// </summary>
    public string? StateCacheDir { get; set; }

    /// <summary>
    /// Capacity of the owning keyed-state cache, such as <c>64 MiB</c>.
    /// Uses <c>PROSODY_STATE_OWNED_CACHE_SIZE</c> when omitted.
    /// Otherwise, the storage engine selects its default.
    /// </summary>
    public string? StateOwnedCacheSize { get; set; }

    /// <summary>
    /// Capacity of the published-state read cache, such as <c>1 MiB</c>.
    /// Uses <c>PROSODY_STATE_READ_CACHE_SIZE</c> when omitted.
    /// It then uses the owned cache size when set, or 1 MiB when both sizes are unset.
    /// </summary>
    public string? StateReadCacheSize { get; set; }

    /// <summary>
    /// Default cache policy for published-state reads.
    /// Uses <c>PROSODY_STATE_READ_CACHE_TTL</c> when omitted, then 5 seconds.
    /// </summary>
    public StateReadCache? StateReadCache { get; set; }

    /// <summary>
    /// Subsystem under which published JSON collections are advertised.
    /// Uses <c>PROSODY_SUBSYSTEM</c> when omitted. Published collections require it.
    /// </summary>
    public string? Subsystem { get; set; }
}
