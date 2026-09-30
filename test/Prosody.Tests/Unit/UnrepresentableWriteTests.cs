using System.Text.Json;
using Prosody.State;
using Prosody.Tests.TestHelpers;

namespace Prosody.Tests.Unit;

/// <summary>
/// Unit tests proving a write of a value with no JSON form classifies transient and leaves the store
/// untouched.
/// </summary>
public sealed class UnrepresentableWriteTests
{
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

    /// <summary>A non-null value whose self-reference is unrepresentable, throwing at serialize time.</summary>
    private sealed class Cyclic
    {
        public Cyclic? Self { get; set; }
    }
}
