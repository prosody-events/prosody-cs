using Native = Prosody.Native;

namespace Prosody.Tests.TestHelpers;

/// <summary>
/// An in-memory fake of the internal native value-state handle. Records whether a write reached the
/// boundary so tests can prove a rejected write left the store untouched.
/// </summary>
internal sealed class FakeValueStateHandle : Native.IValueStateHandle
{
    /// <summary>The number of <c>SetJson</c> calls.</summary>
    public int SetJsonCalls { get; private set; }

    /// <summary>The number of <c>SetMessage</c> calls.</summary>
    public int SetMessageCalls { get; private set; }

    /// <summary>The bytes of the most recent <c>SetJson</c> call.</summary>
    public byte[]? LastSetBytes { get; private set; }

    /// <summary>The item returned by <c>Get</c>.</summary>
    public Native.StateItem? GetResult { get; set; }

    /// <summary>The trace-propagation carrier of the most recent operation.</summary>
    public Dictionary<string, string>? LastCarrier { get; private set; }

    public Task<Native.StateItem?> Get(Dictionary<string, string> carrier)
    {
        LastCarrier = carrier;
        return Task.FromResult(GetResult);
    }

    public Task SetJson(byte[] bytes, Dictionary<string, string> carrier)
    {
        SetJsonCalls++;
        LastSetBytes = bytes;
        LastCarrier = carrier;
        return Task.CompletedTask;
    }

    public Task SetMessage(Native.Message message, Dictionary<string, string> carrier)
    {
        SetMessageCalls++;
        return Task.CompletedTask;
    }

    public Task Clear(Dictionary<string, string> carrier) => Task.CompletedTask;

    public Task Commit(Dictionary<string, string> carrier) => Task.CompletedTask;

    public Task Rollback(Dictionary<string, string> carrier) => Task.CompletedTask;
}
