using Native = Prosody.Native;

namespace Prosody.Tests.TestHelpers;

/// <summary>
/// An in-memory fake of the internal native set-state handle. It records each call as a line in
/// <see cref="Calls"/> so tests can check method wiring.
/// </summary>
internal sealed class FakeSetStateHandle : Native.ISetStateHandle
{
    /// <summary>The calls in order, such as <c>insert:a</c>.</summary>
    public List<string> Calls { get; } = [];

    /// <summary>The query passed to the last <c>Keys</c> call.</summary>
    public Native.KeyQuery? KeysQuery { get; private set; }

    /// <summary>The outcome returned by <c>Commit</c>.</summary>
    public Native.StoreOutcome CommitOutcome { get; set; }

    /// <summary>The outcome returned by <c>Rollback</c>.</summary>
    public Native.StoreOutcome RollbackOutcome { get; set; }

    public Task Clear(Dictionary<string, string> carrier)
    {
        Calls.Add("clear");
        return Task.CompletedTask;
    }

    public Task<Native.StoreOutcome> Commit(Dictionary<string, string> carrier)
    {
        Calls.Add("commit");
        return Task.FromResult(CommitOutcome);
    }

    public Task<bool> Contains(string member, Dictionary<string, string> carrier)
    {
        Calls.Add($"contains:{member}");
        return Task.FromResult(member == "present");
    }

    public Task<bool[]> ContainsMany(string[] members, Dictionary<string, string> carrier)
    {
        Calls.Add($"contains_many:{string.Join(',', members)}");
        return Task.FromResult(Array.ConvertAll(members, member => member == "present"));
    }

    public Task Insert(string member, Dictionary<string, string> carrier)
    {
        Calls.Add($"insert:{member}");
        return Task.CompletedTask;
    }

    public Task<bool> IsEmpty(Dictionary<string, string> carrier)
    {
        Calls.Add("is_empty");
        return Task.FromResult(true);
    }

    public Native.KeyCursor Keys(Native.KeyQuery query)
    {
        KeysQuery = query;
        throw new NotSupportedException("FakeSetStateHandle does not support scanning.");
    }

    public Task Remove(string member, Dictionary<string, string> carrier)
    {
        Calls.Add($"remove:{member}");
        return Task.CompletedTask;
    }

    public Task<Native.StoreOutcome> Rollback(Dictionary<string, string> carrier)
    {
        Calls.Add("rollback");
        return Task.FromResult(RollbackOutcome);
    }
}
