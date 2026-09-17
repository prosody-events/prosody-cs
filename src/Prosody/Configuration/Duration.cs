namespace Prosody.Configuration;

/// <summary>
/// Reads a duration environment variable in the format the native client accepts.
/// </summary>
/// <remarks>
/// The native client parses durations with the <c>humantime</c> crate. The format is one or more
/// number-and-unit pairs, such as <c>90s</c>, <c>1.5m</c>, or <c>1m 30s</c>. Units range from
/// <c>ns</c> to <c>y</c>, with their long forms. This parser uses the same exact integer
/// arithmetic. A fraction must divide its unit exactly, so <c>0.5ns</c> and <c>0.0001h</c> are
/// not durations. The sum is truncated to ticks, so the result equals the native client's
/// duration after conversion. Text that <c>humantime</c> rejects returns <c>null</c>.
/// The native client validates the variable again at connect and reports the precise error.
/// </remarks>
internal static class Duration
{
    private const ulong _nanosPerSecond = 1_000_000_000;
    private const ulong _nanosPerTick = 100;
    private static readonly UInt128 MaxNanos = (UInt128)TimeSpan.MaxValue.Ticks * _nanosPerTick;

    internal static TimeSpan? FromEnvironment(string variable) =>
        Environment.GetEnvironmentVariable(variable) is { } value ? Parse(value) : null;

    /// <summary>Parses a duration. Returns <c>null</c> when the text is not a duration <c>humantime</c> accepts.</summary>
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

        var seconds = 0UL;
        var nanos = 0UL;
        try
        {
            while (!rest.IsEmpty)
            {
                if (!Number(ref rest, out var count, out var fraction))
                {
                    return null;
                }

                var name = rest[..Letters(rest)];
                if (Length(name) is not { } unit)
                {
                    return null;
                }
                rest = rest[name.Length..].TrimStart();

                Add(ref seconds, ref nanos, checked(count * unit.Seconds), checked(count * unit.Nanos));
                if (fraction is { } part)
                {
                    var (fractionSeconds, fractionNanos) = Scale(part, unit);
                    Add(ref seconds, ref nanos, fractionSeconds, fractionNanos);
                }
            }
        }
        catch (OverflowException)
        {
            // humantime reports a number beyond 64 bits, or an inexact fraction, as one error.
            return null;
        }

        // Overflow must not become an unset value that bypasses timeout validation.
        return ((UInt128)seconds * _nanosPerSecond) + nanos <= MaxNanos
            ? TimeSpan.FromTicks(((long)seconds * TimeSpan.TicksPerSecond) + (long)(nanos / _nanosPerTick))
            : throw new OverflowException("The duration exceeds TimeSpan.MaxValue.");
    }

    /// <summary>
    /// Reads one number and the whitespace after it. Returns <c>false</c> when the text does not
    /// start with a digit, or a decimal point has no digit after it. The fraction is the digits
    /// after the decimal point, so its numerator is less than its denominator.
    /// </summary>
    /// <remarks>
    /// <c>humantime</c> ignores whitespace inside a number, so <c>1 .5m</c> and <c>1 5s</c> read
    /// as <c>1.5m</c> and <c>15s</c>. This reader does the same.
    /// </remarks>
    private static bool Number(
        ref ReadOnlySpan<char> rest,
        out ulong count,
        out (ulong Numerator, ulong Denominator)? fraction
    )
    {
        count = 0;
        fraction = null;
        if (rest.IsEmpty || !char.IsAsciiDigit(rest[0]))
        {
            return false;
        }

        var numerator = 0UL;
        var denominator = 1UL;
        var afterPoint = false;
        for (; !rest.IsEmpty; rest = rest[1..])
        {
            var c = rest[0];
            if (char.IsAsciiDigit(c))
            {
                var digit = (ulong)(c - '0');
                if (afterPoint)
                {
                    denominator = checked(denominator * 10);
                    numerator = checked((numerator * 10) + digit);
                }
                else
                {
                    count = checked((count * 10) + digit);
                }
            }
            else if (c == '.' && !afterPoint)
            {
                afterPoint = true;
            }
            else if (!char.IsWhiteSpace(c))
            {
                break;
            }
        }

        if (afterPoint)
        {
            if (denominator == 1)
            {
                return false;
            }
            fraction = (numerator, denominator);
        }
        return true;
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
    private static (ulong Seconds, ulong Nanos)? Length(ReadOnlySpan<char> name) =>
        name switch
        {
            "ns" or "nsec" or "nanos" => (0, 1),
            "us" or "µs" or "usec" => (0, 1_000),
            "ms" or "msec" or "millis" => (0, 1_000_000),
            "s" or "sec" or "secs" or "second" or "seconds" => (1, 0),
            "m" or "min" or "mins" or "minute" or "minutes" => (60, 0),
            "h" or "hr" or "hrs" or "hour" or "hours" => (3_600, 0),
            "d" or "day" or "days" => (86_400, 0),
            "w" or "wk" or "wks" or "week" or "weeks" => (7 * 86_400, 0),
            "M" or "month" or "months" => (2_630_016, 0),
            "y" or "yr" or "yrs" or "year" or "years" => (31_557_600, 0),
            _ => null,
        };

    /// <summary>
    /// Scales a fraction of one unit as <c>humantime</c> does. A fraction of a minute or a shorter
    /// unit resolves in nanoseconds. A fraction of an hour or a longer unit resolves in whole
    /// seconds. A nanosecond has no fraction.
    /// </summary>
    /// <exception cref="OverflowException">The fraction does not divide the unit exactly.</exception>
    private static (ulong Seconds, ulong Nanos) Scale(
        (ulong Numerator, ulong Denominator) fraction,
        (ulong Seconds, ulong Nanos) unit
    ) =>
        unit switch
        {
            (0, 1) => throw new OverflowException("A nanosecond has no fraction."),
            (0, var nanos) => (0, Exact(fraction, nanos)),
            (var seconds, _) when seconds <= 60 => (0, Exact(fraction, seconds * _nanosPerSecond)),
            (var seconds, _) => (Exact(fraction, seconds), 0),
        };

    private static ulong Exact((ulong Numerator, ulong Denominator) fraction, ulong scale)
    {
        var product = checked(fraction.Numerator * scale);
        return product % fraction.Denominator == 0
            ? product / fraction.Denominator
            : throw new OverflowException("The fraction is not exact.");
    }

    /// <summary>Adds one term and carries whole seconds out of the nanoseconds.</summary>
    private static void Add(ref ulong seconds, ref ulong nanos, ulong addSeconds, ulong addNanos)
    {
        nanos = checked(nanos + addNanos);
        seconds = checked(seconds + addSeconds + (nanos / _nanosPerSecond));
        nanos %= _nanosPerSecond;
    }
}
