using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Prosody.State;
using Native = Prosody.Native;

namespace Prosody.Tests.Unit;

/// <summary>
/// Proves <see cref="StateValue{T}"/> distinguishes an absent value from a stored CLR default.
/// </summary>
public sealed class StateValueTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
    };

    private static JsonTypeInfo<T> TypeInfo<T>() => (JsonTypeInfo<T>)JsonOptions.GetTypeInfo(typeof(T));

    [Fact]
    public void Absent_HasNoValue()
    {
        var value = StateInterop.JsonToValue<decimal>(null, TypeInfo<decimal>());

        Assert.Multiple(
            () => Assert.False(value.HasValue),
            () => Assert.Equal(7m, value.ValueOr(7m)),
            () => Assert.Throws<InvalidOperationException>(() => _ = value.Value)
        );
    }

    [Fact]
    public void PresentDefault_Decimal_Zero_IsPresent()
    {
        using var item = new Native.StateItem.Json("0"u8.ToArray());

        var value = StateInterop.JsonToValue<decimal>(item, TypeInfo<decimal>());

        Assert.Multiple(() => Assert.True(value.HasValue), () => Assert.Equal(0m, value.Value));
    }

    [Fact]
    public void PresentDefault_Bool_False_IsPresent()
    {
        using var item = new Native.StateItem.Json("false"u8.ToArray());

        var value = StateInterop.JsonToValue<bool>(item, TypeInfo<bool>());

        Assert.Multiple(() => Assert.True(value.HasValue), () => Assert.False(value.Value));
    }
}
