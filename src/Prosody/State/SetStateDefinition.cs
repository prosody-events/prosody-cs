namespace Prosody.State;

/// <summary>A validated set collection definition. A set stores ordered <see cref="string"/> members.</summary>
public sealed record SetStateDefinition : StateDefinition
{
    internal SetStateDefinition(
        string name,
        TimeSpan? ttl,
        bool readUncommitted,
        int? keysetLimit,
        bool published,
        StateReadCache? readCache
    )
        : base(name, new Native.StateKind.Set(Bound(keysetLimit)), ttl, readUncommitted, published, readCache) { }
}
