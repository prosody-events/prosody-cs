using Prosody.Native;

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

        Assert.Equal(3, values.Length);
        Assert.Contains(ClientMode.Pipeline, values);
        Assert.Contains(ClientMode.LowLatency, values);
        Assert.Contains(ClientMode.BestEffort, values);
    }

    [Fact]
    public void ConsumerStateHasExpectedVariants()
    {
        var values = Enum.GetValues<ConsumerState>();

        Assert.Equal(3, values.Length);
        Assert.Contains(ConsumerState.Unconfigured, values);
        Assert.Contains(ConsumerState.Configured, values);
        Assert.Contains(ConsumerState.Running, values);
    }

    [Fact]
    public void HandlerResultCodeHasExpectedVariants()
    {
        var values = Enum.GetValues<HandlerResultCode>();

        Assert.Equal(4, values.Length);
        Assert.Contains(HandlerResultCode.Success, values);
        Assert.Contains(HandlerResultCode.TransientError, values);
        Assert.Contains(HandlerResultCode.PermanentError, values);
        Assert.Contains(HandlerResultCode.Cancelled, values);
    }
}
