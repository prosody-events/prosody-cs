using Native = Prosody.Native;

namespace Prosody.Tests.TestHelpers;

/// <summary>
/// An in-memory fake of the internal native map-state handle. Records whether a write reached the
/// boundary so tests can prove a rejected write left the store untouched.
/// </summary>
internal sealed class FakeMapStateHandle : Native.IJsonMapStateHandle
{
    /// <summary>The number of <c>Set</c> calls.</summary>
    public int SetCalls { get; private set; }

    /// <summary>The number of <c>Remove</c> calls.</summary>
    public int RemoveCalls { get; private set; }

    /// <summary>The item returned by <c>Get</c>.</summary>
    public byte[]? GetResult { get; set; }

    /// <summary>The value returned by <c>ContainsKey</c>.</summary>
    public bool ContainsKeyResult { get; set; }

    public Task<byte[]?> Get(string key, Dictionary<string, string> carrier) => Task.FromResult(GetResult);

    public Task<Native.JsonMapValue[]> GetMany(string[] keys, Dictionary<string, string> carrier) =>
        Task.FromResult(Array.ConvertAll(keys, static _ => new Native.JsonMapValue(null)));

    public Task<bool> ContainsKey(string key, Dictionary<string, string> carrier) => Task.FromResult(ContainsKeyResult);

    public Native.MapKeyCursor ScanKeys(Native.ScanDirection direction, Dictionary<string, string> carrier) =>
        throw new NotSupportedException("FakeMapStateHandle does not support key scanning.");

    public Task Set(string key, byte[] bytes, Dictionary<string, string> carrier)
    {
        SetCalls++;
        return Task.CompletedTask;
    }

    public Task Remove(string key, Dictionary<string, string> carrier)
    {
        RemoveCalls++;
        return Task.CompletedTask;
    }

    public Task Clear(Dictionary<string, string> carrier) => Task.CompletedTask;

    public Native.JsonMapCursor Scan(Native.ScanDirection direction, Dictionary<string, string> carrier) =>
        throw new NotSupportedException("FakeMapStateHandle does not support scanning.");

    public Task Commit(Dictionary<string, string> carrier) => Task.CompletedTask;

    public Task Rollback(Dictionary<string, string> carrier) => Task.CompletedTask;
}
