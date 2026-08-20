using Prosody.Messaging;
using Prosody.State;

namespace Prosody.Tests.Unit;

/// <summary>
/// Unit tests proving the message-flavoured write path classifies its caller/input mistakes transient
/// before the write crosses the boundary. All three message Set/Push flavours share
/// <c>MessageInterop.ToNative</c>, so exercising one flavour covers the shared invariant.
/// </summary>
public sealed class MessageStateInteropTests
{
    [Fact]
    public void Set_NullMessage_ThrowsNullValueException_TransientCategory()
    {
        var exception = Assert.Throws<NullValueException>(() => MessageInterop.ToNative<int>(null!));

        Assert.Equal(StateErrorCategory.Transient, exception.Category);
    }

    [Fact]
    public void Set_MessageWithoutNativeHandle_ThrowsTransient()
    {
        var message = new Message<int>("topic", "key", 0, 0, DateTimeOffset.UtcNow, 5);

        var exception = Assert.Throws<TransientStateException>(() => MessageInterop.ToNative(message));

        Assert.Equal(StateErrorCategory.Transient, exception.Category);
    }
}
