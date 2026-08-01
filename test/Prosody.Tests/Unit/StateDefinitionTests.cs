using Prosody.State;
using Native = Prosody.Native;

namespace Prosody.Tests.Unit;

/// <summary>
/// Unit tests for <see cref="StateDefinition"/> host-value validation and native mapping.
/// </summary>
public sealed class StateDefinitionTests
{
    [Fact]
    public void Value_ValidName_Constructs()
    {
        var definition = StateDefinition.Value<int>("counter");
        Assert.Equal("counter", definition.Name);
    }

    [Fact]
    public void Keyset_Zero_Ok()
    {
        var definition = StateDefinition.Map<int>("m", keysetLimit: 0);
        Assert.Equal("m", definition.Name);
    }

    [Fact]
    public void Keyset_Negative_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => StateDefinition.Map<int>("m", keysetLimit: -1));
    }

    [Fact]
    public void ToNative_Value_MapsKindAndPayload()
    {
        var native = StateDefinition.Value<int>("v", ttl: TimeSpan.FromSeconds(2)).ToNative();

        Assert.Multiple(
            () => Assert.Equal("v", native.Name),
            () => Assert.Equal(Native.StateKind.Value, native.Kind),
            () => Assert.Equal(Native.StatePayload.Json, native.Payload),
            () => Assert.Equal(TimeSpan.FromSeconds(2), native.Ttl),
            () => Assert.Null(native.ReadUncommitted),
            () => Assert.Null(native.KeysetLimit)
        );
    }

    [Fact]
    public void ToNative_Value_MapsPublicationAndReadCache()
    {
        var native = StateDefinition
            .Value<int>("v", published: true, readCache: StateReadCache.For(TimeSpan.FromSeconds(2)))
            .ToNative();

        Assert.Multiple(
            () => Assert.True(native.Published),
            () => Assert.Equal(TimeSpan.FromSeconds(2), native.ReadCacheTtl),
            () => Assert.False(native.ReadCacheDisabled)
        );
    }

    [Fact]
    public void ReadCache_ZeroTtl_IsPassedToProsody()
    {
        Assert.Equal(
            TimeSpan.Zero,
            StateDefinition.Value<int>("v", readCache: StateReadCache.For(TimeSpan.Zero)).ToNative().ReadCacheTtl
        );
    }

    [Fact]
    public void ToNative_MessageDeque_MapsKindAndPayload()
    {
        var native = StateDefinition.MessageDeque<int>("d").ToNative();

        Assert.Multiple(
            () => Assert.Equal(Native.StateKind.Deque, native.Kind),
            () => Assert.Equal(Native.StatePayload.Message, native.Payload)
        );
    }

    [Fact]
    public void ToNative_Map_MapsKeysetLimit()
    {
        var native = StateDefinition.Map<int>("m", keysetLimit: 8).ToNative();

        Assert.Multiple(
            () => Assert.Equal(Native.StateKind.Map, native.Kind),
            () => Assert.Equal(Native.StatePayload.Json, native.Payload),
            () => Assert.Equal(8u, native.KeysetLimit)
        );
    }

    [Fact]
    public void Deque_Capacity_Positive_Ok()
    {
        var definition = StateDefinition.Deque<int>("d", capacity: 3);
        Assert.Equal("d", definition.Name);
    }

    [Fact]
    public void ToNative_Deque_MapsCapacity()
    {
        var native = StateDefinition.Deque<int>("d", capacity: 100).ToNative();

        Assert.Multiple(
            () => Assert.Equal(Native.StateKind.Deque, native.Kind),
            () => Assert.Equal(100u, native.Capacity)
        );
    }

    [Fact]
    public void ToNative_Deque_NoCapacity_IsNull()
    {
        Assert.Null(StateDefinition.Deque<int>("d").ToNative().Capacity);
    }

    [Fact]
    public void Deque_Capacity_IsNotIdentity()
    {
        // A bounded and an unbounded same-name deque carry identical registration identity: capacity
        // is runtime-only and not part of (name, kind, payload). This pins the C#-observable half of
        // that contract; core owns the cross-restart mutability/convergence property tests.
        var bounded = StateDefinition.Deque<int>("d", capacity: 5).ToNative();
        var unbounded = StateDefinition.Deque<int>("d").ToNative();

        Assert.Multiple(
            () => Assert.Equal(bounded.Name, unbounded.Name),
            () => Assert.Equal(bounded.Kind, unbounded.Kind),
            () => Assert.Equal(bounded.Payload, unbounded.Payload),
            () => Assert.Equal(5u, bounded.Capacity),
            () => Assert.Null(unbounded.Capacity)
        );
    }
}
