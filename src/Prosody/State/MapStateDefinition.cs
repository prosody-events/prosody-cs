namespace Prosody.State;

/// <summary>A validated string-keyed ordered-map JSON collection definition.</summary>
/// <typeparam name="TValue">The stored value type.</typeparam>
public sealed record MapStateDefinition<TValue> : StateDefinition
{
    internal MapStateDefinition(string name, TimeSpan? ttl, bool? readUncommitted, int? keysetLimit)
        : base(name, Native.StateKind.Map, Native.StatePayload.Json, ttl, readUncommitted, keysetLimit) { }
}
