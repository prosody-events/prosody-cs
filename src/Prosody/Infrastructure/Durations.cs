using System.Runtime.CompilerServices;

namespace Prosody.Infrastructure;

/// <summary>
/// Converts a <see cref="TimeSpan"/> to the native duration. The native duration is unsigned, so a
/// negative value fails here, before it crosses the boundary. Prosody checks every other bound.
/// </summary>
internal static class Durations
{
    /// <summary>Returns <paramref name="value"/> when the native layer can represent it.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="value"/> is negative.</exception>
    internal static TimeSpan ToNative(TimeSpan value, [CallerArgumentExpression(nameof(value))] string name = "") =>
        value < TimeSpan.Zero
            ? throw new ArgumentOutOfRangeException(name, value, $"{name} must not be negative.")
            : value;

    /// <summary>Returns <paramref name="value"/> when the native layer can represent it.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="value"/> is negative.</exception>
    internal static TimeSpan? ToNative(TimeSpan? value, [CallerArgumentExpression(nameof(value))] string name = "") =>
        value is { } duration ? ToNative(duration, name) : null;
}
