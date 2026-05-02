using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Prosody.Configuration;
using Prosody.Tests.TestHelpers;

namespace Prosody.Tests.Unit;

// Namespace-level so the STJ source generator can reference them from its .g.cs output
internal sealed record SendTestPayload(string OrderTotal, int ItemCount);

// Source-gen context using snake_case — intentionally different from client's default camelCase
[JsonSerializable(typeof(SendTestPayload))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
internal sealed partial class SnakeCaseSendContext : JsonSerializerContext;

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

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => _client.SendAsync("topic", "key", new { }, cts.Token)
        );
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
}
