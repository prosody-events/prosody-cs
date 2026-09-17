namespace Prosody.Configuration;

/// <summary>
/// Reads a duration environment variable in the format the native client accepts.
/// </summary>
/// <remarks>
/// The native client parses durations with the <c>humantime</c> crate. The format is one or more
/// number-and-unit pairs, such as <c>90s</c>, <c>1.5m</c>, or <c>1m 30s</c>. Units range from
/// <c>ns</c> to <c>y</c>, with their long forms. This parser sums the pairs in nanoseconds with
/// exact <see cref="decimal"/> arithmetic and truncates the sum to ticks. The result equals the
/// native client's duration for every text the native client accepts. Invalid syntax returns
/// <c>null</c>. This parser accepts a fraction that <c>humantime</c> rejects as inexact, such as
/// <c>0.0001h</c>. The native client validates the variable again at connect and reports the
/// precise error.
/// </remarks>
internal static class Duration
{
    private const decimal _nanosPerSecond = 1_000_000_000;
    private const decimal _nanosPerTick = 100;
    private static readonly decimal MaxNanos = TimeSpan.MaxValue.Ticks * _nanosPerTick;

    internal static TimeSpan? FromEnvironment(string variable) =>
        Environment.GetEnvironmentVariable(variable) is { } value ? Parse(value) : null;

    /// <summary>Parses a duration. Returns <c>null</c> when the text is not a valid duration.</summary>
    /// <exception cref="OverflowException">
    /// The duration exceeds <see cref="TimeSpan.MaxValue"/>, or a number exceeds <see cref="decimal.MaxValue"/>.
    /// </exception>
    internal static TimeSpan? Parse(string text)
    {
        if (string.Equals(text, "0", StringComparison.Ordinal))
        {
            return TimeSpan.Zero;
        }

        var rest = text.AsSpan().Trim();
        if (rest.IsEmpty)
        {
            return null;
        }

        var nanos = 0m;
        while (!rest.IsEmpty)
        {
            if (Number(ref rest) is not { } count)
            {
                return null;
            }

            var name = rest[..Letters(rest)];
            if (NanosPerUnit(name) is not { } unit)
            {
                return null;
            }
            rest = rest[name.Length..].TrimStart();

            nanos += count * unit;
        }

        // Overflow must not become an unset value that bypasses timeout validation.
        return nanos <= MaxNanos
            ? TimeSpan.FromTicks((long)(nanos / _nanosPerTick))
            : throw new OverflowException("The duration exceeds TimeSpan.MaxValue.");
    }

    /// <summary>
    /// Reads one number and the whitespace after it. Returns <c>null</c> when the text does not
    /// start with a digit, or a decimal point has no digit after it.
    /// </summary>
    /// <remarks>
    /// <c>humantime</c> ignores whitespace inside a number, so <c>1 .5m</c> and <c>1 5s</c> read
    /// as <c>1.5m</c> and <c>15s</c>. This reader does the same.
    /// </remarks>
    private static decimal? Number(ref ReadOnlySpan<char> rest)
    {
        if (rest.IsEmpty || !char.IsAsciiDigit(rest[0]))
        {
            return null;
        }

        var value = 0m;
        decimal? weight = null; // the weight of the next fraction digit; null before the decimal point
        var pointNeedsDigit = false;
        for (; !rest.IsEmpty; rest = rest[1..])
        {
            var c = rest[0];
            if (char.IsAsciiDigit(c))
            {
                pointNeedsDigit = false;
                if (weight is { } fraction)
                {
                    value += (c - '0') * fraction;
                    weight = fraction / 10;
                }
                else
                {
                    value = (value * 10) + (c - '0');
                }
            }
            else if (c == '.' && weight is null)
            {
                pointNeedsDigit = true;
                weight = 0.1m;
            }
            else if (!char.IsWhiteSpace(c))
            {
                break;
            }
        }
        return pointNeedsDigit ? null : value;
    }

    private static int Letters(ReadOnlySpan<char> text)
    {
        var length = 0;
        while (length < text.Length && char.IsLetter(text[length]))
        {
            length++;
        }
        return length;
    }

    /// <summary>The unit names and lengths <c>humantime</c> accepts. A month is 30.44 days; a year is 365.25 days.</summary>
    private static decimal? NanosPerUnit(ReadOnlySpan<char> name) =>
        name switch
        {
            "ns" or "nsec" or "nanos" => 1,
            "us" or "µs" or "usec" => 1_000,
            "ms" or "msec" or "millis" => 1_000_000,
            "s" or "sec" or "secs" or "second" or "seconds" => _nanosPerSecond,
            "m" or "min" or "mins" or "minute" or "minutes" => 60 * _nanosPerSecond,
            "h" or "hr" or "hrs" or "hour" or "hours" => 3_600 * _nanosPerSecond,
            "d" or "day" or "days" => 86_400 * _nanosPerSecond,
            "w" or "wk" or "wks" or "week" or "weeks" => 604_800 * _nanosPerSecond,
            "M" or "month" or "months" => 2_630_016 * _nanosPerSecond,
            "y" or "yr" or "yrs" or "year" or "years" => 31_557_600 * _nanosPerSecond,
            _ => null,
        };
}
