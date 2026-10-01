namespace Prosody.State;

/// <summary>A validated single-value message collection definition.</summary>
/// <typeparam name="TPayload">The message payload type.</typeparam>
public sealed record MessageValueDefinition<TPayload> : StateDefinition
{
    internal MessageValueDefinition(string name, TimeSpan? ttl, bool readUncommitted)
        : base(name, new Native.StateKind.Value(Native.StatePayload.Message), ttl, readUncommitted) { }
}
