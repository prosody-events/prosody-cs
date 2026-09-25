using Prosody.State;

namespace Prosody;

// This file owns the members that open read-only published state collections.

public sealed partial class ProsodyClient
{
    /// <summary>Opens a read-only published value collection from the same descriptor used by its owner.</summary>
    public async Task<PublishedValue<T>> StateAsync<T>(
        string subsystem,
        ValueStateDefinition<T> definition,
        CancellationToken cancellationToken = default
    )
        where T : notnull =>
        new(
            await OpenPublishedAsync(subsystem, definition, _native.PublishedValue, cancellationToken)
                .ConfigureAwait(false),
            StateInterop.ResolveTypeInfo<T>(JsonOptions)
        );

    /// <summary>Opens a read-only published map collection from the same descriptor used by its owner.</summary>
    public async Task<PublishedMap<TValue>> StateAsync<TValue>(
        string subsystem,
        MapStateDefinition<TValue> definition,
        CancellationToken cancellationToken = default
    )
        where TValue : notnull =>
        new(
            await OpenPublishedAsync(subsystem, definition, _native.PublishedMap, cancellationToken)
                .ConfigureAwait(false),
            StateInterop.ResolveTypeInfo<TValue>(JsonOptions)
        );

    /// <summary>Opens a read-only published deque collection from the same descriptor used by its owner.</summary>
    public async Task<PublishedDeque<T>> StateAsync<T>(
        string subsystem,
        DequeStateDefinition<T> definition,
        CancellationToken cancellationToken = default
    )
        where T : notnull =>
        new(
            await OpenPublishedAsync(subsystem, definition, _native.PublishedDeque, cancellationToken)
                .ConfigureAwait(false),
            StateInterop.ResolveTypeInfo<T>(JsonOptions)
        );

    /// <summary>Opens a read-only published set collection from the same descriptor used by its owner.</summary>
    public async Task<PublishedSet> StateAsync(
        string subsystem,
        SetStateDefinition definition,
        CancellationToken cancellationToken = default
    ) =>
        new(
            await OpenPublishedAsync(subsystem, definition, _native.PublishedSet, cancellationToken)
                .ConfigureAwait(false)
        );

    /// <summary>
    /// Opens the native handle of a published collection with the read cache policy of <paramref name="definition"/>.
    /// </summary>
    private static Task<THandle> OpenPublishedAsync<THandle>(
        string subsystem,
        StateDefinition definition,
        Func<string, string, TimeSpan?, bool, Task<THandle>> open,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(subsystem);
        ArgumentNullException.ThrowIfNull(definition);
        return StateInterop.RunAsync(
            () => open(subsystem, definition.Name, definition.ReadCacheTtl, definition.ReadCacheDisabled),
            cancellationToken
        );
    }
}
