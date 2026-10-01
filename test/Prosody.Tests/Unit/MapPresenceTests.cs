using Prosody.State;
using Prosody.Tests.TestHelpers;

namespace Prosody.Tests.Unit;

/// <summary>Verifies that map emptiness and batch presence call the matching native operations.</summary>
public sealed class MapPresenceTests
{
    [Fact]
    public async Task MapEmptinessAndBatchPresenceUseTheNativeOperations()
    {
        var handle = new FakeMapStateHandle { IsEmptyResult = true };
        var map = new MapState<int>(handle, TestJson.TypeInfo<int>());
        var cancellationToken = TestContext.Current.CancellationToken;

        var isEmpty = await map.IsEmptyAsync(cancellationToken);
        var present = await map.ContainsManyAsync(["+a", "b", "+c"], cancellationToken);

        Assert.Multiple(
            () => Assert.True(isEmpty),
            () => Assert.Equal([true, false, true], present),
            () => Assert.Equal(["+a", "b", "+c"], handle.ContainsManyKeys ?? [])
        );
    }
}
