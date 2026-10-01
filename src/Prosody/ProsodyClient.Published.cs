using Prosody.State;

namespace Prosody;

// This file owns the members that open read-only published state collections.

public sealed partial class ProsodyClient
{
    /// <summary>Opens a read-only published value collection from the same descriptor used by its owner.</summary>
    /// <param name="subsystem">The subsystem that publishes the collection.</param>
    /// <param name="definition">The same definition that the owner registers.</param>
    /// <param name="cancellationToken">A token to observe before the operation dispatches.</param>
    /// <returns>A read-only handle for the published collection.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="subsystem"/> is empty.</exception>
    /// <exception cref="TransientStateException">The read failed, and a retry can succeed.</exception>
    /// <exception cref="PermanentStateException">The read cannot succeed, for example after an identity mismatch.</exception>
    public async Task<PublishedValue<T>> StateAsync<T>(
        string subsystem,
        ValueStateDefinition<T> definition,
        CancellationToken cancellationToken = default
    )
        where T : notnull =>
        new(
            await OpenPublishedAsync(
                    subsystem,
                    definition,
                    static (native, subsystem, name, cache) => native.PublishedValue(subsystem, name, cache),
                    cancellationToken
                )
                .ConfigureAwait(false),
            StateInterop.ResolveTypeInfo<T>(JsonOptions)
        );

    /// <summary>Opens a read-only published map collection from the same descriptor used by its owner.</summary>
    /// <inheritdoc cref="StateAsync{T}(string, ValueStateDefinition{T}, CancellationToken)" path="/param|/returns|/exception"/>
    public async Task<PublishedMap<TValue>> StateAsync<TValue>(
        string subsystem,
        MapStateDefinition<TValue> definition,
        CancellationToken cancellationToken = default
    )
        where TValue : notnull =>
        new(
            await OpenPublishedAsync(
                    subsystem,
                    definition,
                    static (native, subsystem, name, cache) => native.PublishedMap(subsystem, name, cache),
                    cancellationToken
                )
                .ConfigureAwait(false),
            StateInterop.ResolveTypeInfo<TValue>(JsonOptions)
        );

    /// <summary>Opens a read-only published deque collection from the same descriptor used by its owner.</summary>
    /// <inheritdoc cref="StateAsync{T}(string, ValueStateDefinition{T}, CancellationToken)" path="/param|/returns|/exception"/>
    public async Task<PublishedDeque<T>> StateAsync<T>(
        string subsystem,
        DequeStateDefinition<T> definition,
        CancellationToken cancellationToken = default
    )
        where T : notnull =>
        new(
            await OpenPublishedAsync(
                    subsystem,
                    definition,
                    static (native, subsystem, name, cache) => native.PublishedDeque(subsystem, name, cache),
                    cancellationToken
                )
                .ConfigureAwait(false),
            StateInterop.ResolveTypeInfo<T>(JsonOptions)
        );

    /// <summary>Opens a read-only published set collection from the same descriptor used by its owner.</summary>
    /// <inheritdoc cref="StateAsync{T}(string, ValueStateDefinition{T}, CancellationToken)" path="/param|/returns|/exception"/>
    public async Task<PublishedSet> StateAsync(
        string subsystem,
        SetStateDefinition definition,
        CancellationToken cancellationToken = default
    ) =>
        new(
            await OpenPublishedAsync(
                    subsystem,
                    definition,
                    static (native, subsystem, name, cache) => native.PublishedSet(subsystem, name, cache),
                    cancellationToken
                )
                .ConfigureAwait(false)
        );

    /// <summary>
    /// Opens the native handle of a published collection with the read cache policy of <paramref name="definition"/>.
    /// </summary>
    private async Task<THandle> OpenPublishedAsync<THandle>(
        string subsystem,
        StateDefinition definition,
        Func<Native.ProsodyClient, string, string, Native.ReadCache?, Task<THandle>> open,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(subsystem);
        ArgumentNullException.ThrowIfNull(definition);
        var native = await NativeAsync(cancellationToken).ConfigureAwait(false);
        return await StateInterop
            .RunAsync(_ => open(native, subsystem, definition.Name, definition.ReadCache?.Policy), cancellationToken)
            .ConfigureAwait(false);
    }
}
