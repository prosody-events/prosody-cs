using Native = Prosody.Native;

namespace Prosody.Tests.TestHelpers;

/// <summary>
/// An in-memory fake of the internal native map-state handle. Records whether a write reached the
/// boundary so tests can prove a rejected write left the store untouched. Records the last native
/// query so tests can check query translation.
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

    /// <summary>The value returned by <c>IsEmpty</c>.</summary>
    public bool IsEmptyResult { get; set; }

    /// <summary>The keys passed to the last <c>ContainsMany</c> call.</summary>
    public string[]? ContainsManyKeys { get; private set; }

    /// <summary>The query passed to the last <c>Entries</c> call.</summary>
    public Native.KeyQuery? EntriesQuery { get; private set; }

    /// <summary>The query passed to the last <c>Keys</c> call.</summary>
    public Native.KeyQuery? KeysQuery { get; private set; }

    /// <summary>The outcome returned by <c>Commit</c>.</summary>
    public Native.StoreOutcome CommitOutcome { get; set; }

    /// <summary>The outcome returned by <c>Rollback</c>.</summary>
    public Native.StoreOutcome RollbackOutcome { get; set; }

    public Task<byte[]?> Get(string key, Dictionary<string, string> carrier) => Task.FromResult(GetResult);

    public Task<Native.JsonMapValue[]> GetMany(string[] keys, Dictionary<string, string> carrier) =>
        Task.FromResult(Array.ConvertAll(keys, static _ => new Native.JsonMapValue(null)));

    public Task<bool> ContainsKey(string key, Dictionary<string, string> carrier) => Task.FromResult(ContainsKeyResult);

    public Task<bool[]> ContainsMany(string[] keys, Dictionary<string, string> carrier)
    {
        ContainsManyKeys = keys;
        return Task.FromResult(Array.ConvertAll(keys, key => key.StartsWith('+')));
    }

    public Task<bool> IsEmpty(Dictionary<string, string> carrier) => Task.FromResult(IsEmptyResult);

    public Native.KeyCursor Keys(Native.KeyQuery query)
    {
        KeysQuery = query;
        throw new NotSupportedException("FakeMapStateHandle does not support key scanning.");
    }

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

    public Native.JsonMapCursor Entries(Native.KeyQuery query)
    {
        EntriesQuery = query;
        throw new NotSupportedException("FakeMapStateHandle does not support scanning.");
    }

    public Task<Native.StoreOutcome> Commit(Dictionary<string, string> carrier) => Task.FromResult(CommitOutcome);

    public Task<Native.StoreOutcome> Rollback(Dictionary<string, string> carrier) => Task.FromResult(RollbackOutcome);
}
