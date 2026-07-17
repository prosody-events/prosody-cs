using System.Diagnostics.CodeAnalysis;

namespace Prosody.State;

/// <summary>
/// An optional keyed-state read result that distinguishes an absent value from a
/// stored default.
/// </summary>
/// <typeparam name="T">The stored value type.</typeparam>
/// <remarks>
/// <para>
/// A nullable reference or <see cref="System.Nullable{T}"/> cannot tell "no value stored"
/// apart from a stored CLR default: a map of <see cref="decimal"/> holding <c>0</c> and a
/// deque of <see cref="bool"/> holding <c>false</c> would both collapse to the default.
/// <see cref="StateValue{T}"/> keeps the two apart — <see cref="HasValue"/> is
/// <see langword="false"/> only when the collection has no value at that position.
/// </para>
/// </remarks>
public readonly struct StateValue<T> : IEquatable<StateValue<T>>
    where T : notnull
{
    private readonly T _value;

    internal StateValue(T value)
    {
        _value = value;
        HasValue = true;
    }

    /// <summary>
    /// Gets a value indicating whether a value is present. When <see langword="false"/>,
    /// the collection held no value at the requested position (never written, cleared, or removed).
    /// </summary>
    public bool HasValue { get; }

    /// <summary>
    /// Gets the stored value.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when <see cref="HasValue"/> is <see langword="false"/>.</exception>
    public T Value =>
        HasValue
            ? _value
            : throw new InvalidOperationException(
                "StateValue has no value; check HasValue, TryGetValue, or GetValueOrDefault before reading Value."
            );

    /// <summary>
    /// Returns the stored value when present, otherwise the supplied fallback.
    /// </summary>
    /// <param name="defaultValue">The value to return when no value is present.</param>
    /// <returns>The stored value, or <paramref name="defaultValue"/> when absent.</returns>
    /// <remarks>Mirrors <see cref="System.Nullable{T}.GetValueOrDefault(T)"/>.</remarks>
    public T GetValueOrDefault(T defaultValue) => HasValue ? _value : defaultValue;

    /// <summary>
    /// Gets the stored value when present.
    /// </summary>
    /// <param name="value">When this method returns <see langword="true"/>, the stored value; otherwise the default.</param>
    /// <returns><see langword="true"/> when a value is present; otherwise <see langword="false"/>.</returns>
    public bool TryGetValue([MaybeNullWhen(false)] out T value)
    {
        value = _value;
        return HasValue;
    }

    /// <summary>Represents the absent value.</summary>
    internal static StateValue<T> None => default;

    /// <inheritdoc/>
    public bool Equals(StateValue<T> other)
    {
        if (HasValue != other.HasValue)
        {
            return false;
        }

        return !HasValue || EqualityComparer<T>.Default.Equals(_value, other._value);
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is StateValue<T> other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HasValue ? EqualityComparer<T>.Default.GetHashCode(_value!) : 0;

    /// <summary>Determines whether two <see cref="StateValue{T}"/> instances are equal.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns><see langword="true"/> when the instances are equal.</returns>
    public static bool operator ==(StateValue<T> left, StateValue<T> right) => left.Equals(right);

    /// <summary>Determines whether two <see cref="StateValue{T}"/> instances are unequal.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns><see langword="true"/> when the instances are not equal.</returns>
    public static bool operator !=(StateValue<T> left, StateValue<T> right) => !left.Equals(right);
}
