namespace Prosody.State;

/// <summary>A validated string-keyed ordered-map JSON collection definition.</summary>
/// <typeparam name="TValue">The stored value type. JSON <see langword="null"/> is not storable, so <c>TValue</c> is <c>notnull</c>.</typeparam>
public sealed record MapStateDefinition<TValue> : StateDefinition
    where TValue : notnull
{
    internal MapStateDefinition(string name, TimeSpan? ttl, bool? readUncommitted, int? keysetLimit)
        : base(name, Native.StateKind.Map, Native.StatePayload.Json, ttl, readUncommitted, keysetLimit, capacity: null)
    { }
}
