using Prosody.State;
using Native = Prosody.Native;

namespace Prosody.Tests.Unit;

/// <summary>
/// Unit tests for <see cref="StateDefinition"/> construction-time validation and native mapping.
/// </summary>
public sealed class StateDefinitionTests
{
    private const long _cassandraCeilingSeconds = 630_720_000;

    [Fact]
    public void Value_ValidName_Constructs()
    {
        var definition = StateDefinition.Value<int>("counter");
        Assert.Equal("counter", definition.Name);
    }

    [Theory]
    [InlineData("")]
    public void Value_EmptyName_Throws(string name)
    {
        Assert.Throws<ArgumentException>(() => StateDefinition.Value<int>(name));
    }

    [Fact]
    public void Map_EmptyName_Throws()
    {
        Assert.Throws<ArgumentException>(() => StateDefinition.Map<int>(string.Empty));
    }

    [Fact]
    public void Deque_EmptyName_Throws()
    {
        Assert.Throws<ArgumentException>(() => StateDefinition.Deque<int>(string.Empty));
    }

    [Fact]
    public void MessageDeque_EmptyName_Throws()
    {
        Assert.Throws<ArgumentException>(() => StateDefinition.MessageDeque<int>(string.Empty));
    }

    [Fact]
    public void Ttl_Zero_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => StateDefinition.Value<int>("v", ttl: TimeSpan.Zero));
    }

    [Fact]
    public void Ttl_Ceiling_Ok()
    {
        var definition = StateDefinition.Value<int>("v", ttl: TimeSpan.FromSeconds(_cassandraCeilingSeconds));
        Assert.Equal("v", definition.Name);
    }

    [Fact]
    public void Ttl_AboveCeiling_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            StateDefinition.Value<int>("v", ttl: TimeSpan.FromSeconds(_cassandraCeilingSeconds + 1))
        );
    }

    [Fact]
    public void Ttl_Fractional_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            StateDefinition.Value<int>("v", ttl: TimeSpan.FromMilliseconds(1500))
        );
    }

    [Fact]
    public void Ttl_OneSecond_Ok()
    {
        var definition = StateDefinition.Value<int>("v", ttl: TimeSpan.FromSeconds(1));
        Assert.Equal("v", definition.Name);
    }

    [Fact]
    public void Keyset_Zero_Ok()
    {
        var definition = StateDefinition.Map<int>("m", keysetLimit: 0);
        Assert.Equal("m", definition.Name);
    }

    [Fact]
    public void Keyset_Max_Ok()
    {
        var definition = StateDefinition.Map<int>("m", keysetLimit: 4096);
        Assert.Equal("m", definition.Name);
    }

    [Fact]
    public void Keyset_AboveMax_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => StateDefinition.Map<int>("m", keysetLimit: 4097));
    }

    [Fact]
    public void Keyset_Negative_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => StateDefinition.Map<int>("m", keysetLimit: -1));
    }

    [Fact]
    public void MessageMap_Keyset_AboveMax_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => StateDefinition.MessageMap<int>("m", keysetLimit: 4097));
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
}
