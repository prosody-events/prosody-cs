namespace Prosody.State;

/// <summary>A validated deque JSON collection definition.</summary>
/// <typeparam name="T">The stored element type.</typeparam>
public sealed record DequeStateDefinition<T> : StateDefinition
{
    internal DequeStateDefinition(string name, TimeSpan? ttl, bool? readUncommitted)
        : base(name, Native.StateKind.Deque, Native.StatePayload.Json, ttl, readUncommitted, keysetLimit: null) { }
}
