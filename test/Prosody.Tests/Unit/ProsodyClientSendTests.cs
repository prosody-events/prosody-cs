using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Prosody.Configuration;
using Prosody.Infrastructure;
using Prosody.Tests.TestHelpers;

namespace Prosody.Tests.Unit;

// Namespace-level so the STJ source generator can reference them from its .g.cs output
internal sealed record SendTestPayload(string OrderTotal, int ItemCount);

// Source-gen context using snake_case — intentionally different from client's default camelCase
[JsonSerializable(typeof(SendTestPayload))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
internal sealed partial class SnakeCaseSendContext : JsonSerializerContext;

// Payload whose converter cancels the token during serialization — after the entry
// ThrowIfCancellationRequested, before the native signal is created — so the native
// send starts with a pre-cancelled signal and deterministically reports Cancelled.
internal sealed record CancelOnWritePayload;

internal sealed class CancelOnWriteConverter(CancellationTokenSource cts) : JsonConverter<CancelOnWritePayload>
{
    public override CancelOnWritePayload Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    ) => throw new NotSupportedException();

    public override void Write(Utf8JsonWriter writer, CancelOnWritePayload value, JsonSerializerOptions options)
    {
        cts.Cancel();
        writer.WriteStartObject();
        writer.WriteEndObject();
    }
}

// Payload with [JsonPropertyName] overrides so TypedEventMetadataExtractor can find id/type
internal sealed record MetadataTestPayload
{
    [JsonPropertyName("id")]
    public string? EventId { get; init; }

    [JsonPropertyName("type")]
    public string? EventType { get; init; }
}

/// <summary>
/// Tests for <see cref="ProsodyClient.SendAsync{T}"/> validation.
/// </summary>
public sealed class ProsodyClientSendTests : IDisposable
{
    private readonly ProsodyClient _client = new(
        new ClientOptions
        {
            Mock = true,
            BootstrapServers = [TestDefaults.BootstrapServers],
            GroupId = "test-group",
            SourceSystem = "test",
        }
    );

    public void Dispose() => _client.Dispose();

    [Fact]
    public async Task SendAsyncThrowsWhenTopicIsNull()
    {
        var ct = TestContext.Current.CancellationToken;
        await Assert.ThrowsAsync<ArgumentNullException>("topic", () => _client.SendAsync(null!, "key", new { }, ct));
    }

    [Fact]
    public async Task SendAsyncThrowsWhenKeyIsNull()
    {
        var ct = TestContext.Current.CancellationToken;
        await Assert.ThrowsAsync<ArgumentNullException>("key", () => _client.SendAsync("topic", null!, new { }, ct));
    }

    [Fact]
    public async Task SendAsyncThrowsWhenAlreadyCancelled()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            _client.SendAsync("topic", "key", new { }, cts.Token)
        );
    }

    [Fact]
    public async Task SendAsyncThrowsOperationCanceledWhenCancelledMidSend()
    {
        using var cts = new CancellationTokenSource();
        await using var client = new ProsodyClient(
            new ClientOptions
            {
                Mock = true,
                BootstrapServers = [TestDefaults.BootstrapServers],
                GroupId = "test-group",
                SourceSystem = "test",
                ConfigureJsonOptions = options => options.Converters.Add(new CancelOnWriteConverter(cts)),
            }
        );

        var ex = await Assert.ThrowsAsync<OperationCanceledException>(() =>
            client.SendAsync("topic", "key", new CancelOnWritePayload(), cts.Token)
        );

        // The inner exception proves the cancellation crossed the native boundary and was
        // translated, rather than being caught by the entry-point token check.
        Assert.IsType<Native.FfiException.Cancelled>(ex.InnerException);
    }

    [Fact]
    public async Task SendAsync_JsonTypeInfo_ThrowsOnNullTopic()
    {
        var typeInfo = SnakeCaseSendContext.Default.SendTestPayload;
        var ct = TestContext.Current.CancellationToken;
        await Assert.ThrowsAsync<ArgumentNullException>(
            "topic",
            () => _client.SendAsync(null!, "key", new SendTestPayload("10.00", 1), typeInfo, ct)
        );
    }

    [Fact]
    public async Task SendAsync_JsonTypeInfo_ThrowsOnNullKey()
    {
        var typeInfo = SnakeCaseSendContext.Default.SendTestPayload;
        var ct = TestContext.Current.CancellationToken;
        await Assert.ThrowsAsync<ArgumentNullException>(
            "key",
            () => _client.SendAsync("topic", null!, new SendTestPayload("10.00", 1), typeInfo, ct)
        );
    }

    [Fact]
    public async Task SendAsync_JsonTypeInfo_ThrowsOnNullTypeInfo()
    {
        var ct = TestContext.Current.CancellationToken;
        await Assert.ThrowsAsync<ArgumentNullException>(
            "typeInfo",
            () => _client.SendAsync<SendTestPayload>("topic", "key", new SendTestPayload("10.00", 1), null!, ct)
        );
    }

    [Fact]
    public void SendAsync_JsonTypeInfo_OverloadUsesSnakeCaseContext()
    {
        // The trim-safe overload accepts a JsonTypeInfo from a snake_case source-gen context.
        // The client is configured with camelCase defaults; passing a snake_case JsonTypeInfo
        // should use that type info's naming policy, not the client's options.
        var snakeTypeInfo = SnakeCaseSendContext.Default.SendTestPayload;
        Assert.Equal(JsonNamingPolicy.SnakeCaseLower, snakeTypeInfo.Options.PropertyNamingPolicy);
    }

    [Fact]
    public async Task SendAsync_SendOptions_ThrowsOnNullTopic()
    {
        var typeInfo = SnakeCaseSendContext.Default.SendTestPayload;
        var opts = new SendOptions();
        var ct = TestContext.Current.CancellationToken;
        await Assert.ThrowsAsync<ArgumentNullException>(
            "topic",
            () => _client.SendAsync(null!, "key", new SendTestPayload("10.00", 1), typeInfo, opts, ct)
        );
    }

    [Fact]
    public async Task SendAsync_SendOptions_ThrowsOnNullKey()
    {
        var typeInfo = SnakeCaseSendContext.Default.SendTestPayload;
        var opts = new SendOptions();
        var ct = TestContext.Current.CancellationToken;
        await Assert.ThrowsAsync<ArgumentNullException>(
            "key",
            () => _client.SendAsync("topic", null!, new SendTestPayload("10.00", 1), typeInfo, opts, ct)
        );
    }

    [Fact]
    public async Task SendAsync_SendOptions_ThrowsOnNullTypeInfo()
    {
        var opts = new SendOptions();
        var ct = TestContext.Current.CancellationToken;
        await Assert.ThrowsAsync<ArgumentNullException>(
            "typeInfo",
            () => _client.SendAsync<SendTestPayload>("topic", "key", new SendTestPayload("10.00", 1), null!, opts, ct)
        );
    }

    [Fact]
    public async Task SendAsync_SendOptions_ThrowsOnNullOptions()
    {
        var typeInfo = SnakeCaseSendContext.Default.SendTestPayload;
        var ct = TestContext.Current.CancellationToken;
        await Assert.ThrowsAsync<ArgumentNullException>(
            "options",
            () => _client.SendAsync("topic", "key", new SendTestPayload("10.00", 1), typeInfo, null!, ct)
        );
    }

    [Fact]
    public async Task SendAsync_SendOptions_ThrowsWhenAlreadyCancelled()
    {
        var typeInfo = SnakeCaseSendContext.Default.SendTestPayload;
        var opts = new SendOptions();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            _client.SendAsync("topic", "key", new SendTestPayload("10.00", 1), typeInfo, opts, cts.Token)
        );
    }
}

/// <summary>
/// Tests for the <see cref="SendOptions"/> record contract and the override-vs-extract coalesce
/// logic in <see cref="ProsodyClient.SendAsync{T}(string,string,T,JsonTypeInfo{T},SendOptions,CancellationToken)"/>.
/// </summary>
public sealed class SendOptionsTests
{
    [Fact]
    public void DefaultConstructed_HasNullProperties()
    {
        var opts = new SendOptions();
        Assert.Multiple(() => Assert.Null(opts.EventId), () => Assert.Null(opts.EventType));
    }

    [Fact]
    public void WithEventId_CreatesNewInstance()
    {
        var original = new SendOptions();
        var overridden = original with { EventId = "evt-x" };

        Assert.NotSame(original, overridden);
        Assert.Multiple(() => Assert.Null(original.EventId), () => Assert.Equal("evt-x", overridden.EventId));
    }

    [Fact]
    public void EqualityHoldsByValue()
    {
        var a = new SendOptions { EventId = "a", EventType = "b" };
        var b = new SendOptions { EventId = "a", EventType = "b" };

        Assert.Equal(a, b);
    }

    // B3 fallback: Message<T> does not expose consumed EventMetadata headers, so the coalesce
    // logic at ProsodyClient.SendCoreAsync:252-253 is verified directly via TypedEventMetadataExtractor
    // (internal, accessible via InternalsVisibleTo).
    [Fact]
    public void OverrideAndFallback_CoalesceMatchesSendCore()
    {
        var payload = new MetadataTestPayload { EventId = "payload-id", EventType = "payload.type" };
        var typeInfo =
            (JsonTypeInfo<MetadataTestPayload>)JsonSerializerOptions.Default.GetTypeInfo(typeof(MetadataTestPayload));

        var (extractedId, extractedType) = TypedEventMetadataExtractor.Extract(payload, typeInfo);

        // Explicit options override extracted values (mirrors SendCoreAsync:252-253)
        var withOverrides = new SendOptions { EventId = "override-id", EventType = "override.type" };
        Assert.Multiple(
            () => Assert.Equal("override-id", withOverrides.EventId ?? extractedId),
            () => Assert.Equal("override.type", withOverrides.EventType ?? extractedType)
        );

        // Null option values fall back to extracted (mirrors SendCoreAsync:252-253 when properties are null)
        var withNulls = new SendOptions();
        Assert.Multiple(
            () => Assert.Equal("payload-id", withNulls.EventId ?? extractedId),
            () => Assert.Equal("payload.type", withNulls.EventType ?? extractedType)
        );
    }
}
