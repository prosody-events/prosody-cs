using System.Text.Json.Serialization.Metadata;

namespace Prosody.State;

/// <summary>Read-only access to a published value collection.</summary>
public sealed class PublishedValue<T>
    where T : notnull
{
    private readonly Native.PublishedValueHandle _handle;
    private readonly JsonTypeInfo<T> _typeInfo;

    internal PublishedValue(Native.PublishedValueHandle handle, JsonTypeInfo<T> typeInfo) =>
        (_handle, _typeInfo) = (handle, typeInfo);

    /// <summary>Reads the committed value for a user key.</summary>
    public Task<StateValue<T>> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        return StateInterop.RunAsync(
            async () =>
                StateInterop.JsonToValue(
                    await _handle.Get(key, StateInterop.CreateCarrier()).ConfigureAwait(false),
                    _typeInfo
                ),
            cancellationToken
        );
    }
}
