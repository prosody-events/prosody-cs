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
            GroupId = "test-group",
            SubscribedTopics = ["test-topic"],
        };

    private sealed class NoOpHandler : IProsodyHandler
    {
        public Task OnMessageAsync(
            Context context,
            Message message,
            CancellationToken cancellationToken
        ) => Task.CompletedTask;

        public Task OnTimerAsync(
            Context context,
            Timer timer,
            CancellationToken cancellationToken
        ) => Task.CompletedTask;
    }

    [Fact]
    public async Task DisposeAsyncSafeWhenNotSubscribed()
    {
        // DisposeAsync should not throw when consumer was never subscribed
        await using var client = new ProsodyClient(MockOptions);

        var exception = await Record.ExceptionAsync(async () => await client.DisposeAsync());

        Assert.Null(exception);
    }

    [Fact]
    public async Task DisposeAsyncIsIdempotent()
    {
        // Calling DisposeAsync multiple times should be safe
        var client = new ProsodyClient(MockOptions);
        var handler = new NoOpHandler();

        await client.SubscribeAsync(handler);
        await client.DisposeAsync();

        var exception = await Record.ExceptionAsync(async () => await client.DisposeAsync());

        Assert.Null(exception);
    }
}
