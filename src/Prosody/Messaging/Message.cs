namespace Prosody.Messaging;

/// <summary>
/// Kafka message data with a deserialized JSON payload. All members are safe for concurrent read access.
/// </summary>
/// <typeparam name="T">The payload type.</typeparam>
/// <remarks>
/// This type is produced by subscriptions through <see cref="IProsodyHandler{TPayload}"/>.
/// The payload is deserialized once before the handler is invoked.
/// For topics with dynamic or mixed schemas, use <c>T = <see cref="System.Text.Json.JsonElement"/></c>.
/// </remarks>
public sealed class Message<T>
{
    internal Message(string topic, string key, int partition, long offset, DateTimeOffset timestamp, T? payload)
        : this(topic, key, partition, offset, timestamp, payload, nativeHandle: null) { }

    internal Message(
        string topic,
        string key,
        int partition,
        long offset,
        DateTimeOffset timestamp,
        T? payload,
        Native.Message? nativeHandle
    )
    {
        ArgumentNullException.ThrowIfNull(topic);
        ArgumentNullException.ThrowIfNull(key);

        Topic = topic;
        Key = key;
        Partition = partition;
        Offset = offset;
        Timestamp = timestamp;
        Payload = payload;
        NativeHandle = nativeHandle;
    }

    /// <summary>
    /// The native message handle, retained so a received message can be written back to a
    /// message-flavoured keyed-state collection. Never exposed on the public surface.
    /// </summary>
    internal Native.Message? NativeHandle { get; }

    /// <summary>
    /// Gets the topic name.
    /// </summary>
    public string Topic { get; }

    /// <summary>
    /// Gets the message key.
    /// </summary>
    public string Key { get; }

    /// <summary>
    /// Gets the partition number.
    /// </summary>
    public int Partition { get; }

    /// <summary>
    /// Gets the message offset.
    /// </summary>
    public long Offset { get; }

    /// <summary>
    /// Gets the message timestamp (UTC).
    /// </summary>
    public DateTimeOffset Timestamp { get; }

    /// <summary>
    /// Gets the deserialized JSON payload.
    /// </summary>
    /// <remarks>
    /// The value is <see langword="null"/> for an excise record.
    /// A JSON null payload also produces null when <typeparamref name="T"/> permits null.
    /// </remarks>
    public T? Payload { get; }
}
