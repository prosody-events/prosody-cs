namespace Prosody.Messaging;

/// <summary>Metadata for an excise record.</summary>
public sealed class ExciseMessage
{
    internal ExciseMessage(string topic, string key, int partition, long offset, DateTimeOffset timestamp)
    {
        ArgumentNullException.ThrowIfNull(topic);
        ArgumentNullException.ThrowIfNull(key);
        Topic = topic;
        Key = key;
        Partition = partition;
        Offset = offset;
        Timestamp = timestamp;
    }

    /// <summary>Gets the topic name.</summary>
    public string Topic { get; }

    /// <summary>Gets the message key.</summary>
    public string Key { get; }

    /// <summary>Gets the partition number.</summary>
    public int Partition { get; }

    /// <summary>Gets the message offset.</summary>
    public long Offset { get; }

    /// <summary>Gets the record timestamp.</summary>
    public DateTimeOffset Timestamp { get; }
}
