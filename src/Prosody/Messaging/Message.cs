using System.Text.Json;

namespace Prosody.Messaging;

/// <summary>
/// Kafka message data.
/// </summary>
public sealed class Message
{
    private readonly Native.Message _native;

    internal Message(Native.Message native)
    {
        ArgumentNullException.ThrowIfNull(native);
        _native = native;

        // Cache all properties eagerly to avoid repeated FFI crossings.
        // Each call to a native accessor crosses the FFI boundary (Arc clone +
        // method dispatch + atomic bookkeeping); primitives are cheap to cache
        // once and avoid that overhead on repeated access.
        Topic = native.Topic();
        Key = native.Key();
        Partition = native.Partition();
        Offset = native.Offset();
        Timestamp = new(native.Timestamp(), TimeSpan.Zero);
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
    /// Deserializes the payload to the specified type.
    /// </summary>
    /// <typeparam name="T">The type to deserialize to.</typeparam>
    /// <returns>The deserialized payload.</returns>
    /// <exception cref="JsonException">If deserialization fails.</exception>
    public T GetPayload<T>()
    {
        return JsonSerializer.Deserialize<T>(_native.Payload())
            ?? throw new JsonException($"Failed to deserialize payload as {typeof(T).Name}");
    }
}
