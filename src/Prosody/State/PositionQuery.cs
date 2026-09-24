namespace Prosody.State;

/// <summary>
/// Selects the elements that a deque enumeration returns. Positions are zero-based and count from
/// the front. A query is a plain value: build it once and reuse it.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="From"/>, <see cref="After"/>, <see cref="To"/>, and <see cref="Before"/> are in
/// iteration order. A <see cref="ScanDirection.Backward"/> query starts at the back.
/// </para>
/// <para>
/// <see cref="Range"/> is an ascending interval and applies in either direction. Every setting
/// narrows the selection and never widens it. Combined settings intersect.
/// </para>
/// <para>
/// For keyset paging, set <see cref="After"/> to the last position of the previous page and
/// <see cref="Limit"/> to the page size.
/// </para>
/// </remarks>
public sealed record PositionQuery
{
    /// <summary>Gets the iteration order. The default is <see cref="ScanDirection.Forward"/>.</summary>
    public ScanDirection Direction { get; init; } = ScanDirection.Forward;

    /// <summary>Gets the first position in iteration order, inclusive. Do not set it with <see cref="After"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
    public int? From
    {
        get;
        init => field = Position(value, nameof(From));
    }

    /// <summary>Gets the position before the first position in iteration order. Do not set it with <see cref="From"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
    public int? After
    {
        get;
        init => field = Position(value, nameof(After));
    }

    /// <summary>Gets the last position in iteration order, inclusive. Do not set it with <see cref="Before"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
    public int? To
    {
        get;
        init => field = Position(value, nameof(To));
    }

    /// <summary>Gets the position after the last position in iteration order. Do not set it with <see cref="To"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
    public int? Before
    {
        get;
        init => field = Position(value, nameof(Before));
    }

    /// <summary>
    /// Gets the ascending range of positions to keep, such as <c>2..5</c> or <c>3..</c>. The end is
    /// exclusive.
    /// </summary>
    /// <remarks>
    /// A position must count from the front. The only from-end index allowed is <c>^0</c> as the
    /// end, which means no upper bound. Other from-end indexes need the deque length, so they are
    /// rejected.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The range uses a from-end index other than an end of <c>^0</c>, or its start is after its end.
    /// </exception>
    public Range? Range
    {
        get;
        init => field = Ascending(value);
    }

    /// <summary>Gets the maximum number of returned items.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is zero or negative.</exception>
    public int? Limit
    {
        get;
        init =>
            field = value is <= 0
                ? throw new ArgumentOutOfRangeException(nameof(value), value, "Limit must be positive.")
                : value;
    }

    /// <summary>Converts <paramref name="query"/> for the native layer.</summary>
    /// <exception cref="ArgumentException">The query sets both edges of an inclusive and exclusive pair.</exception>
    internal static Native.PositionQuery ToNative(PositionQuery query)
    {
        if ((query.From is not null && query.After is not null) || (query.To is not null && query.Before is not null))
        {
            throw new ArgumentException(
                "A PositionQuery sets From or After, not both, and To or Before, not both.",
                nameof(query)
            );
        }

        return new(
            StateInterop.ToNative(query.Direction),
            Edge(query.From, query.After),
            Edge(query.To, query.Before),
            query.Range is { } range
                ? new Native.PositionRange(
                    (ulong)range.Start.Value,
                    range.End.IsFromEnd ? null : (ulong)range.End.Value
                )
                : null,
            (uint?)query.Limit
        );
    }

    private static int? Position(int? value, string property) =>
        value is < 0
            ? throw new ArgumentOutOfRangeException(nameof(value), value, $"{property} must not be negative.")
            : value;

    private static Range? Ascending(Range? value)
    {
        if (value is not { } range)
        {
            return null;
        }

        if (range.Start.IsFromEnd || (range.End.IsFromEnd && range.End.Value != 0))
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                range,
                "Range must count from the front. Only ^0 is allowed, as the end."
            );
        }

        if (!range.End.IsFromEnd && range.Start.Value > range.End.Value)
        {
            throw new ArgumentOutOfRangeException(nameof(value), range, "Range must be ascending.");
        }

        return range;
    }

    private static Native.PositionEdge? Edge(int? inclusive, int? exclusive) =>
        (inclusive, exclusive) switch
        {
            ({ } included, _) => new Native.PositionEdge.Inclusive((ulong)included),
            (null, { } excluded) => new Native.PositionEdge.Exclusive((ulong)excluded),
            (null, null) => null,
        };
}
