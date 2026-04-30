using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Prosody.Messaging;

/// <summary>
/// Kafka message data. All members are safe for concurrent read access.
/// </summary>
public sealed class Message
{
    private readonly byte[] _payload;
    private readonly JsonSerializerOptions _options;

    internal Message(Native.Message native, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(native);
        ArgumentNullException.ThrowIfNull(options);

        // Cache all properties eagerly to avoid repeated FFI crossings.
        // Each call to a native accessor crosses the FFI boundary (Arc clone +
        // method dispatch + atomic bookkeeping); primitives are cheap to cache
        // once and avoid that overhead on repeated access.
        // The payload copy is unconditional — even consumers that only inspect
        // metadata (topic, key, offset) pay one buffer allocation at construction.
        // This is intentional: lazy access to Native.Message after the handler
        // scope ends causes ObjectDisposedException.
        Topic = native.Topic();
        Key = native.Key();
        Partition = native.Partition();
        Offset = native.Offset();
        Timestamp = new(native.Timestamp(), TimeSpan.Zero);
        _payload = native.Payload();
        _options = options;
    }

    // Native.Message is a sealed FFI type that cannot be faked in tests.
    internal Message(
        string topic,
        string key,
        int partition,
        long offset,
        DateTimeOffset timestamp,
        byte[] payload,
        JsonSerializerOptions options
    )
    {
        ArgumentNullException.ThrowIfNull(topic);
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(options);

        Topic = topic;
        Key = key;
        Partition = partition;
        Offset = offset;
        Timestamp = timestamp;
        _payload = payload;
        _options = options;
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
    /// The encoded payload bytes as produced by the FFI layer.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The returned <see cref="ReadOnlyMemory{T}"/> wraps the internal <c>byte[]</c> directly
    /// (zero copy). Callers that recover the backing array via
    /// <see cref="System.Runtime.InteropServices.MemoryMarshal.TryGetArray{T}"/> MUST NOT
    /// mutate the array; doing so corrupts subsequent <see cref="RawPayload"/> and
    /// <see cref="GetPayload{T}"/> calls and is undefined behavior.
    /// </para>
    /// <para>
    /// Suitable for zero-copy streaming into <see cref="System.Text.Json.Utf8JsonReader"/>,
    /// <c>PipeWriter</c>, <c>Stream.WriteAsync</c>, etc.
    /// </para>
    /// </remarks>
    public ReadOnlyMemory<byte> RawPayload => _payload;

    /// <summary>
    /// Deserializes the payload using the client's configured <see cref="JsonSerializerOptions"/>.
    /// </summary>
    /// <typeparam name="T">The type to deserialize to.</typeparam>
    /// <returns>The deserialized payload, or <c>null</c> if the JSON token is <c>null</c>.</returns>
    /// <exception cref="JsonException">If deserialization fails.</exception>
    /// <remarks>
    /// Options are set at client construction via
    /// <see cref="ProsodyClientBuilder.ConfigureJsonSerializer"/>. To use a custom
    /// <see cref="System.Text.Json.Serialization.JsonSerializerContext"/> for AOT/trim-safe
    /// deserialization, supply it through <c>ConfigureJsonSerializer</c>.
    /// </remarks>
    public T? GetPayload<T>()
    {
        var typeInfo = (JsonTypeInfo<T>)_options.GetTypeInfo(typeof(T));
        return JsonSerializer.Deserialize(_payload.AsSpan(), typeInfo);
    }
}
