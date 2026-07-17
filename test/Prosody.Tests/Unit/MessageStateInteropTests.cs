using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Prosody.Messaging;
using Prosody.State;
using Prosody.Tests.TestHelpers;

namespace Prosody.Tests.Unit;

/// <summary>
/// Unit tests proving the message-flavoured write path classifies its caller/input mistakes transient
/// before the write crosses the boundary. All three message Set/Push flavours share
/// <c>MessageInterop.ToNative</c>, so exercising one flavour covers the shared invariant.
/// </summary>
public sealed class MessageStateInteropTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
    };

    private static JsonTypeInfo<T> TypeInfo<T>() => (JsonTypeInfo<T>)JsonOptions.GetTypeInfo(typeof(T));

    [Fact]
    public async Task Set_NullMessage_ThrowsNullValueException_TransientCategory_StoreUntouched()
    {
        var handle = new FakeValueStateHandle();
        var state = new MessageValueState<int>(handle, TypeInfo<int>());

        var exception = await Assert.ThrowsAsync<NullValueException>(() =>
            state.SetAsync(null!, TestContext.Current.CancellationToken)
        );

        Assert.Multiple(
            () => Assert.Equal(StateErrorCategory.Transient, exception.Category),
            () => Assert.Equal(0, handle.SetMessageCalls)
        );
    }

    [Fact]
    public async Task Set_MessageWithoutNativeHandle_ThrowsTransient_StoreUntouched()
    {
        var handle = new FakeValueStateHandle();
        var state = new MessageValueState<int>(handle, TypeInfo<int>());

        // A hand-constructed message carries no native handle, so it cannot be written back.
        var message = new Message<int>("topic", "key", 0, 0, DateTimeOffset.UtcNow, 5);

        var exception = await Assert.ThrowsAsync<TransientStateException>(() =>
            state.SetAsync(message, TestContext.Current.CancellationToken)
        );

        Assert.Multiple(
            () => Assert.Equal(StateErrorCategory.Transient, exception.Category),
            () => Assert.Equal(0, handle.SetMessageCalls)
        );
    }
}
