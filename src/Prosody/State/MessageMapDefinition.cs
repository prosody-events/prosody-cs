namespace Prosody.State;

/// <summary>A validated string-keyed ordered-map message collection definition.</summary>
/// <typeparam name="TPayload">The message payload type.</typeparam>
public sealed record MessageMapDefinition<TPayload> : StateDefinition
{
    internal MessageMapDefinition(string name, TimeSpan? ttl, bool? readUncommitted, int? keysetLimit)
        : base(name, Native.StateKind.Map, Native.StatePayload.Message, ttl, readUncommitted, keysetLimit) { }
}
