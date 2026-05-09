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
    {
        ArgumentNullException.ThrowIfNull(topic);
        ArgumentNullException.ThrowIfNull(key);

        Topic = topic;
        Key = key;
        Partition = partition;
        Offset = offset;
        Timestamp = timestamp;
        Payload = payload;
    }

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
    /// The value is <see langword="null"/> when the payload JSON token is <c>null</c> and
    /// <typeparamref name="T"/> can represent null. For dynamic-schema topics, use
    /// <c>T = <see cref="System.Text.Json.JsonElement"/></c>.
    /// </remarks>
    public T? Payload { get; }
}
