using Native = Prosody.Native;

namespace Prosody.Tests.TestHelpers;

/// <summary>Records calls to a native Kafka-message value-state handle.</summary>
internal sealed class FakeMessageValueStateHandle : Native.IMessageValueStateHandle
{
    public int SetCalls { get; private set; }

    public Native.Message? GetResult { get; set; }

    public Task<Native.Message?> Get(Dictionary<string, string> carrier) => Task.FromResult(GetResult);

    public Task Set(Native.Message message, Dictionary<string, string> carrier)
    {
        SetCalls++;
        return Task.CompletedTask;
    }

    public Task Clear(Dictionary<string, string> carrier) => Task.CompletedTask;

    public Task<Native.StoreOutcome> Commit(Dictionary<string, string> carrier) =>
        Task.FromResult(Native.StoreOutcome.Applied);

    public Task<Native.StoreOutcome> Rollback(Dictionary<string, string> carrier) =>
        Task.FromResult(Native.StoreOutcome.NoOp);
}
