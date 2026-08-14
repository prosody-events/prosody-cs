namespace Prosody.State;

/// <summary>A validated single-value JSON collection definition.</summary>
/// <typeparam name="T">The stored value type. JSON <see langword="null"/> is not storable, so <c>T</c> is <c>notnull</c>.</typeparam>
public sealed record ValueStateDefinition<T> : StateDefinition
    where T : notnull
{
    internal ValueStateDefinition(
        string name,
        TimeSpan? ttl,
        bool? readUncommitted,
        bool published,
        StateReadCache? readCache
    )
        : base(
            name,
            Native.StateKind.Value,
            Native.StatePayload.Json,
            ttl,
            readUncommitted,
            keysetLimit: null,
            capacity: null,
            published,
            readCache
        )
    { }
}
