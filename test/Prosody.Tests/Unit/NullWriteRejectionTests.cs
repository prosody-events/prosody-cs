using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Prosody.Errors;
using Prosody.State;
using Prosody.Tests.TestHelpers;

namespace Prosody.Tests.Unit;

/// <summary>
/// Unit tests proving a null write is rejected before it crosses the boundary, classifies transient,
/// and leaves the store untouched.
/// </summary>
public sealed class NullWriteRejectionTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
    };

    private static JsonTypeInfo<T> TypeInfo<T>() => (JsonTypeInfo<T>)JsonOptions.GetTypeInfo(typeof(T));

    [Fact]
    public async Task Value_SetNull_ThrowsNullValueException_TransientCategory_StoreUntouched()
    {
        var handle = new FakeValueStateHandle();
        var state = new ValueState<string>(handle, TypeInfo<string>());

        var exception = await Assert.ThrowsAsync<NullValueException>(() =>
            state.SetAsync(null!, TestContext.Current.CancellationToken)
        );

        Assert.Multiple(
            () => Assert.Equal(StateErrorCategory.Transient, exception.Category),
            () => Assert.IsAssignableFrom<TransientStateException>(exception),
            () => Assert.Contains("ClearAsync", exception.Message, StringComparison.Ordinal),
            () => Assert.Equal(0, handle.SetJsonCalls)
        );
    }

    [Fact]
    public async Task Map_SetNull_ThrowsNullValueException_NamesRemoveAsync_StoreUntouched()
    {
        var handle = new FakeMapStateHandle();
        var state = new MapState<string>(handle, TypeInfo<string>());

        var exception = await Assert.ThrowsAsync<NullValueException>(() =>
            state.SetAsync("k", null!, TestContext.Current.CancellationToken)
        );

        Assert.Multiple(
            () => Assert.Equal(StateErrorCategory.Transient, exception.Category),
            () => Assert.Contains("RemoveAsync", exception.Message, StringComparison.Ordinal),
            () => Assert.Equal(0, handle.SetJsonCalls)
        );
    }

    [Fact]
    public void NullValueException_IsTransient_NotPermanent()
    {
        // Typed as the base so the `is IPermanentError` check is a runtime test the falsification
        // target (reparenting NullValueException under the permanent type) can flip.
        StateException exception = new NullValueException("boom");

        Assert.Multiple(
            () => Assert.IsAssignableFrom<TransientStateException>(exception),
            () => Assert.False(exception is IPermanentError),
            () => Assert.Equal(StateErrorCategory.Transient, exception.Category)
        );
    }
}
