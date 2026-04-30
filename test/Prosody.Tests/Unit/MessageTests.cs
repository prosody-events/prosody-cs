using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Prosody.Messaging;

namespace Prosody.Tests.Unit;

/// <summary>
/// Unit tests for <see cref="Message.RawPayload"/> and <see cref="Message.GetPayload{T}"/>.
/// Uses the internal test constructor to avoid requiring a real Native.Message (FFI object).
/// </summary>
public sealed partial class MessageTests
{
    private sealed record SampleRecord(string Name, int Value);

    private static readonly JsonSerializerOptions DefaultOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
    {
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
    };

    private static byte[] Serialize<T>(T value) => JsonSerializer.SerializeToUtf8Bytes(value, DefaultOptions);

    private static Message CreateMessage(byte[] payload, JsonSerializerOptions? options = null) =>
        new("topic", "key", partition: 0, offset: 0, DateTimeOffset.UtcNow, payload, options ?? DefaultOptions);

    [Fact]
    public void RawPayload_ReturnsExactBytes()
    {
        var bytes = Serialize(new SampleRecord("hello", 1));
        var message = CreateMessage(bytes);

        Assert.True(message.RawPayload.Span.SequenceEqual(bytes));
    }

    [Fact]
    public void RawPayload_ZeroCopy_AliasesInternalArray()
    {
        var message = CreateMessage(Serialize(new SampleRecord("hello", 1)));

        MemoryMarshal.TryGetArray(message.RawPayload, out var first);
        MemoryMarshal.TryGetArray(message.RawPayload, out var second);

        Assert.Same(first.Array, second.Array);
    }

    [Fact]
    public void GetPayload_Deserializes_WithClientOptions()
    {
        var expected = new SampleRecord("alice", 10);
        var message = CreateMessage(Serialize(expected));

        var result = message.GetPayload<SampleRecord>();

        Assert.Equal(expected, result);
    }

    [Fact]
    public void GetPayload_EmptyPayload_ThrowsJsonException()
    {
        var message = CreateMessage([]);

        Assert.Throws<JsonException>(() => message.GetPayload<SampleRecord>());
    }

    [Fact]
    public void GetPayload_MalformedJson_ThrowsJsonException()
    {
        var message = CreateMessage("{not valid json"u8.ToArray());

        Assert.Throws<JsonException>(() => message.GetPayload<SampleRecord>());
    }

    [Fact]
    public void GetPayload_JsonNullToken_ReturnsNull()
    {
        var message = CreateMessage("null"u8.ToArray());

        var result = message.GetPayload<SampleRecord>();

        Assert.Null(result);
    }

    [Fact]
    public void GetPayload_HonorsSnakeCaseOverride()
    {
        var snakeOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        };
        var snakeBytes = """{"name":"carol","value":30}"""u8.ToArray();
        var message = CreateMessage(snakeBytes, snakeOptions);

        var result = message.GetPayload<SampleRecord>();

        Assert.Equal(new SampleRecord("carol", 30), result);
    }

    [JsonSerializable(typeof(SampleRecord))]
    private sealed partial class SampleAotContext : JsonSerializerContext;

    [Fact]
    public void GetPayload_HonorsConfiguredTypeInfoResolver()
    {
        var aotOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        aotOptions.TypeInfoResolverChain.Add(SampleAotContext.Default);
        aotOptions.MakeReadOnly();

        var bytes = JsonSerializer.SerializeToUtf8Bytes(new SampleRecord("dave", 99), aotOptions);
        var message = CreateMessage(bytes, aotOptions);

        var result = message.GetPayload<SampleRecord>();

        Assert.Equal(new SampleRecord("dave", 99), result);
    }
}
