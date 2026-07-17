using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
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
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
    };

    private static JsonTypeInfo<T> TypeInfo<T>() => (JsonTypeInfo<T>)JsonOptions.GetTypeInfo(typeof(T));

    [Fact]
    public async Task NativePermanentFailure_SurfacesAsPermanentStateException()
    {
        var handle = new PermanentFaultingValueStateHandle();
        var state = new ValueState<int>(handle, TypeInfo<int>());

        var exception = await Assert.ThrowsAsync<PermanentStateException>(() =>
            state.CommitAsync(TestContext.Current.CancellationToken)
        );

        Assert.Equal(StateErrorCategory.Permanent, exception.Category);
    }

    /// <summary>A native value handle whose <c>Commit</c> raises a generated permanent state failure.</summary>
    private sealed class PermanentFaultingValueStateHandle : Native.IValueStateHandle
    {
        public Task<Native.StateItem?> Get(Dictionary<string, string> carrier) =>
            Task.FromResult<Native.StateItem?>(null);

        public Task SetJson(byte[] bytes, Dictionary<string, string> carrier) => Task.CompletedTask;

        public Task SetMessage(Native.Message message, Dictionary<string, string> carrier) => Task.CompletedTask;

        public Task Clear(Dictionary<string, string> carrier) => Task.CompletedTask;

        public Task Commit(Dictionary<string, string> carrier) => throw new Native.FfiException.PermanentState("boom");

        public Task Rollback(Dictionary<string, string> carrier) => Task.CompletedTask;
    }
}
