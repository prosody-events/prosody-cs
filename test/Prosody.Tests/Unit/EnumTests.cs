using Prosody.Configuration;
using Prosody.Messaging;

namespace Prosody.Tests.Unit;

/// <summary>
/// Tests for enum types.
/// </summary>
public sealed class EnumTests
{
    [Fact]
    public void ClientModeHasExpectedVariants()
    {
        var values = Enum.GetValues<ClientMode>();

        Assert.Multiple(
            () => Assert.Equal(3, values.Length),
            () => Assert.Contains(ClientMode.Pipeline, values),
            () => Assert.Contains(ClientMode.LowLatency, values),
            () => Assert.Contains(ClientMode.BestEffort, values)
        );
    }

    [Fact]
    public void ConsumerStateHasExpectedVariants()
    {
        var values = Enum.GetValues<ConsumerState>();

        Assert.Multiple(
            () => Assert.Equal(4, values.Length),
            () => Assert.Contains(ConsumerState.Unconfigured, values),
            () => Assert.Contains(ConsumerState.Configured, values),
            () => Assert.Contains(ConsumerState.Running, values),
            () => Assert.Contains(ConsumerState.Shutdown, values)
        );
    }
}
