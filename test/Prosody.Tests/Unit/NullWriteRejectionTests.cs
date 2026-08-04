using System.Text.Json;
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
    [Fact]
    public async Task Value_SetNull_ThrowsNullValueException_TransientCategory_StoreUntouched()
    {
        var handle = new FakeJsonValueStateHandle();
        var state = new ValueState<string>(handle, TestJson.TypeInfo<string>());

        var exception = await Assert.ThrowsAsync<NullValueException>(() =>
            state.SetAsync(null!, TestContext.Current.CancellationToken)
        );

        Assert.Multiple(
            () => Assert.Equal(StateErrorCategory.Transient, exception.Category),
            () => Assert.IsAssignableFrom<TransientStateException>(exception),
            () => Assert.Contains("ClearAsync", exception.Message, StringComparison.Ordinal),
            () => Assert.Equal(0, handle.SetCalls)
        );
    }

    [Fact]
    public async Task Map_SetNull_ThrowsNullValueException_NamesRemoveAsync_StoreUntouched()
    {
        var handle = new FakeMapStateHandle();
        var state = new MapState<string>(handle, TestJson.TypeInfo<string>());

        var exception = await Assert.ThrowsAsync<NullValueException>(() =>
            state.SetAsync("k", null!, TestContext.Current.CancellationToken)
        );

        Assert.Multiple(
            () => Assert.Equal(StateErrorCategory.Transient, exception.Category),
            () => Assert.Contains("RemoveAsync", exception.Message, StringComparison.Ordinal),
            () => Assert.Equal(0, handle.SetCalls)
        );
    }

    [Fact]
    public async Task Deque_PushNull_ThrowsNullValueException_TransientCategory_StoreUntouched()
    {
        var handle = new FakeDequeStateHandle();
        var state = new DequeState<string>(handle, TestJson.TypeInfo<string>());

        var exception = await Assert.ThrowsAsync<NullValueException>(() =>
            state.PushBackAsync(null!, TestContext.Current.CancellationToken)
        );

        Assert.Multiple(
            () => Assert.Equal(StateErrorCategory.Transient, exception.Category),
            () => Assert.Equal(0, handle.PushBackCalls)
        );
    }

    [Fact]
    public async Task Value_SetUnrepresentable_ThrowsTransient_StoreUntouched()
    {
        var handle = new FakeJsonValueStateHandle();
        var state = new ValueState<Cyclic>(handle, TestJson.TypeInfo<Cyclic>());

        // A self-referencing graph throws JsonException at serialize time (cycle detected).
        var value = new Cyclic();
        value.Self = value;

        var exception = await Assert.ThrowsAsync<TransientStateException>(() =>
            state.SetAsync(value, TestContext.Current.CancellationToken)
        );

        Assert.Multiple(
            () => Assert.Equal(StateErrorCategory.Transient, exception.Category),
            () => Assert.IsType<JsonException>(exception.InnerException),
            () => Assert.Equal(0, handle.SetCalls)
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

    /// <summary>A non-null value whose self-reference is unrepresentable, throwing at serialize time.</summary>
    private sealed class Cyclic
    {
        public Cyclic? Self { get; set; }
    }
}
