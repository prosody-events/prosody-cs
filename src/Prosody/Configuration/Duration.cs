namespace Prosody.Configuration;

/// <summary>
/// Reads a duration environment variable in the format the native client accepts.
/// </summary>
/// <remarks>
/// The native client parses durations with the <c>humantime</c> crate. The format is one or more
/// number-and-unit pairs, such as <c>90s</c>, <c>1.5m</c>, or <c>1m 30s</c>. Units range from
/// <c>ns</c> to <c>y</c>, with their long forms. Invalid syntax returns <c>null</c>.
/// The native build validates the variable and reports syntax errors.
/// </remarks>
internal static class Duration
{
    internal static TimeSpan? FromEnvironment(string variable) =>
        Environment.GetEnvironmentVariable(variable) is { } value ? Parse(value) : null;

    /// <summary>Parses a duration. Returns <c>null</c> when the text is not a valid duration.</summary>
    /// <exception cref="OverflowException">The duration exceeds <see cref="TimeSpan.MaxValue"/>.</exception>
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

        var ticks = 0d;
        while (!rest.IsEmpty)
        {
            if (Number(ref rest) is not { } count)
            {
                return null;
            }

            var name = rest[..Letters(rest)];
            if (TicksPerUnit(name) is not { } unit)
            {
                return null;
            }
            rest = rest[name.Length..].TrimStart();

            ticks += count * unit;
        }
        // Overflow must not become an unset value that bypasses timeout validation.
        return ticks < long.MaxValue
            ? TimeSpan.FromTicks((long)Math.Round(ticks))
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
    private static double? Number(ref ReadOnlySpan<char> rest)
    {
        if (rest.IsEmpty || !char.IsAsciiDigit(rest[0]))
        {
            return null;
        }

        var value = 0d;
        double? weight = null; // the weight of the next fraction digit; null before the decimal point
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
                weight = 0.1;
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
    private static double? TicksPerUnit(ReadOnlySpan<char> name) =>
        name switch
        {
            "ns" or "nsec" or "nanos" => TimeSpan.TicksPerMicrosecond / 1000d,
            "us" or "µs" or "usec" => TimeSpan.TicksPerMicrosecond,
            "ms" or "msec" or "millis" => TimeSpan.TicksPerMillisecond,
            "s" or "sec" or "secs" or "second" or "seconds" => TimeSpan.TicksPerSecond,
            "m" or "min" or "mins" or "minute" or "minutes" => TimeSpan.TicksPerMinute,
            "h" or "hr" or "hrs" or "hour" or "hours" => TimeSpan.TicksPerHour,
            "d" or "day" or "days" => TimeSpan.TicksPerDay,
            "w" or "wk" or "wks" or "week" or "weeks" => 7 * TimeSpan.TicksPerDay,
            "M" or "month" or "months" => 2_630_016 * TimeSpan.TicksPerSecond,
            "y" or "yr" or "yrs" or "year" or "years" => 31_557_600 * TimeSpan.TicksPerSecond,
            _ => null,
        };
}
