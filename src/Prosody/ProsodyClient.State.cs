using Prosody.State;

namespace Prosody;

// Read-only access to published state collections that another subsystem owns.
public sealed partial class ProsodyClient
{
    /// <summary>Opens a read-only published value collection from the same descriptor used by its owner.</summary>
    public async Task<PublishedValue<T>> StateAsync<T>(
        string subsystem,
        ValueStateDefinition<T> definition,
        CancellationToken cancellationToken = default
    )
        where T : notnull
    {
        ArgumentNullException.ThrowIfNull(subsystem);
        ArgumentNullException.ThrowIfNull(definition);
        var native = await NativeAsync(cancellationToken).ConfigureAwait(false);
        var handle = await StateInterop
            .RunAsync(
                () =>
                    native.PublishedValue(
                        subsystem,
                        definition.Name,
                        definition.ReadCacheTtl,
                        definition.ReadCacheDisabled
                    ),
                cancellationToken
            )
            .ConfigureAwait(false);
        return new PublishedValue<T>(handle, StateInterop.ResolveTypeInfo<T>(JsonOptions));
    }

    /// <summary>Opens a read-only published map collection from the same descriptor used by its owner.</summary>
    public async Task<PublishedMap<TValue>> StateAsync<TValue>(
        string subsystem,
        MapStateDefinition<TValue> definition,
        CancellationToken cancellationToken = default
    )
        where TValue : notnull
    {
        ArgumentNullException.ThrowIfNull(subsystem);
        ArgumentNullException.ThrowIfNull(definition);
        var native = await NativeAsync(cancellationToken).ConfigureAwait(false);
        var handle = await StateInterop
            .RunAsync(
                () =>
                    native.PublishedMap(
                        subsystem,
                        definition.Name,
                        definition.ReadCacheTtl,
                        definition.ReadCacheDisabled
                    ),
                cancellationToken
            )
            .ConfigureAwait(false);
        return new PublishedMap<TValue>(handle, StateInterop.ResolveTypeInfo<TValue>(JsonOptions));
    }

    /// <summary>Opens a read-only published deque collection from the same descriptor used by its owner.</summary>
    public async Task<PublishedDeque<T>> StateAsync<T>(
        string subsystem,
        DequeStateDefinition<T> definition,
        CancellationToken cancellationToken = default
    )
        where T : notnull
    {
        ArgumentNullException.ThrowIfNull(subsystem);
        ArgumentNullException.ThrowIfNull(definition);
        var native = await NativeAsync(cancellationToken).ConfigureAwait(false);
        var handle = await StateInterop
            .RunAsync(
                () =>
                    native.PublishedDeque(
                        subsystem,
                        definition.Name,
                        definition.ReadCacheTtl,
                        definition.ReadCacheDisabled
                    ),
                cancellationToken
            )
            .ConfigureAwait(false);
        return new PublishedDeque<T>(handle, StateInterop.ResolveTypeInfo<T>(JsonOptions));
    }
}
