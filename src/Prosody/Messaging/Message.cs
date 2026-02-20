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

        // Cache string properties eagerly to avoid repeated FFI crossings.
        // Each call to the native accessor clones a Rust String, allocates a
        // RustBuffer, and UTF-8 decodes into a C# string.
        Topic = native.Topic();
        Key = native.Key();
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
    public int Partition => _native.Partition();

    /// <summary>
    /// Gets the message offset.
    /// </summary>
    public long Offset => _native.Offset();

    /// <summary>
    /// Gets the message timestamp (UTC).
    /// </summary>
    public DateTimeOffset Timestamp => new(_native.Timestamp(), TimeSpan.Zero);

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
