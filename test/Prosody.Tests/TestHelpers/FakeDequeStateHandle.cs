using Native = Prosody.Native;

namespace Prosody.Tests.TestHelpers;

/// <summary>
/// An in-memory fake of the internal native deque-state handle. Records whether a write reached the
/// boundary so tests can prove a rejected write left the store untouched.
/// </summary>
internal sealed class FakeDequeStateHandle : Native.IDequeStateHandle
{
    /// <summary>The number of <c>PushBackJson</c> calls.</summary>
    public int PushBackJsonCalls { get; private set; }

    /// <summary>The number of <c>PushFrontJson</c> calls.</summary>
    public int PushFrontJsonCalls { get; private set; }

    public Task PushBackJson(byte[] bytes, Dictionary<string, string> carrier)
    {
        PushBackJsonCalls++;
        return Task.CompletedTask;
    }

    public Task PushFrontJson(byte[] bytes, Dictionary<string, string> carrier)
    {
        PushFrontJsonCalls++;
        return Task.CompletedTask;
    }

    public Task PushBackMessage(Native.Message message, Dictionary<string, string> carrier) => Task.CompletedTask;

    public Task PushFrontMessage(Native.Message message, Dictionary<string, string> carrier) => Task.CompletedTask;

    public Task<Native.StateItem?> PopFront(Dictionary<string, string> carrier) =>
        Task.FromResult<Native.StateItem?>(null);

    public Task<Native.StateItem?> PopBack(Dictionary<string, string> carrier) =>
        Task.FromResult<Native.StateItem?>(null);

    public Task<Native.StateItem?> Get(ulong index, Dictionary<string, string> carrier) =>
        Task.FromResult<Native.StateItem?>(null);

    public Task<ulong> Len(Dictionary<string, string> carrier) => Task.FromResult(0UL);

    public Task<bool> IsEmpty(Dictionary<string, string> carrier) => Task.FromResult(true);

    public Native.StateCursor Scan(Native.ScanDirection direction, Dictionary<string, string> carrier) =>
        throw new NotSupportedException("FakeDequeStateHandle does not support scanning.");

    public Task Clear(Dictionary<string, string> carrier) => Task.CompletedTask;

    public Task Commit(Dictionary<string, string> carrier) => Task.CompletedTask;

    public Task Rollback(Dictionary<string, string> carrier) => Task.CompletedTask;
}
