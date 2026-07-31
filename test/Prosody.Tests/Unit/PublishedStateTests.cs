using Prosody.State;
using Prosody.Tests.TestHelpers;
using Native = Prosody.Native;

namespace Prosody.Tests.Unit;

/// <summary>Verifies published handles expose the owned read operations for an explicit state key.</summary>
public sealed class PublishedStateTests
{
    [Fact]
    public async Task MapContainsKeyUsesTheTypedNativeOperation()
    {
        var handle = new PublishedMapHandle();
        var state = new PublishedMap<int>(handle, TestJson.TypeInfo<int>());
        var cancellationToken = TestContext.Current.CancellationToken;

        Assert.True(await state.ContainsKeyAsync("user-1", "item", cancellationToken));
        Assert.Equal(("user-1", "item"), handle.ContainsRequest);
    }

    [Fact]
    public async Task DequeEndpointReadsUseTheTypedNativeOperations()
    {
        var state = new PublishedDeque<string>(new PublishedDequeHandle(), TestJson.TypeInfo<string>());
        var cancellationToken = TestContext.Current.CancellationToken;

        var front = await state.PeekFrontAsync("user-1", cancellationToken);
        var back = await state.PeekBackAsync("user-1", cancellationToken);
        var isEmpty = await state.IsEmptyAsync("user-1", cancellationToken);

        Assert.Multiple(
            () => Assert.False(isEmpty),
            () => Assert.Equal("front", front.Value),
            () => Assert.Equal("back", back.Value)
        );
    }

    private sealed class PublishedMapHandle : Native.IPublishedMapHandle
    {
        internal (string Key, string MapKey)? ContainsRequest { get; private set; }

        public Task<bool> ContainsKey(string key, string mapKey, Dictionary<string, string> carrier)
        {
            ContainsRequest = (key, mapKey);
            return Task.FromResult(true);
        }

        public Task<Native.StateItem?> Get(string key, string mapKey, Dictionary<string, string> carrier) =>
            Task.FromResult<Native.StateItem?>(null);

        public Task<Native.StateItem?[]> GetMany(string key, string[] mapKeys, Dictionary<string, string> carrier) =>
            Task.FromResult(Array.Empty<Native.StateItem?>());

        public Task<Native.StateCursor> Keys(
            string key,
            Native.ScanDirection directionValue,
            Dictionary<string, string> carrier
        ) => throw new NotSupportedException();

        public Task<Native.StateCursor> Scan(
            string key,
            Native.ScanDirection directionValue,
            Dictionary<string, string> carrier
        ) => throw new NotSupportedException();
    }

    private sealed class PublishedDequeHandle : Native.IPublishedDequeHandle
    {
        public Task<Native.StateItem?> Get(string key, ulong index, Dictionary<string, string> carrier) =>
            Task.FromResult<Native.StateItem?>(null);

        public Task<bool> IsEmpty(string key, Dictionary<string, string> carrier) => Task.FromResult(false);

        public Task<ulong> Len(string key, Dictionary<string, string> carrier) => Task.FromResult(2UL);

        public Task<Native.StateItem?> PeekBack(string key, Dictionary<string, string> carrier) =>
            Task.FromResult<Native.StateItem?>(new Native.StateItem.Json("\"back\""u8.ToArray()));

        public Task<Native.StateItem?> PeekFront(string key, Dictionary<string, string> carrier) =>
            Task.FromResult<Native.StateItem?>(new Native.StateItem.Json("\"front\""u8.ToArray()));

        public Task<Native.StateCursor> Scan(
            string key,
            Native.ScanDirection directionValue,
            Dictionary<string, string> carrier
        ) => throw new NotSupportedException();
    }
}
