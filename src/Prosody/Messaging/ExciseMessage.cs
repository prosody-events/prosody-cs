namespace Prosody.Messaging;

/// <summary>Metadata for an excise record.</summary>
public sealed class ExciseMessage
{
    internal ExciseMessage(
        string topic,
        string key,
        int partition,
        long offset,
        DateTimeOffset timestamp,
        string? sourceSystem = null,
        bool responseRequested = false
    )
    {
        ArgumentNullException.ThrowIfNull(topic);
        ArgumentNullException.ThrowIfNull(key);
        Topic = topic;
        Key = key;
        Partition = partition;
        Offset = offset;
        Timestamp = timestamp;
        SourceSystem = sourceSystem;
        ResponseRequested = responseRequested;
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

    /// <summary>
    /// Gets the source system that produced the record, or <see langword="null"/> when its headers
    /// name none.
    /// </summary>
    public string? SourceSystem { get; }

    /// <summary>Gets a value indicating whether the sender waits for a response to this record.</summary>
    public bool ResponseRequested { get; }
}
