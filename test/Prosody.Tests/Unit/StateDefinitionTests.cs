using Prosody.State;
using Native = Prosody.Native;

namespace Prosody.Tests.Unit;

/// <summary>
/// Unit tests for <see cref="StateDefinition"/> host-value validation and native mapping.
/// </summary>
public sealed class StateDefinitionTests
{
    [Fact]
    public void NegativeBound_Throws()
    {
        Assert.Multiple(
            () => Assert.Throws<ArgumentOutOfRangeException>(() => StateDefinition.Map<int>("m", keysetLimit: -1)),
            () => Assert.Throws<ArgumentOutOfRangeException>(() => StateDefinition.Set("s", keysetLimit: -1)),
            () => Assert.Throws<ArgumentOutOfRangeException>(() => StateDefinition.Deque<int>("d", capacity: -1))
        );
    }

    [Fact]
    public void EachFactory_MapsToItsNativeCollection()
    {
        var json = Native.StatePayload.Json;
        var message = Native.StatePayload.Message;
        var ttl = TimeSpan.FromSeconds(5);

        Assert.Multiple(
            () =>
                Assert.Equal(
                    new Native.StateCollectionConfig("v", new Native.StateKind.Value(json), ttl, false, true),
                    StateDefinition.Value<int>("v", ttl: ttl, published: true).ToNative()
                ),
            () =>
                Assert.Equal(
                    new Native.StateCollectionConfig("m", new Native.StateKind.Map(json, 0), null, true, false),
                    StateDefinition.Map<int>("m", readUncommitted: true, keysetLimit: 0).ToNative()
                ),
            () =>
                Assert.Equal(
                    new Native.StateCollectionConfig("d", new Native.StateKind.Deque(json, 100), null, false, false),
                    StateDefinition.Deque<int>("d", capacity: 100).ToNative()
                ),
            () =>
                Assert.Equal(
                    new Native.StateCollectionConfig("s", new Native.StateKind.Set(64), null, false, false),
                    StateDefinition.Set("s", keysetLimit: 64).ToNative()
                ),
            () =>
                Assert.Equal(
                    new Native.StateCollectionConfig("mv", new Native.StateKind.Value(message), null, false, false),
                    StateDefinition.MessageValue<int>("mv").ToNative()
                ),
            () =>
                Assert.Equal(
                    new Native.StateCollectionConfig("mm", new Native.StateKind.Map(message, 8), null, false, false),
                    StateDefinition.MessageMap<int>("mm", keysetLimit: 8).ToNative()
                ),
            () =>
                Assert.Equal(
                    new Native.StateCollectionConfig(
                        "md",
                        new Native.StateKind.Deque(message, null),
                        null,
                        false,
                        false
                    ),
                    StateDefinition.MessageDeque<int>("md").ToNative()
                )
        );
    }

    [Fact]
    public void ReadCache_MapsToTheNativePolicy()
    {
        Assert.Multiple(
            () =>
                Assert.Equal(
                    new Native.ReadCache.Ttl(TimeSpan.Zero),
                    StateDefinition.Value<int>("v", readCache: StateReadCache.For(TimeSpan.Zero)).ReadCache?.Policy
                ),
            () =>
                Assert.Equal(
                    new Native.ReadCache.Disabled(),
                    StateDefinition.Set("s", readCache: StateReadCache.Disabled).ReadCache?.Policy
                )
        );
    }
}
