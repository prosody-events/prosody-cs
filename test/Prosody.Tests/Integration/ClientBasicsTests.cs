using Prosody.Messaging;
using Prosody.Tests.TestHelpers;

namespace Prosody.Tests.Integration;

/// <summary>
/// Basic client initialization and lifecycle tests.
/// </summary>
public sealed class ClientBasicsTests(IntegrationTestFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact(Timeout = 30_000)]
    public async Task InitializesCorrectly()
    {
        await using IntegrationTestContext ctx = await CreateTestContextAsync();
        ConsumerState state = await ctx.Client.GetConsumerStateAsync();
        Assert.Multiple(() => Assert.NotNull(ctx.Client), () => Assert.Equal(ConsumerState.Configured, state));
    }

    [Fact(Timeout = 30_000)]
    public async Task ExposesSourceSystemIdentifier()
    {
        await using IntegrationTestContext ctx = await CreateTestContextAsync();
        Assert.Equal("test-source", ctx.Client.SourceSystem);
    }

    [Fact(Timeout = 30_000)]
    public async Task SubscribesAndUnsubscribes()
    {
        await using IntegrationTestContext ctx = await CreateTestContextAsync();
        var handler = new TestProsodyHandler();

        await ctx.Client.SubscribeAsync(handler);
        Assert.Equal(ConsumerState.Running, await ctx.Client.GetConsumerStateAsync());

        await ctx.Client.UnsubscribeAsync();
        Assert.Equal(ConsumerState.Configured, await ctx.Client.GetConsumerStateAsync());
    }
}
