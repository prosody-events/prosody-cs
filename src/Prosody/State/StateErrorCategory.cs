namespace Prosody.State;

/// <summary>
/// The category of a keyed-state error. State errors are exactly two-way and are never terminal:
/// a shutdown-class failure surfaces as <see cref="Transient"/> and the event redelivers.
/// </summary>
public enum StateErrorCategory
{
    /// <summary>
    /// A permanent failure that can never succeed for this event. Recovered structurally from the
    /// erased seam and reserved for configuration or deployment mistakes (unregistered collection,
    /// identity mismatch, duplicate name, invalid TTL).
    /// </summary>
    Permanent = 0,

    /// <summary>
    /// A retryable failure. Every caller or input mistake at the keyed-state boundary
    /// (null or unrepresentable writes, wrong item shapes, invalid indices, invalid direction
    /// tokens) folds into this category so a data-dependent handler bug retries rather than
    /// committing the offset and silently losing the message.
    /// </summary>
    Transient = 1,
}
