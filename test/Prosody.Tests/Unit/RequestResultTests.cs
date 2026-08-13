using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Prosody.Messaging;

namespace Prosody.Tests.Unit;

public sealed class RequestResultTests
{
    [Fact]
    public void MalformedJsonMapsToMalformedError()
    {
        var result = ProsodyClient.MapRequestResult(
            new Native.NativeRequestResult.Ok("{"u8.ToArray()),
            (JsonTypeInfo<string>)JsonSerializerOptions.Default.GetTypeInfo(typeof(string))
        );

        Assert.IsType<MalformedResponseError>(Assert.IsType<Err<string>>(result).Error);
    }

    [Fact]
    public void JsonNullRemainsAValidSuccess()
    {
        var result = ProsodyClient.MapRequestResult(
            new Native.NativeRequestResult.Ok("null"u8.ToArray()),
            (JsonTypeInfo<string>)JsonSerializerOptions.Default.GetTypeInfo(typeof(string))
        );

        Assert.Null(Assert.IsType<Ok<string>>(result).Value);
    }
}
