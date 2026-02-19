using Prosody.Tests.TestHelpers;

namespace Prosody.Tests.Unit;

/// <summary>
/// Tests for ProsodyClient disposal behavior.
/// </summary>
public sealed class DisposalTests
{
    private static ClientOptions MockOptions =>
        new()
        {
            Mock = true,
            BootstrapServers = [TestDefaults.BootstrapServers],
            GroupId = "test-group",
            SubscribedTopics = ["test-topic"],
        };

    private sealed class NoOpHandler : IProsodyHandler
    {
        public Task OnMessageAsync(
            ProsodyContext prosodyContext,
            Message message,
            CancellationToken cancellationToken
        ) => Task.CompletedTask;

        public Task OnTimerAsync(ProsodyContext prosodyContext, Timer timer, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    [Fact]
    public async Task DisposeAsyncSafeWhenNotSubscribed()
    {
        var client = new ProsodyClient(MockOptions);

        // Should not throw when consumer was never subscribed
        await client.DisposeAsync();
    }

    [Fact]
    public async Task DisposeAsyncIsIdempotent()
    {
        var client = new ProsodyClient(MockOptions);

        await client.SubscribeAsync(new NoOpHandler());
        await client.DisposeAsync();

        // Should not throw on second call
        await client.DisposeAsync();
    }
}
