namespace Prosody.State;

/// <summary>
/// An immutable, validated declaration of a keyed-state collection.
/// </summary>
/// <remarks>
/// <para>
/// A definition is the single source of typing: the same object is registered via
/// <see cref="ProsodyClientBuilder.WithStateCollections"/> and passed to a <c>State</c> overload on
/// <c>ProsodyContext</c> to bind a typed handle. Construct definitions through the static factories
/// (<see cref="Value{T}"/>, <see cref="Map{TValue}"/>, <see cref="Deque{T}"/>,
/// <see cref="MessageValue{TPayload}"/>, <see cref="MessageMap{TPayload}"/>,
/// <see cref="MessageDeque{TPayload}"/>).
/// </para>
/// <para>
/// Per-definition rules (name non-empty; TTL whole seconds in <c>1..=630_720_000</c>; keyset limit
/// in <c>0..=4096</c>) are enforced here at construction; set-level rules (name uniqueness, TTL
/// exceeding the recovery delay) are enforced when the client options are validated.
/// </para>
/// </remarks>
public abstract record StateDefinition
{
    /// <summary>The maximum TTL in seconds accepted by the Cassandra backing store.</summary>
    private const long _maxTtlSeconds = 630_720_000;

    /// <summary>The inclusive upper bound for a map keyset limit.</summary>
    private const int _maxKeysetLimit = 4096;

    private protected StateDefinition(
        string name,
        Native.StateKind kind,
        Native.StatePayload payload,
        TimeSpan? ttl,
        bool? readUncommitted,
        int? keysetLimit
    )
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("State collection name must be non-empty.", nameof(name));
        }

        name = name.Trim();

        if (ttl is { } t)
        {
            ValidateTtl(t);
        }

        if (keysetLimit is { } k && k is < 0 or > _maxKeysetLimit)
        {
            throw new ArgumentOutOfRangeException(
                nameof(keysetLimit),
                k,
                $"Keyset limit must be between 0 and {_maxKeysetLimit}."
            );
        }

        Name = name;
        Kind = kind;
        Payload = payload;
        Ttl = ttl;
        ReadUncommitted = readUncommitted;
        KeysetLimit = keysetLimit;
    }

    /// <summary>Gets the collection name. Non-empty and unique within the client's definition set.</summary>
    public string Name { get; }

    internal Native.StateKind Kind { get; }

    internal Native.StatePayload Payload { get; }

    internal TimeSpan? Ttl { get; }

    internal bool? ReadUncommitted { get; }

    internal int? KeysetLimit { get; }

    /// <summary>
    /// Declares a single-value JSON collection.
    /// </summary>
    /// <typeparam name="T">The stored value type.</typeparam>
    /// <param name="name">The collection name.</param>
    /// <param name="ttl">Optional per-write TTL (whole seconds, at least one).</param>
    /// <param name="readUncommitted">Optional opt-out of transactional staging.</param>
    /// <returns>A validated definition.</returns>
    public static ValueStateDefinition<T> Value<T>(string name, TimeSpan? ttl = null, bool? readUncommitted = null)
        where T : notnull => new(name, ttl, readUncommitted);

    /// <summary>
    /// Declares a string-keyed ordered-map JSON collection.
    /// </summary>
    /// <typeparam name="TValue">The stored value type. Keys are always <see cref="string"/>.</typeparam>
    /// <param name="name">The collection name.</param>
    /// <param name="ttl">Optional per-write TTL (whole seconds, at least one).</param>
    /// <param name="readUncommitted">Optional opt-out of transactional staging.</param>
    /// <param name="keysetLimit">Optional ordered-scan keyset bound (<c>0..=4096</c>).</param>
    /// <returns>A validated definition.</returns>
    public static MapStateDefinition<TValue> Map<TValue>(
        string name,
        TimeSpan? ttl = null,
        bool? readUncommitted = null,
        int? keysetLimit = null
    )
        where TValue : notnull => new(name, ttl, readUncommitted, keysetLimit);

    /// <summary>
    /// Declares a deque JSON collection.
    /// </summary>
    /// <typeparam name="T">The stored element type.</typeparam>
    /// <param name="name">The collection name.</param>
    /// <param name="ttl">Optional per-write TTL (whole seconds, at least one).</param>
    /// <param name="readUncommitted">Optional opt-out of transactional staging.</param>
    /// <returns>A validated definition.</returns>
    public static DequeStateDefinition<T> Deque<T>(string name, TimeSpan? ttl = null, bool? readUncommitted = null)
        where T : notnull => new(name, ttl, readUncommitted);

    /// <summary>
    /// Declares a single-value message collection storing the full Kafka message.
    /// </summary>
    /// <typeparam name="TPayload">The message payload type.</typeparam>
    /// <param name="name">The collection name.</param>
    /// <param name="ttl">Optional per-write TTL (whole seconds, at least one).</param>
    /// <param name="readUncommitted">Optional opt-out of transactional staging.</param>
    /// <returns>A validated definition.</returns>
    public static MessageValueDefinition<TPayload> MessageValue<TPayload>(
        string name,
        TimeSpan? ttl = null,
        bool? readUncommitted = null
    ) => new(name, ttl, readUncommitted);

    /// <summary>
    /// Declares a string-keyed ordered-map message collection storing the full Kafka message.
    /// </summary>
    /// <typeparam name="TPayload">The message payload type. Keys are always <see cref="string"/>.</typeparam>
    /// <param name="name">The collection name.</param>
    /// <param name="ttl">Optional per-write TTL (whole seconds, at least one).</param>
    /// <param name="readUncommitted">Optional opt-out of transactional staging.</param>
    /// <param name="keysetLimit">Optional ordered-scan keyset bound (<c>0..=4096</c>).</param>
    /// <returns>A validated definition.</returns>
    public static MessageMapDefinition<TPayload> MessageMap<TPayload>(
        string name,
        TimeSpan? ttl = null,
        bool? readUncommitted = null,
        int? keysetLimit = null
    ) => new(name, ttl, readUncommitted, keysetLimit);

    /// <summary>
    /// Declares a deque message collection storing the full Kafka message.
    /// </summary>
    /// <typeparam name="TPayload">The message payload type.</typeparam>
    /// <param name="name">The collection name.</param>
    /// <param name="ttl">Optional per-write TTL (whole seconds, at least one).</param>
    /// <param name="readUncommitted">Optional opt-out of transactional staging.</param>
    /// <returns>A validated definition.</returns>
    public static MessageDequeDefinition<TPayload> MessageDeque<TPayload>(
        string name,
        TimeSpan? ttl = null,
        bool? readUncommitted = null
    ) => new(name, ttl, readUncommitted);

    internal Native.StateCollectionConfig ToNative() =>
        new(Name, Kind, Payload, Ttl, ReadUncommitted, KeysetLimit is { } k ? (uint)k : null);

    private static void ValidateTtl(TimeSpan ttl)
    {
        if (ttl.Ticks % TimeSpan.TicksPerSecond != 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ttl),
                ttl,
                "TTL must be a whole number of seconds (no fractional or sub-second values)."
            );
        }

        var seconds = ttl.Ticks / TimeSpan.TicksPerSecond;
        if (seconds is < 1 or > _maxTtlSeconds)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ttl),
                ttl,
                $"TTL must be between 1 and {_maxTtlSeconds} seconds."
            );
        }
    }
}
