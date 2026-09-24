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

    [Fact]
    public async Task MapEmptinessAndBatchPresenceUseTheTypedNativeOperations()
    {
        var state = new PublishedMap<int>(new PublishedMapHandle(), TestJson.TypeInfo<int>());
        var cancellationToken = TestContext.Current.CancellationToken;

        var empty = await state.IsEmptyAsync("empty", cancellationToken);
        var full = await state.IsEmptyAsync("user-1", cancellationToken);
        var present = await state.ContainsManyAsync("user-1", ["item", "other"], cancellationToken);

        Assert.Multiple(() => Assert.True(empty), () => Assert.False(full), () => Assert.Equal([true, false], present));
    }

    [Fact]
    public async Task SetReadsUseTheTypedNativeOperations()
    {
        var handle = new PublishedSetHandle();
        var state = new PublishedSet(handle);
        var cancellationToken = TestContext.Current.CancellationToken;
        var query = new KeyQuery { Prefix = "t", Limit = 1 };

        var contains = await state.ContainsAsync("user-1", "tag", cancellationToken);
        var present = await state.ContainsManyAsync("user-1", ["tag", "other"], cancellationToken);
        var empty = await state.IsEmptyAsync("user-2", cancellationToken);
        Assert.Throws<NotSupportedException>(() =>
            state.EnumerateAsync("user-3", query, cancellationToken).GetAsyncEnumerator(cancellationToken)
        );

        Assert.Multiple(
            () => Assert.True(contains),
            () => Assert.Equal([true, false], present),
            () => Assert.True(empty),
            () =>
                Assert.Equal(
                    ["contains:user-1:tag", "contains_many:user-1:tag,other", "is_empty:user-2"],
                    handle.Calls
                ),
            () => Assert.Equal(("user-3", KeyQuery.ToNative(query)), handle.KeysRequest)
        );
    }

    [Fact]
    public void DequeEnumerationPassesThePositionQuery()
    {
        var handle = new PublishedDequeHandle();
        var state = new PublishedDeque<string>(handle, TestJson.TypeInfo<string>());
        var cancellationToken = TestContext.Current.CancellationToken;
        var query = new PositionQuery { After = 3, Limit = 2 };

        Assert.Throws<NotSupportedException>(() =>
            state.EnumerateAsync("user-1", query, cancellationToken).GetAsyncEnumerator(cancellationToken)
        );

        Assert.Equal(("user-1", PositionQuery.ToNative(query)), handle.ValuesRequest);
    }

    private sealed class PublishedSetHandle : Native.IPublishedSetHandle
    {
        internal List<string> Calls { get; } = [];

        internal (string Key, Native.KeyQuery Query)? KeysRequest { get; private set; }

        public Task<bool> Contains(string key, string member, Dictionary<string, string> carrier)
        {
            Calls.Add($"contains:{key}:{member}");
            return Task.FromResult(member == "tag");
        }

        public Task<bool[]> ContainsMany(string key, string[] members, Dictionary<string, string> carrier)
        {
            Calls.Add($"contains_many:{key}:{string.Join(',', members)}");
            return Task.FromResult(Array.ConvertAll(members, member => member == "tag"));
        }

        public Task<bool> IsEmpty(string key, Dictionary<string, string> carrier)
        {
            Calls.Add($"is_empty:{key}");
            return Task.FromResult(true);
        }

        public Native.KeyCursor Keys(string key, Native.KeyQuery query)
        {
            KeysRequest = (key, query);
            throw new NotSupportedException();
        }
    }

    private sealed class PublishedMapHandle : Native.IPublishedMapHandle
    {
        internal (string Key, string MapKey)? ContainsRequest { get; private set; }

        public Task<bool> ContainsKey(string key, string mapKey, Dictionary<string, string> carrier)
        {
            ContainsRequest = (key, mapKey);
            return Task.FromResult(true);
        }

        public Task<byte[]?> Get(string key, string mapKey, Dictionary<string, string> carrier) =>
            Task.FromResult<byte[]?>(null);

        public Task<Native.JsonMapValue[]> GetMany(string key, string[] mapKeys, Dictionary<string, string> carrier) =>
            Task.FromResult(Array.Empty<Native.JsonMapValue>());

        public Task<bool[]> ContainsMany(string key, string[] mapKeys, Dictionary<string, string> carrier) =>
            Task.FromResult(Array.ConvertAll(mapKeys, mapKey => mapKey == "item"));

        public Task<bool> IsEmpty(string key, Dictionary<string, string> carrier) => Task.FromResult(key == "empty");

        public Native.KeyCursor Keys(string key, Native.KeyQuery query) => throw new NotSupportedException();

        public Native.JsonMapCursor Entries(string key, Native.KeyQuery query) => throw new NotSupportedException();
    }

    private sealed class PublishedDequeHandle : Native.IPublishedDequeHandle
    {
        public Task<byte[]?> Get(string key, ulong index, Dictionary<string, string> carrier) =>
            Task.FromResult<byte[]?>(null);

        public Task<bool> IsEmpty(string key, Dictionary<string, string> carrier) => Task.FromResult(false);

        public Task<ulong> Len(string key, Dictionary<string, string> carrier) => Task.FromResult(2UL);

        public Task<byte[]?> PeekBack(string key, Dictionary<string, string> carrier) =>
            Task.FromResult<byte[]?>("\"back\""u8.ToArray());

        public Task<byte[]?> PeekFront(string key, Dictionary<string, string> carrier) =>
            Task.FromResult<byte[]?>("\"front\""u8.ToArray());

        internal (string Key, Native.PositionQuery Query)? ValuesRequest { get; private set; }

        public Native.JsonDequeCursor Values(string key, Native.PositionQuery query)
        {
            ValuesRequest = (key, query);
            throw new NotSupportedException();
        }
    }
}
