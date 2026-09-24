namespace Prosody.State;

/// <summary>A validated set collection definition. A set stores ordered <see cref="string"/> members.</summary>
public sealed record SetStateDefinition : StateDefinition
{
    internal SetStateDefinition(
        string name,
        TimeSpan? ttl,
        bool? readUncommitted,
        int? keysetLimit,
        bool published,
        StateReadCache? readCache
    )
        : base(
            name,
            Native.StateKind.Set,
            Native.StatePayload.Json,
            ttl,
            readUncommitted,
            keysetLimit,
            capacity: null,
            published,
            readCache
        ) { }
}
