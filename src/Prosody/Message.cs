using System.Text.Json;

namespace Prosody;

/// <summary>
/// Represents a Kafka message received from a topic.
/// </summary>
public sealed class Message
{
    /// <summary>
    /// Gets the topic the message was received from.
    /// </summary>
    public required string Topic { get; init; }

    /// <summary>
    /// Gets the partition the message was received from.
    /// </summary>
    public required int Partition { get; init; }

    /// <summary>
    /// Gets the offset of the message within the partition.
    /// </summary>
    public required long Offset { get; init; }

    /// <summary>
    /// Gets the message key.
    /// </summary>
    public required string Key { get; init; }

    /// <summary>
    /// Gets the message timestamp.
    /// </summary>
    public required DateTimeOffset Timestamp { get; init; }

    /// <summary>
    /// Gets the raw JSON payload.
    /// </summary>
    public required JsonElement Payload { get; init; }

    /// <summary>
    /// Deserializes the payload to the specified type.
    /// </summary>
    /// <typeparam name="T">The type to deserialize to.</typeparam>
    /// <param name="options">Optional JSON serializer options.</param>
    /// <returns>The deserialized payload.</returns>
    public T? GetPayload<T>(JsonSerializerOptions? options = null)
    {
        return Payload.Deserialize<T>(options);
    }
}
