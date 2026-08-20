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
    public void Value_SetNull_ThrowsNullValueException_TransientCategory()
    {
        var exception = Assert.Throws<NullValueException>(() =>
            StateInterop.SerializeJsonOrThrowNull(null!, TestJson.TypeInfo<string>(), "Use ClearAsync.")
        );

        Assert.Multiple(
            () => Assert.Equal(StateErrorCategory.Transient, exception.Category),
            () => Assert.IsAssignableFrom<TransientStateException>(exception),
            () => Assert.Contains("ClearAsync", exception.Message, StringComparison.Ordinal)
        );
    }

    [Fact]
    public void Map_SetNull_ThrowsNullValueException_NamesRemoveOperation()
    {
        var exception = Assert.Throws<NullValueException>(() =>
            StateInterop.SerializeJsonOrThrowNull(null!, TestJson.TypeInfo<string>(), "Use RemoveAsync.")
        );

        Assert.Multiple(
            () => Assert.Equal(StateErrorCategory.Transient, exception.Category),
            () => Assert.Contains("RemoveAsync", exception.Message, StringComparison.Ordinal)
        );
    }

    [Fact]
    public void Deque_PushNull_ThrowsNullValueException_TransientCategory()
    {
        var exception = Assert.Throws<NullValueException>(() =>
            StateInterop.SerializeJsonOrThrowNull(null!, TestJson.TypeInfo<string>(), "Use ClearAsync.")
        );

        Assert.Equal(StateErrorCategory.Transient, exception.Category);
    }

    [Fact]
    public void Value_SetUnrepresentable_ThrowsTransient()
    {
        var value = new Cyclic();
        value.Self = value;

        var exception = Assert.Throws<TransientStateException>(() =>
            StateInterop.SerializeJsonOrThrowNull(value, TestJson.TypeInfo<Cyclic>(), "Use ClearAsync.")
        );

        Assert.Multiple(
            () => Assert.Equal(StateErrorCategory.Transient, exception.Category),
            () => Assert.IsType<JsonException>(exception.InnerException)
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
