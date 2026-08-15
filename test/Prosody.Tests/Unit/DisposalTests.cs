using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Prosody.Configuration;
using Prosody.Messaging;
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

    private sealed class NoOpHandler : IProsodyHandler<JsonElement>
    {
        public Task OnMessageAsync(
            ProsodyContext prosodyContext,
            Message<JsonElement> message,
            CancellationToken cancellationToken
        ) => Task.CompletedTask;

        public Task OnExciseAsync(
            ProsodyContext prosodyContext,
            Message<JsonElement> message,
            CancellationToken cancellationToken
        ) => Task.CompletedTask;

        public Task OnTimerAsync(
            ProsodyContext prosodyContext,
            ProsodyTimer timer,
            CancellationToken cancellationToken
        ) => Task.CompletedTask;
    }

    [Fact]
    public async Task DisposeAsyncSafeWhenNotSubscribed()
    {
        var client = await ProsodyClient.CreateAsync(MockOptions);
        // Should not throw when consumer was never subscribed
        await client.DisposeAsync();
    }

    [Fact]
    public async Task DisposeAsyncIsIdempotent()
    {
        var client = await ProsodyClient.CreateAsync(MockOptions);
        await client.SubscribeAsync(new NoOpHandler());
        await client.DisposeAsync();

        // Should not throw on second call
        await client.DisposeAsync();
    }

    [Fact]
    public async Task ProviderRetriesFailedConstruction()
    {
        var attempts = 0;
        await using var provider = new ProsodyClientProvider(async () =>
        {
            if (Interlocked.Increment(ref attempts) == 1)
            {
                throw new InvalidOperationException("unavailable");
            }
            return await ProsodyClient.CreateAsync(MockOptions);
        });

        await Assert.ThrowsAsync<InvalidOperationException>(provider.GetAsync);
        Assert.NotNull(await provider.GetAsync());
        Assert.Equal(2, attempts);
    }

    [Fact]
    public async Task ProviderDisposalDoesNotRepeatConstructionFailure()
    {
        var pending = new TaskCompletionSource<ProsodyClient>(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = new ProsodyClientProvider(() => pending.Task);

        var construction = provider.GetAsync();
        var disposal = provider.DisposeAsync().AsTask();
        pending.SetException(new InvalidOperationException("unavailable"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => construction);
        await disposal;
    }

    [Fact]
    public async Task ProviderSupportsSynchronousDisposal()
    {
        var services = new ServiceCollection();
        services.AddSingleton(_ => new ProsodyClientProvider(() => ProsodyClient.CreateAsync(MockOptions)));
        var container = services.BuildServiceProvider();
        var provider = container.GetRequiredService<ProsodyClientProvider>();
        Assert.NotNull(await provider.GetAsync());

        DisposeSynchronously(container);
    }

    private static void DisposeSynchronously(ServiceProvider value) => value.Dispose();
}
