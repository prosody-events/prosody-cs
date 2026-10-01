namespace Prosody.State;

/// <summary>
/// Selects the keys that a map or set enumeration returns. A query is a plain value: build it once
/// and reuse it.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="From"/>, <see cref="After"/>, <see cref="To"/>, and <see cref="Before"/> are in
/// iteration order. A <see cref="ScanDirection.Backward"/> query starts at the high end.
/// </para>
/// <para>
/// <see cref="Range"/> is an ascending key range and applies in either direction. Every setting
/// narrows the selection and never widens it. Combined settings intersect.
/// </para>
/// <para>
/// For keyset paging, set <see cref="After"/> to the last key of the previous page and
/// <see cref="Limit"/> to the page size. This works in either direction.
/// </para>
/// </remarks>
public sealed record KeyQuery
{
    /// <summary>Gets the iteration order. The default is <see cref="ScanDirection.Forward"/>.</summary>
    public ScanDirection Direction { get; init; } = ScanDirection.Forward;

    /// <summary>Gets the prefix that every returned key starts with.</summary>
    public string? Prefix { get; init; }

    /// <summary>Gets the first key in iteration order, inclusive. Do not set it with <see cref="After"/>.</summary>
    public string? From { get; init; }

    /// <summary>Gets the key before the first key in iteration order. Do not set it with <see cref="From"/>.</summary>
    public string? After { get; init; }

    /// <summary>Gets the last key in iteration order, inclusive. Do not set it with <see cref="Before"/>.</summary>
    public string? To { get; init; }

    /// <summary>Gets the key after the last key in iteration order. Do not set it with <see cref="To"/>.</summary>
    public string? Before { get; init; }

    /// <summary>
    /// Gets the ascending range of keys to keep, such as <c>new KeyRange("a", "m")</c>. The end is
    /// exclusive. A <see langword="null"/> start or end leaves that side open. A descending range
    /// selects no keys.
    /// </summary>
    public KeyRange? Range { get; init; }

    /// <summary>Gets the maximum number of returned items.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is zero or negative.</exception>
    public int? Limit
    {
        get;
        init => field = StateInterop.PositiveLimit(value, nameof(Limit));
    }

    /// <summary>Converts <paramref name="query"/> for the native layer.</summary>
    /// <exception cref="ArgumentException">The query sets both edges of an inclusive and exclusive pair.</exception>
    internal static Native.KeyQuery ToNative(KeyQuery query)
    {
        if ((query.From is not null && query.After is not null) || (query.To is not null && query.Before is not null))
        {
            throw new ArgumentException(
                "A KeyQuery sets From or After, not both, and To or Before, not both.",
                nameof(query)
            );
        }

        return new(
            StateInterop.ToNative(query.Direction),
            query.Prefix,
            Edge(query.From, query.After),
            Edge(query.To, query.Before),
            query.Range is { } range ? new Native.KeyRange(range.Start, range.End) : null,
            (uint?)query.Limit
        );
    }

    private static Native.KeyEdge? Edge(string? inclusive, string? exclusive) =>
        (inclusive, exclusive) switch
        {
            (not null, _) => new Native.KeyEdge.Inclusive(inclusive),
            (null, not null) => new Native.KeyEdge.Exclusive(exclusive),
            (null, null) => null,
        };
}
