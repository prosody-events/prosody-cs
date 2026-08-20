namespace Prosody.State;

/// <summary>Owns one native message map entry.</summary>
internal sealed class MessageMapEntry : IDisposable
{
    internal MessageMapEntry(Native.MessageBatch batch, int index)
    {
        Key = batch.KeyAt((ulong)index);
        Message =
            batch.MessageAt((ulong)index)
            ?? throw new TransientStateException("A message map scan returned an empty slot.");
    }

    internal string Key { get; }
    internal Native.Message Message { get; }

    public void Dispose() => Message.Dispose();
}
