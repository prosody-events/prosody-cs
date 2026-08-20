using System.Text.Json.Serialization.Metadata;

namespace Prosody.State;

/// <summary>
/// JSON-flavoured single-value state handle backed by a native value handle.
/// </summary>
/// <typeparam name="T">The stored value type.</typeparam>
internal sealed class ValueState<T> : IValueState<T>
    where T : notnull
{
    private readonly Native.JsonValueStateHandle _handle;
    private readonly JsonTypeInfo<T> _typeInfo;

    internal ValueState(Native.JsonValueStateHandle handle, JsonTypeInfo<T> typeInfo)
    {
        _handle = handle;
        _typeInfo = typeInfo;
    }

    public Task<StateValue<T>> GetAsync(CancellationToken cancellationToken = default) =>
        StateInterop.RunAsync(
            async () =>
                StateInterop.JsonToValue(
                    await _handle.Get(StateInterop.CreateCarrier()).ConfigureAwait(false),
                    _typeInfo
                ),
            cancellationToken
        );

    public Task SetAsync(T value, CancellationToken cancellationToken = default)
    {
        var bytes = StateInterop.SerializeJsonOrThrowNull(value, _typeInfo, "Use ClearAsync to delete instead.");
        return StateInterop.RunAsync(() => _handle.Set(bytes, StateInterop.CreateCarrier()), cancellationToken);
    }

    public Task ClearAsync(CancellationToken cancellationToken = default) =>
        StateInterop.RunAsync(() => _handle.Clear(StateInterop.CreateCarrier()), cancellationToken);

    public Task CommitAsync(CancellationToken cancellationToken = default) =>
        StateInterop.RunAsync(() => _handle.Commit(StateInterop.CreateCarrier()), cancellationToken);

    public Task RollbackAsync(CancellationToken cancellationToken = default) =>
        StateInterop.RunAsync(() => _handle.Rollback(StateInterop.CreateCarrier()), cancellationToken);
}
