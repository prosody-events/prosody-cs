using System.Runtime.CompilerServices;
using Prosody.Infrastructure;

namespace Prosody.State;

/// <summary>
/// An immutable declaration of a keyed-state collection.
/// </summary>
/// <remarks>
/// <para>
/// A definition is the single source of typing: it is registered via
/// <see cref="ProsodyClientBuilder.WithStateCollections"/> and passed to a <c>State</c> overload on
/// <c>ProsodyContext</c> to bind a typed handle. Binding uses record equality, so an equal definition
/// or a <c>with { }</c> copy binds the registered collection. Construct definitions through the static factories
/// (<see cref="Value{T}"/>, <see cref="Map{TValue}"/>, <see cref="Deque{T}"/>, <see cref="Set"/>,
/// <see cref="MessageValue{TPayload}"/>, <see cref="MessageMap{TPayload}"/>,
/// <see cref="MessageDeque{TPayload}"/>).
/// </para>
/// <para>
/// Prosody validates collection semantics when the client is built. Capacity is runtime-only. It is
/// enforced lazily on push and may change on a later deploy.
/// </para>
/// </remarks>
public abstract record StateDefinition
{
    private protected StateDefinition(
        string name,
        Native.StateKind kind,
        TimeSpan? ttl,
        bool readUncommitted,
        bool published = false,
        StateReadCache? readCache = null
    )
    {
        ArgumentNullException.ThrowIfNull(name);
        Name = name;
        Kind = kind;
        Ttl = Durations.ToNative(ttl);
        ReadUncommitted = readUncommitted;
        Published = published;
        ReadCache = readCache;
    }

    /// <summary>Gets the collection name.</summary>
    public string Name { get; }

    internal Native.StateKind Kind { get; }

    internal TimeSpan? Ttl { get; }

    internal bool ReadUncommitted { get; }

    internal bool Published { get; }

    internal StateReadCache? ReadCache { get; }

    /// <summary>
    /// Declares a single-value JSON collection.
    /// </summary>
    /// <typeparam name="T">The stored value type.</typeparam>
    /// <param name="name">The collection name.</param>
    /// <param name="ttl">Optional per-write TTL (whole seconds, at least one).</param>
    /// <param name="readUncommitted">Optional opt-out of transactional staging.</param>
    /// <param name="published">Whether owners advertise the collection for cross-group reads.</param>
    /// <param name="readCache">Optional cache policy used by read-only clients.</param>
    /// <returns>A validated definition.</returns>
    public static ValueStateDefinition<T> Value<T>(
        string name,
        TimeSpan? ttl = null,
        bool readUncommitted = false,
        bool published = false,
        StateReadCache? readCache = null
    )
        where T : notnull => new(name, ttl, readUncommitted, published, readCache);

    /// <summary>
    /// Declares a string-keyed ordered-map JSON collection.
    /// </summary>
    /// <typeparam name="TValue">The stored value type. Keys are always <see cref="string"/>.</typeparam>
    /// <param name="name">The collection name.</param>
    /// <param name="ttl">Optional per-write TTL (whole seconds, at least one).</param>
    /// <param name="readUncommitted">Optional opt-out of transactional staging.</param>
    /// <param name="keysetLimit">Optional ordered-scan keyset bound (<c>0..=4096</c>).</param>
    /// <param name="published">Whether owners advertise the collection for cross-group reads.</param>
    /// <param name="readCache">Optional cache policy used by read-only clients.</param>
    /// <returns>A validated definition.</returns>
    public static MapStateDefinition<TValue> Map<TValue>(
        string name,
        TimeSpan? ttl = null,
        bool readUncommitted = false,
        int? keysetLimit = null,
        bool published = false,
        StateReadCache? readCache = null
    )
        where TValue : notnull => new(name, ttl, readUncommitted, keysetLimit, published, readCache);

    /// <summary>
    /// Declares a deque JSON collection.
    /// </summary>
    /// <typeparam name="T">The stored element type.</typeparam>
    /// <param name="name">The collection name.</param>
    /// <param name="ttl">Optional per-write TTL (whole seconds, at least one).</param>
    /// <param name="readUncommitted">Optional opt-out of transactional staging.</param>
    /// <param name="capacity">
    /// Optional maximum window size (positive), enforced lazily on push: each push evicts from the far
    /// end toward the bound. Runtime-only — never persisted, not part of identity, and freely changed
    /// across redeploys, so a shrunk deque reports its old length until the next push trims it.
    /// </param>
    /// <param name="published">Whether owners advertise the collection for cross-group reads.</param>
    /// <param name="readCache">Optional cache policy used by read-only clients.</param>
    /// <returns>A validated definition.</returns>
    public static DequeStateDefinition<T> Deque<T>(
        string name,
        TimeSpan? ttl = null,
        bool readUncommitted = false,
        int? capacity = null,
        bool published = false,
        StateReadCache? readCache = null
    )
        where T : notnull => new(name, ttl, readUncommitted, capacity, published, readCache);

    /// <summary>
    /// Declares an ordered set of <see cref="string"/> members. A set stores presence only.
    /// </summary>
    /// <param name="name">The collection name.</param>
    /// <param name="ttl">Optional per-write TTL (whole seconds, at least one).</param>
    /// <param name="readUncommitted">Optional opt-out of transactional staging.</param>
    /// <param name="keysetLimit">Optional ordered-scan keyset bound (<c>0..=4096</c>).</param>
    /// <param name="published">Whether owners advertise the collection for cross-group reads.</param>
    /// <param name="readCache">Optional cache policy used by read-only clients.</param>
    /// <returns>A validated definition.</returns>
    public static SetStateDefinition Set(
        string name,
        TimeSpan? ttl = null,
        bool readUncommitted = false,
        int? keysetLimit = null,
        bool published = false,
        StateReadCache? readCache = null
    ) => new(name, ttl, readUncommitted, keysetLimit, published, readCache);

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
        bool readUncommitted = false
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
        bool readUncommitted = false,
        int? keysetLimit = null
    ) => new(name, ttl, readUncommitted, keysetLimit);

    /// <summary>
    /// Declares a deque message collection storing the full Kafka message.
    /// </summary>
    /// <typeparam name="TPayload">The message payload type.</typeparam>
    /// <param name="name">The collection name.</param>
    /// <param name="ttl">Optional per-write TTL (whole seconds, at least one).</param>
    /// <param name="readUncommitted">Optional opt-out of transactional staging.</param>
    /// <param name="capacity">
    /// Optional maximum window size (positive), enforced lazily on push. Runtime-only — never
    /// persisted and freely changed across redeploys. See <see cref="Deque{T}"/>.
    /// </param>
    /// <returns>A validated definition.</returns>
    public static MessageDequeDefinition<TPayload> MessageDeque<TPayload>(
        string name,
        TimeSpan? ttl = null,
        bool readUncommitted = false,
        int? capacity = null
    ) => new(name, ttl, readUncommitted, capacity);

    internal Native.StateCollectionConfig ToNative() => new(Name, Kind, Ttl, ReadUncommitted, Published);

    /// <summary>Returns a keyset limit or capacity as the unsigned native bound.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="value"/> is negative.</exception>
    private protected static uint? Bound(int? value, [CallerArgumentExpression(nameof(value))] string name = "") =>
        value is < 0
            ? throw new ArgumentOutOfRangeException(name, value, $"{name} must not be negative.")
            : (uint?)value;
}
