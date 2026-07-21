using Prosody.State;
using Prosody.Tests.TestHelpers;
using Native = Prosody.Native;

namespace Prosody.Tests.Unit;

/// <summary>
/// Proves <see cref="StateValue{T}"/> distinguishes an absent value from a stored CLR default.
/// </summary>
public sealed class StateValueTests
{
    [Fact]
    public void Absent_HasNoValue()
    {
        var value = StateInterop.JsonToValue<decimal>(null, TestJson.TypeInfo<decimal>());

        Assert.Multiple(
            () => Assert.False(value.HasValue),
            () => Assert.Equal(7m, value.GetValueOrDefault(7m)),
            () => Assert.Throws<InvalidOperationException>(() => _ = value.Value)
        );
    }

    [Fact]
    public void TryGetValue_MatchesPresence()
    {
        var absent = StateInterop.JsonToValue<decimal>(null, TestJson.TypeInfo<decimal>());
        using var item = new Native.StateItem.Json("42"u8.ToArray());
        var present = StateInterop.JsonToValue<decimal>(item, TestJson.TypeInfo<decimal>());

        Assert.Multiple(
            () => Assert.False(absent.TryGetValue(out _)),
            () => Assert.True(present.TryGetValue(out var got)),
            () =>
            {
                Assert.True(present.TryGetValue(out var got));
                Assert.Equal(42m, got);
            }
        );
    }

    [Fact]
    public void PresentDefault_Decimal_Zero_IsPresent()
    {
        using var item = new Native.StateItem.Json("0"u8.ToArray());

        var value = StateInterop.JsonToValue<decimal>(item, TestJson.TypeInfo<decimal>());

        Assert.Multiple(() => Assert.True(value.HasValue), () => Assert.Equal(0m, value.Value));
    }

    [Fact]
    public void PresentDefault_Bool_False_IsPresent()
    {
        using var item = new Native.StateItem.Json("false"u8.ToArray());

        var value = StateInterop.JsonToValue<bool>(item, TestJson.TypeInfo<bool>());

        Assert.Multiple(() => Assert.True(value.HasValue), () => Assert.False(value.Value));
    }

    [Fact]
    public void StoredJsonNull_Throws()
    {
        using var item = new Native.StateItem.Json("null"u8.ToArray());

        Assert.Throws<TransientStateException>(() =>
            StateInterop.JsonToValue<string>(item, TestJson.TypeInfo<string>())
        );
    }
}
