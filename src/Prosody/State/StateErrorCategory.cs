namespace Prosody.State;

/// <summary>
/// The category of a keyed-state error. State errors are exactly two-way and are never terminal:
/// a shutdown-class failure surfaces as <see cref="Transient"/> and the event redelivers.
/// </summary>
public enum StateErrorCategory
{
    /// <summary>
    /// A permanent failure that can never succeed for this event. Recovered structurally from the
    /// erased seam: configuration or deployment mistakes (unregistered collection, identity mismatch,
    /// duplicate name, invalid TTL) and a JSON <see langword="null"/> write.
    /// </summary>
    Permanent = 0,

    /// <summary>
    /// A retryable failure. A caller mistake that the client detects, such as a value with no JSON
    /// form or an invalid scan direction, folds into this category, so the event retries.
    /// </summary>
    Transient = 1,
}
