using Prosody.State;
using Prosody.Tests.TestHelpers;

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
        var present = StateInterop.JsonToValue<decimal>("42"u8.ToArray(), TestJson.TypeInfo<decimal>());

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
        var value = StateInterop.JsonToValue<decimal>("0"u8.ToArray(), TestJson.TypeInfo<decimal>());

        Assert.Multiple(() => Assert.True(value.HasValue), () => Assert.Equal(0m, value.Value));
    }

    [Fact]
    public void PresentDefault_Bool_False_IsPresent()
    {
        var value = StateInterop.JsonToValue<bool>("false"u8.ToArray(), TestJson.TypeInfo<bool>());

        Assert.Multiple(() => Assert.True(value.HasValue), () => Assert.False(value.Value));
    }

    [Fact]
    public void StoredJsonNull_Throws()
    {
        Assert.Throws<TransientStateException>(() =>
            StateInterop.JsonToValue<string>("null"u8.ToArray(), TestJson.TypeInfo<string>())
        );
    }
}
