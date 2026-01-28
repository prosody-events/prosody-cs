using Prosody.Native;

namespace Prosody.Tests.Unit;

/// <summary>
/// Tests for UniFFI-generated enum types.
/// </summary>
public sealed class EnumTests
{
    [Fact]
    public void ConsumerState_HasExpectedValues()
    {
        Assert.Equal(0, (int)ConsumerState.Unconfigured);
        Assert.Equal(1, (int)ConsumerState.Configured);
        Assert.Equal(2, (int)ConsumerState.Running);
    }

    [Fact]
    public void ConsumerState_HasThreeValues()
    {
        var values = Enum.GetValues<ConsumerState>();
        Assert.Equal(3, values.Length);
    }

    [Fact]
    public void HandlerResultCode_HasExpectedValues()
    {
        Assert.Equal(0, (int)HandlerResultCode.Success);
        Assert.Equal(1, (int)HandlerResultCode.TransientError);
        Assert.Equal(2, (int)HandlerResultCode.PermanentError);
        Assert.Equal(3, (int)HandlerResultCode.Cancelled);
    }

    [Fact]
    public void HandlerResultCode_HasFourValues()
    {
        var values = Enum.GetValues<HandlerResultCode>();
        Assert.Equal(4, values.Length);
    }
}
