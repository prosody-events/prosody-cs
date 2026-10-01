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
        Native.Message? nativeHandle,
        string? sourceSystem = null,
        bool isResponseRequested = false
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
        SourceSystem = sourceSystem;
        IsResponseRequested = isResponseRequested;
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
    /// Gets the source system that produced the message, or <see langword="null"/> when its headers
    /// name none.
    /// </summary>
    public string? SourceSystem { get; }

    /// <summary>
    /// Gets a value indicating whether the sender waits for a response to this message. A handler can
    /// skip the work that only a response needs when this value is <see langword="false"/>.
    /// </summary>
    public bool IsResponseRequested { get; }

    /// <summary>
    /// Gets the deserialized JSON payload.
    /// </summary>
    /// <remarks>A JSON null produces null when <typeparamref name="T"/> permits null.</remarks>
    public T? Payload { get; }
}
