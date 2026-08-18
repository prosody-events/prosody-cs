namespace Prosody.Messaging;

/// <summary>
/// Classifies exceptions thrown by an <see cref="IProsodyHandler{TPayload}"/> as permanent or transient
/// using explicit logic — no reflection, no attribute lookup.
/// </summary>
/// <remarks>
/// Implement this interface on your handler (or as a separate class) and pass it to
/// <c>ProsodyClient.SubscribeAsync&lt;TPayload&gt;(handler, classifier)</c>
/// when you want full control over error classification or want to avoid the
/// <c>PermanentErrorAttribute</c> reflection path entirely.
/// </remarks>
public interface IPermanentErrorClassifier
{
    /// <summary>
    /// Determines whether an exception thrown from <see cref="IProsodyHandler{TPayload}.OnMessageAsync"/> is permanent.
    /// </summary>
    /// <param name="exception">The exception to classify.</param>
    /// <returns><see langword="true"/> if the error is permanent (do not retry); otherwise, <see langword="false"/>.</returns>
    bool IsMessageErrorPermanent(Exception exception);

    /// <summary>Determines whether an excise handler exception is permanent.</summary>
    /// <remarks>The default implementation uses the message decision.</remarks>
    /// <param name="exception">The exception to classify.</param>
    /// <returns><see langword="true"/> if Prosody must not retry the excise record.</returns>
    bool IsExciseErrorPermanent(Exception exception) => IsMessageErrorPermanent(exception);

    /// <summary>
    /// Determines whether an exception thrown from <see cref="IProsodyHandler{TPayload}.OnTimerAsync"/> is permanent.
    /// </summary>
    /// <param name="exception">The exception to classify.</param>
    /// <returns><see langword="true"/> if the error is permanent (do not retry); otherwise, <see langword="false"/>.</returns>
    bool IsTimerErrorPermanent(Exception exception);
}
