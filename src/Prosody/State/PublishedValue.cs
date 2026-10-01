using System.Text.Json.Serialization.Metadata;

namespace Prosody.State;

/// <summary>Read-only access to a published value collection.</summary>
public sealed class PublishedValue<T>
    where T : notnull
{
    private readonly Native.IPublishedValueHandle _handle;
    private readonly JsonTypeInfo<T> _typeInfo;

    internal PublishedValue(Native.IPublishedValueHandle handle, JsonTypeInfo<T> typeInfo) =>
        (_handle, _typeInfo) = (handle, typeInfo);

    /// <summary>Reads the committed value for a user key.</summary>
    /// <param name="key">The user key that owns the collection.</param>
    /// <param name="cancellationToken">A token to observe before the operation dispatches.</param>
    /// <returns>The value, or an absent <see cref="StateValue{T}"/> when none is stored.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="TransientStateException">The read failed, and a retry can succeed.</exception>
    /// <exception cref="PermanentStateException">The read cannot succeed, for example after an identity mismatch.</exception>
    public Task<StateValue<T>> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        return StateInterop.ReadJsonAsync(carrier => _handle.Get(key, carrier), _typeInfo, cancellationToken);
    }
}
