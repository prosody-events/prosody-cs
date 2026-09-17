using System.Globalization;
using System.Text;
using CsCheck;
using Prosody.Configuration;

namespace Prosody.Tests.Unit;

/// <summary>
/// Property tests for <see cref="Duration.Parse"/> against an exact model of <c>humantime</c>.
/// </summary>
/// <remarks>
/// The model is a list of terms. Each term is a count, a unit, and an optional decimal fraction.
/// The model sums the terms in nanoseconds with <see cref="UInt128"/> arithmetic, so it does not
/// repeat the parser's carry logic or its 64-bit overflow checks.
/// </remarks>
public sealed class DurationPropertyTests
{
    private static readonly UInt128 MaxNanos = (UInt128)TimeSpan.MaxValue.Ticks * 100;

    /// <summary>
    /// A unit's names, its length in nanoseconds, and the granularity of its fraction in nanoseconds.
    /// <c>humantime</c> resolves a fraction of a minute or less in nanoseconds, a fraction of an
    /// hour or more in whole seconds, and no fraction of a nanosecond.
    /// </summary>
    private static readonly (string[] Names, ulong Nanos, ulong? Grain)[] Units =
    [
        (["ns", "nsec", "nanos"], 1, null),
        (["us", "µs", "usec"], 1_000, 1),
        (["ms", "msec", "millis"], 1_000_000, 1),
        (["s", "sec", "secs", "second", "seconds"], 1_000_000_000, 1),
        (["m", "min", "mins", "minute", "minutes"], 60_000_000_000, 1),
        (["h", "hr", "hrs", "hour", "hours"], 3_600_000_000_000, 1_000_000_000),
        (["d", "day", "days"], 86_400_000_000_000, 1_000_000_000),
        (["w", "wk", "wks", "week", "weeks"], 604_800_000_000_000, 1_000_000_000),
        (["M", "month", "months"], 2_630_016_000_000_000, 1_000_000_000),
        (["y", "yr", "yrs", "year", "years"], 31_557_600_000_000_000, 1_000_000_000),
    ];

    private static readonly string[] Spaces = ["", "", " ", "  ", "\t"];

    /// <summary>Counts stay below 10^10 so that six terms of any unit fit the 64-bit seconds <c>humantime</c> sums.</summary>
    private static readonly Gen<Term> TermGen = Gen.Int[0, Units.Length - 1]
        .Select(
            Gen.Int[0, 100],
            Gen.ULong[0, 9_999],
            Gen.Int[0, 6],
            Gen.Int[0, 6],
            Gen.ULong[0, 999_999],
            (unit, name, mantissa, exponent, digits, numerator) =>
                new Term(unit, name, mantissa * Pow10(exponent), digits, numerator % Pow10(digits))
        );

    /// <summary>The whitespace choices for one rendering. A rendering reads them in order and wraps around.</summary>
    private static readonly Gen<int[]> SpacesGen = Gen.Int[0, Spaces.Length - 1].Array[128];

    private static readonly Gen<(List<Term>, int[], int[])> ModelGen = TermGen.List[1, 6].Select(SpacesGen, SpacesGen);

    /// <summary>
    /// Invariant: the parser returns the model's exact sum truncated to ticks. It returns
    /// <c>null</c> for an inexact fraction and throws when the sum exceeds <see cref="TimeSpan.MaxValue"/>.
    /// </summary>
    [Fact]
    public void ParseAgreesWithTheExactModel() =>
        ModelGen.Sample(
            (terms, spaces, _) =>
            {
                var text = Render(terms, spaces);
                switch (Model(terms))
                {
                    case null:
                        Assert.Null(Duration.Parse(text));
                        break;
                    case var total when total > MaxNanos:
                        Assert.Throws<OverflowException>(() => Duration.Parse(text));
                        break;
                    case var total:
                        Assert.Equal(TimeSpan.FromTicks((long)(total / 100)), Duration.Parse(text));
                        break;
                }
            },
            iter: 20_000
        );

    /// <summary>Invariant: whitespace between tokens and inside a number does not change the result.</summary>
    [Fact]
    public void ParseIgnoresWhitespacePlacement() =>
        ModelGen.Sample(
            (terms, first, second) => Assert.Equal(Outcome(Render(terms, first)), Outcome(Render(terms, second))),
            iter: 20_000
        );

    private static UInt128? Model(List<Term> terms)
    {
        var total = UInt128.Zero;
        foreach (var term in terms)
        {
            var (_, nanos, grain) = Units[term.Unit];
            total += (UInt128)term.Count * nanos;
            if (term.Digits == 0)
            {
                continue;
            }

            var scaled = (UInt128)term.Numerator * nanos;
            var denominator = Pow10(term.Digits);
            if (grain is not { } g || scaled % ((UInt128)denominator * g) != 0)
            {
                return null;
            }
            total += scaled / denominator;
        }
        return total;
    }

    private static string Render(List<Term> terms, int[] spaces)
    {
        var text = new StringBuilder();
        var slot = 0;
        foreach (var term in terms)
        {
            var (names, _, _) = Units[term.Unit];
            Spaced(term.Count.ToString(CultureInfo.InvariantCulture));
            if (term.Digits > 0)
            {
                Spaced(".");
                Spaced(term.Numerator.ToString(CultureInfo.InvariantCulture).PadLeft(term.Digits, '0'));
            }
            Space();
            text.Append(names[term.Name % names.Length]);
        }
        Space();
        return text.ToString();

        void Space() => text.Append(Spaces[spaces[slot++ % spaces.Length]]);

        void Spaced(string token)
        {
            foreach (var c in token)
            {
                Space();
                text.Append(c);
            }
        }
    }

    private static (bool Overflow, TimeSpan? Value) Outcome(string text)
    {
        try
        {
            return (false, Duration.Parse(text));
        }
        catch (OverflowException)
        {
            return (true, null);
        }
    }

    private static ulong Pow10(int exponent) => (ulong)Math.Pow(10, exponent);

    private sealed record Term(int Unit, int Name, ulong Count, int Digits, ulong Numerator);
}
