namespace Prosody.Messaging;

/// <summary>
/// Classifies handler exceptions as permanent or transient without reflection.
/// </summary>
/// <remarks>
/// Pass this classifier to a <c>SubscribeAsync</c> overload for explicit error classification.
/// This path does not inspect <c>PermanentErrorAttribute</c>.
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
