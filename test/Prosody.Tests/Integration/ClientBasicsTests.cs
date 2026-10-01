using Prosody.Errors;
using Prosody.Messaging;
using Prosody.Tests.TestHelpers;

namespace Prosody.Tests.Integration;

/// <summary>
/// Basic client initialization and lifecycle tests.
/// </summary>
public sealed class ClientBasicsTests(IntegrationTestFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task InitializesCorrectly()
    {
        await using IntegrationTestContext ctx = await CreateTestContextAsync();
        ConsumerState state = await ctx.Client.GetConsumerStateAsync(TestContext.Current.CancellationToken);
        Assert.Multiple(() => Assert.NotNull(ctx.Client), () => Assert.Equal(ConsumerState.Configured, state));
    }

    [Fact]
    public async Task ExposesSourceSystemIdentifier()
    {
        await using IntegrationTestContext ctx = await CreateTestContextAsync();
        Assert.Equal("test-source", ctx.Client.SourceSystem);
    }

    [Fact]
    public async Task SubscribesAndUnsubscribes()
    {
        await using IntegrationTestContext ctx = await CreateTestContextAsync();
        var handler = new TestProsodyHandler<TestPayload>();

        await ctx.Client.SubscribeAsync(handler, TestContext.Current.CancellationToken);
        Assert.Equal(
            ConsumerState.Running,
            await ctx.Client.GetConsumerStateAsync(TestContext.Current.CancellationToken)
        );

        await ctx.Client.UnsubscribeAsync();
        Assert.Equal(
            ConsumerState.Configured,
            await ctx.Client.GetConsumerStateAsync(TestContext.Current.CancellationToken)
        );
    }

    [Fact]
    public async Task CreatesTopicWithCleanupPolicyAndRetention()
    {
        var compacted = TopicGenerator.GenerateTopicName();
        var invalid = TopicGenerator.GenerateTopicName();

        // The broker rejects an unknown policy only when the client sends it.
        await Fixture.Admin.CreateTopicAsync(compacted, 1, 1, "compact", TimeSpan.FromDays(1));
        await Assert.ThrowsAsync<ProsodyException>(() => Fixture.Admin.CreateTopicAsync(invalid, 1, 1, "bogus"));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            Fixture.Admin.CreateTopicAsync(invalid, 1, 1, retention: TimeSpan.FromTicks(-1))
        );
        await Fixture.Admin.DeleteTopicAsync(compacted);
    }

    // An assembly built against the three-parameter method must still bind to it.
    [Fact]
    public void KeepsThreeParameterCreateTopic() =>
        Assert.NotNull(
            typeof(AdminClient).GetMethod(
                nameof(AdminClient.CreateTopicAsync),
                [typeof(string), typeof(ushort), typeof(ushort)]
            )
        );
}
