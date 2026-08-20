using Prosody.State;
using Native = Prosody.Native;

namespace Prosody.Tests.Unit;

/// <summary>
/// Unit tests proving <c>StateInterop.Translate</c> recovers the error category from the generated
/// exception <b>type</b>: a native permanent failure surfaces as a <see cref="PermanentStateException"/>
/// with <see cref="StateErrorCategory.Permanent"/>. Swapping the two Translate arms turns this red.
/// </summary>
public sealed class StateInteropTranslateTests
{
    [Fact]
    public void NativePermanentFailure_SurfacesAsPermanentStateException()
    {
        var native = new Native.FfiErrorException(new Native.FfiError.PermanentState("boom"));

        var exception = Assert.IsType<PermanentStateException>(StateInterop.Translate(native));

        Assert.Equal(StateErrorCategory.Permanent, exception.Category);
    }
}
