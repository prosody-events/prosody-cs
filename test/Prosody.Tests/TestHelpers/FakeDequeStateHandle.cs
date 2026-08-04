using Native = Prosody.Native;

namespace Prosody.Tests.TestHelpers;

/// <summary>
/// An in-memory fake of the internal native deque-state handle. Records whether a write reached the
/// boundary so tests can prove a rejected write left the store untouched.
/// </summary>
internal sealed class FakeDequeStateHandle : Native.IJsonDequeStateHandle
{
    /// <summary>The number of <c>PushBack</c> calls.</summary>
    public int PushBackCalls { get; private set; }

    /// <summary>The number of <c>PushFront</c> calls.</summary>
    public int PushFrontCalls { get; private set; }

    public Task PushBack(byte[] bytes, Dictionary<string, string> carrier)
    {
        PushBackCalls++;
        return Task.CompletedTask;
    }

    public Task PushFront(byte[] bytes, Dictionary<string, string> carrier)
    {
        PushFrontCalls++;
        return Task.CompletedTask;
    }

    public Task<byte[]?> PopFront(Dictionary<string, string> carrier) => Task.FromResult<byte[]?>(null);

    public Task<byte[]?> PopBack(Dictionary<string, string> carrier) => Task.FromResult<byte[]?>(null);

    public Task<byte[]?> Get(ulong index, Dictionary<string, string> carrier) => Task.FromResult<byte[]?>(null);

    public Task<byte[]?> PeekFront(Dictionary<string, string> carrier) => Task.FromResult<byte[]?>(null);

    public Task<byte[]?> PeekBack(Dictionary<string, string> carrier) => Task.FromResult<byte[]?>(null);

    public Task<ulong> Len(Dictionary<string, string> carrier) => Task.FromResult(0UL);

    public Task<bool> IsEmpty(Dictionary<string, string> carrier) => Task.FromResult(true);

    public Native.JsonDequeCursor Scan(Native.ScanDirection direction, Dictionary<string, string> carrier) =>
        throw new NotSupportedException("FakeDequeStateHandle does not support scanning.");

    public Task Clear(Dictionary<string, string> carrier) => Task.CompletedTask;

    public Task Commit(Dictionary<string, string> carrier) => Task.CompletedTask;

    public Task Rollback(Dictionary<string, string> carrier) => Task.CompletedTask;
}
