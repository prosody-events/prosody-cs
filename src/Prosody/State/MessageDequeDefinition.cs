namespace Prosody.State;

/// <summary>A validated deque message collection definition.</summary>
/// <typeparam name="TPayload">The message payload type.</typeparam>
public sealed record MessageDequeDefinition<TPayload> : StateDefinition
{
    internal MessageDequeDefinition(string name, TimeSpan? ttl, bool? readUncommitted, int? capacity)
        : base(
            name,
            Native.StateKind.Deque,
            Native.StatePayload.Message,
            ttl,
            readUncommitted,
            keysetLimit: null,
            capacity
        ) { }
}
