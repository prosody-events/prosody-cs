using Prosody.Configuration;
using Prosody.State;

namespace Prosody.Tests.TestHelpers;

/// <summary>
/// Shared keyed-state definitions and payload records for the integration suite. Mirrors the JS
/// reference <c>STATE_DEFS</c> — one of every kind × payload — plus the scalar/array/source-gen and
/// missing-vs-default pins the C# suite adds.
/// </summary>
/// <remarks>
/// Fixed names are safe because <see cref="IntegrationTestContext"/> mints a unique group id per
/// context and identity is keyed by <c>(group_id, state_type, name)</c>, so every context is fully
/// isolated. Definitions carry no TTL, which avoids the set-level <c>Ttl &gt; StateRecoveryDelay</c>
/// cross-rule.
/// </remarks>
internal static class StateTestSupport
{
    /// <summary>A JSON single-value collection of an object payload.</summary>
    public static readonly ValueStateDefinition<CartState> Cart = StateDefinition.Value<CartState>("cart");

    /// <summary>A JSON single-value collection of a rich object payload (unicode/number/bool/array/nested null).</summary>
    public static readonly ValueStateDefinition<RichState> Rich = StateDefinition.Value<RichState>("rich");

    /// <summary>
    /// A deque collection of a bare scalar (scalar/array round-trip pin). Read back by a fresh client
    /// after a consumer restart so the item travels the full serialize/durable/recover/deserialize
    /// path rather than being served from an in-session materialized cell.
    /// </summary>
    public static readonly DequeStateDefinition<int> ScalarDeque = StateDefinition.Deque<int>("scalarDeque");

    /// <summary>
    /// A deque collection of a bare array (scalar/array round-trip pin). Exercised the same way as
    /// <see cref="ScalarDeque"/>.
    /// </summary>
    public static readonly DequeStateDefinition<int[]> ArrayDeque = StateDefinition.Deque<int[]>("arrayDeque");

    /// <summary>A string-keyed ordered-map collection of an integer value.</summary>
    public static readonly MapStateDefinition<int> Totals = StateDefinition.Map<int>("totals", keysetLimit: 256);

    /// <summary>A string-keyed ordered-map collection of a decimal value (missing-vs-default pin).</summary>
    public static readonly MapStateDefinition<decimal> DecMap = StateDefinition.Map<decimal>("decMap");

    /// <summary>A deque collection of a string element.</summary>
    public static readonly DequeStateDefinition<string> Backlog = StateDefinition.Deque<string>("backlog");

    /// <summary>A deque collection of a bool element (missing-vs-default pin).</summary>
    public static readonly DequeStateDefinition<bool> BoolDeque = StateDefinition.Deque<bool>("boolDeque");

    /// <summary>A capacity-3 deque of an integer element: the lazy push-only eviction pin.</summary>
    public static readonly DequeStateDefinition<int> BoundedDeque = StateDefinition.Deque<int>(
        "boundedDeque",
        capacity: 3
    );

    /// <summary>A single-value message collection.</summary>
    public static readonly MessageValueDefinition<StateMessagePayload> LastMsg =
        StateDefinition.MessageValue<StateMessagePayload>("lastMsg");

    /// <summary>A string-keyed ordered-map message collection.</summary>
    public static readonly MessageMapDefinition<StateMessagePayload> MsgIndex =
        StateDefinition.MessageMap<StateMessagePayload>("msgIndex");

    /// <summary>A deque message collection.</summary>
    public static readonly MessageDequeDefinition<StateMessagePayload> MsgLog =
        StateDefinition.MessageDeque<StateMessagePayload>("msgLog");

    /// <summary>The full canonical set: one of every kind × payload plus the C# pins.</summary>
    public static readonly StateDefinition[] All =
    [
        Cart,
        Rich,
        ScalarDeque,
        ArrayDeque,
        Totals,
        DecMap,
        Backlog,
        BoolDeque,
        BoundedDeque,
        LastMsg,
        MsgIndex,
        MsgLog,
    ];

    /// <summary>
    /// Builds a client-options configure callback that registers <see cref="All"/>, optionally
    /// chaining a further customization.
    /// </summary>
    /// <param name="extra">An optional additional configuration step applied after the collections.</param>
    /// <returns>A configure callback for <see cref="IntegrationTestContext.CreateAsync"/>.</returns>
    public static Action<ClientOptions> WithAllCollections(Action<ClientOptions>? extra = null) =>
        options =>
        {
            options.StateCollections = All;
            extra?.Invoke(options);
        };
}

/// <summary>The message-topic payload used by message-collection tests.</summary>
internal sealed record StateMessagePayload
{
    /// <summary>The message content.</summary>
    public string Content { get; init; } = "";

    /// <summary>A step/attempt discriminator.</summary>
    public int Sequence { get; init; }
}

/// <summary>A simple object payload for value-collection round-trip tests.</summary>
internal sealed record CartState
{
    /// <summary>An opaque nonce distinguishing a written value from pre-existing store contents.</summary>
    public string V { get; init; } = "";
}

/// <summary>
/// A payload exercising the JSON document surface — unicode, fractional number, boolean, array, and a
/// nested null — so a lossy or envelope-coupled codec fails a field-by-field round-trip comparison.
/// </summary>
internal sealed record RichState
{
    /// <summary>A unicode string.</summary>
    public string Text { get; init; } = "";

    /// <summary>A fractional number.</summary>
    public double Number { get; init; }

    /// <summary>A boolean.</summary>
    public bool Flag { get; init; }

    /// <summary>A JSON array.</summary>
    public int[] Items { get; init; } = [];

    /// <summary>A nested nullable field left null.</summary>
    public string? Nested { get; init; }
}
