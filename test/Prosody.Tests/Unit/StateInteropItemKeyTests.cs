using Prosody.State;
using Native = Prosody.Native;

namespace Prosody.Tests.Unit;

/// <summary>
/// Unit tests for <c>StateInterop.ItemKey</c>, the key-only scan transform: it returns the key of a
/// <see cref="Native.StateScanItem.MapKey"/> and rejects any other item shape as transient (so a
/// wrong-shape binding invariant retries rather than dropping the message).
/// </summary>
public sealed class StateInteropItemKeyTests
{
    [Fact]
    public void ItemKey_MapKey_ReturnsKey()
    {
        using Native.StateScanItem item = new Native.StateScanItem.MapKey("k1");
        Assert.Equal("k1", StateInterop.ItemKey(item));
    }

    [Fact]
    public void ItemKey_WrongShape_ThrowsTransient()
    {
        using Native.StateScanItem item = new Native.StateScanItem.MapJson("k1", [1, 2, 3]);
        Assert.Throws<TransientStateException>(() => StateInterop.ItemKey(item));
    }
}
