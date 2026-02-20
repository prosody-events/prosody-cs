using Prosody.Configuration;
using Prosody.Tests.TestHelpers;

namespace Prosody.Tests.Unit;

/// <summary>
/// Tests for <see cref="ProsodyClient.SendAsync{T}"/> and <see cref="ProsodyClient.SendRawAsync"/> validation.
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
    public async Task SendRawAsyncThrowsWhenTopicIsNull()
    {
        var ct = TestContext.Current.CancellationToken;
        await Assert.ThrowsAsync<ArgumentNullException>("topic", () => _client.SendRawAsync(null!, "key", [], ct));
    }

    [Fact]
    public async Task SendRawAsyncThrowsWhenKeyIsNull()
    {
        var ct = TestContext.Current.CancellationToken;
        await Assert.ThrowsAsync<ArgumentNullException>("key", () => _client.SendRawAsync("topic", null!, [], ct));
    }

    [Fact]
    public async Task SendRawAsyncThrowsWhenPayloadIsNull()
    {
        var ct = TestContext.Current.CancellationToken;
        await Assert.ThrowsAsync<ArgumentNullException>(
            "jsonPayload",
            () => _client.SendRawAsync("topic", "key", null!, ct)
        );
    }

    [Fact]
    public async Task SendRawAsyncThrowsWhenAlreadyCancelled()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        await Assert.ThrowsAsync<OperationCanceledException>(() => _client.SendRawAsync("topic", "key", [], cts.Token));
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
}
