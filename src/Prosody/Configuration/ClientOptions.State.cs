using Prosody.State;

namespace Prosody.Configuration;

/// <summary>Defines keyed-state settings for <see cref="ClientOptions"/>.</summary>
public sealed partial class ClientOptions
{
    /// <summary>The keyed-state collections to register.</summary>
    public StateDefinition[]? StateCollections { get; set; }

    /// <summary>Disk workspace for the local keyed-state cache.</summary>
    public string? StateCacheDir { get; set; }

    /// <summary>Capacity of the owning keyed-state cache.</summary>
    public string? StateOwnedCacheSize { get; set; }

    /// <summary>Capacity of the published-state read cache.</summary>
    public string? StateReadCacheSize { get; set; }

    /// <summary>Default cache policy for published-state reads.</summary>
    public StateReadCache? StateReadCache { get; set; }

    /// <summary>Subsystem for published JSON collections.</summary>
    public string? Subsystem { get; set; }

    /// <summary>Delay between provisional state and its recovery sweep.</summary>
    public TimeSpan? StateRecoveryDelay { get; set; }
}
