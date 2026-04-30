using Prosody.Configuration;
using Prosody.Tests.TestHelpers;

namespace Prosody.Tests.Unit;

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
}
