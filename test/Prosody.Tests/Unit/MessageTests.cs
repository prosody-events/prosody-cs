using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Prosody.Messaging;

namespace Prosody.Tests.Unit;

// Namespace-level so the STJ source generator can reference them from its .g.cs output
internal sealed record SampleRecord(string Name, int Value);

[JsonSerializable(typeof(SampleRecord))]
internal sealed partial class SampleAotJsonContext : JsonSerializerContext;

/// <summary>
/// Unit tests for <see cref="Message{T}"/>.
/// Uses the internal test constructor to avoid requiring a real Native.Message (FFI object).
/// </summary>
public sealed class MessageTests
{
    private static readonly JsonSerializerOptions AotOptions = BuildAotOptions();

    private static JsonSerializerOptions BuildAotOptions()
    {
        var o = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        o.TypeInfoResolverChain.Add(SampleAotJsonContext.Default);
        o.MakeReadOnly();
        return o;
    }

    private static Message<T> CreateTypedMessage<T>(T? payload) =>
        new("topic", "key", partition: 0, offset: 0, DateTimeOffset.UtcNow, payload);

    [Fact]
    public void TypedMessage_ExposesDeserializedPayload()
    {
        var expected = new SampleRecord("erin", 11);
        var message = CreateTypedMessage(expected);

        Assert.Multiple(
            () => Assert.Equal(expected, message.Payload),
            () => Assert.Equal("topic", message.Topic),
            () => Assert.Equal("key", message.Key)
        );
    }

    [Fact]
    public void TypedMessage_NullPayload_ReturnsNull()
    {
        var message = CreateTypedMessage<SampleRecord>(null);

        Assert.Null(message.Payload);
    }

    [Fact]
    public void TypedMessage_JsonElementPayload_ParsesJsonDom()
    {
        var element = JsonSerializer.Deserialize<JsonElement>("""{"name":"frank","value":12}""");
        var message = CreateTypedMessage(element);

        Assert.Multiple(
            () => Assert.Equal(JsonValueKind.Object, message.Payload.ValueKind),
            () => Assert.Equal("frank", message.Payload.GetProperty("name").GetString()),
            () => Assert.Equal(12, message.Payload.GetProperty("value").GetInt32())
        );
    }

    [Fact]
    public void TypedMessage_MetadataFields_AreExposed()
    {
        var ts = new DateTimeOffset(2024, 1, 15, 10, 30, 0, TimeSpan.Zero);
        var message = new Message<SampleRecord>("my-topic", "my-key", partition: 3, offset: 42L, ts, null);

        Assert.Multiple(
            () => Assert.Equal("my-topic", message.Topic),
            () => Assert.Equal("my-key", message.Key),
            () => Assert.Equal(3, message.Partition),
            () => Assert.Equal(42L, message.Offset),
            () => Assert.Equal(ts, message.Timestamp)
        );
    }

    [Fact]
    public void Message_RoundTripsViaSourceGenContext()
    {
        var typeInfo = (JsonTypeInfo<SampleRecord>)AotOptions.GetTypeInfo(typeof(SampleRecord));
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new SampleRecord("dave", 99), AotOptions);
        var payload = JsonSerializer.Deserialize(bytes.AsSpan(), typeInfo);
        var message = new Message<SampleRecord>("t", "k", 0, 0L, default, payload);

        Assert.Equal(new SampleRecord("dave", 99), message.Payload);
    }
}
