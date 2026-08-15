using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Prosody.Configuration;
using Prosody.Messaging;
using Prosody.Tests.TestHelpers;

namespace Prosody.Tests.Unit;

public sealed class OutcomeTests
{
    [Fact]
    public void MalformedJsonMapsToMalformedError()
    {
        var result = ProsodyClient.MapOutcome(
            new Native.NativeRequestResult.Ok("{"u8.ToArray()),
            (JsonTypeInfo<string>)JsonSerializerOptions.Default.GetTypeInfo(typeof(string))
        );

        var error = Assert.IsType<MalformedResponseError>(Assert.IsType<Failure<string>>(result).Error);
        Assert.NotEmpty(error.Message);
    }

    [Fact]
    public void JsonNullRemainsAValidSuccess()
    {
        var result = ProsodyClient.MapOutcome(
            new Native.NativeRequestResult.Ok("null"u8.ToArray()),
            (JsonTypeInfo<string>)JsonSerializerOptions.Default.GetTypeInfo(typeof(string))
        );

        Assert.Null(Assert.IsType<Success<string>>(result).Value);
    }

    [Fact]
    public void UnsupportedJsonMapsToMalformedError()
    {
        var options = new JsonSerializerOptions(JsonSerializerOptions.Default);
        options.Converters.Add(new UnsupportedConverter());
        var result = ProsodyClient.MapOutcome(
            new Native.NativeRequestResult.Ok("{}"u8.ToArray()),
            (JsonTypeInfo<object>)options.GetTypeInfo(typeof(object))
        );

        var error = Assert.IsType<MalformedResponseError>(Assert.IsType<Failure<object>>(result).Error);
        Assert.NotEmpty(error.Message);
    }

    [Fact]
    public async Task NegativeRequestTimeoutCannotConvertToDuration()
    {
        await using var client = await ProsodyClient.CreateAsync(
            new ClientOptions
            {
                Mock = true,
                BootstrapServers = [TestDefaults.BootstrapServers],
                GroupId = "test-group",
            }
        );

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            client.RequestAsync<object, object>(
                "topic",
                "key",
                new(),
                ["subsystem"],
                TimeSpan.FromTicks(-1),
                cancellationToken: TestContext.Current.CancellationToken
            )
        );
    }

    private sealed class UnsupportedConverter : JsonConverter<object>
    {
        public override object Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            throw new NotSupportedException();

        public override void Write(Utf8JsonWriter writer, object value, JsonSerializerOptions options) =>
            throw new NotSupportedException();
    }
}
