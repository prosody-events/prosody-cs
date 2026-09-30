namespace Prosody.State;

/// <summary>A validated deque JSON collection definition.</summary>
/// <typeparam name="T">The stored element type. JSON <see langword="null"/> is not storable, so <c>T</c> is <c>notnull</c>.</typeparam>
public sealed record DequeStateDefinition<T> : StateDefinition
    where T : notnull
{
    internal DequeStateDefinition(
        string name,
        TimeSpan? ttl,
        bool? readUncommitted,
        int? capacity,
        bool published,
        StateReadCache? readCache
    )
        : base(
            name,
            new Native.StateKind.Deque(Native.StatePayload.Json, Bound(capacity)),
            ttl,
            readUncommitted,
            published,
            readCache
        ) { }
}
