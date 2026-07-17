namespace Prosody.State;

/// <summary>A validated single-value JSON collection definition.</summary>
/// <typeparam name="T">The stored value type.</typeparam>
public sealed record ValueStateDefinition<T> : StateDefinition
{
    internal ValueStateDefinition(string name, TimeSpan? ttl, bool? readUncommitted)
        : base(name, Native.StateKind.Value, Native.StatePayload.Json, ttl, readUncommitted, keysetLimit: null) { }
}
