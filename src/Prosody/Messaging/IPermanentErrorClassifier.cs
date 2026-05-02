namespace Prosody.Messaging;

/// <summary>
/// Classifies exceptions thrown by an <see cref="IProsodyHandler{TPayload}"/> as permanent or transient
/// without relying on reflection-based <see cref="Errors.PermanentErrorAttribute"/> discovery.
/// </summary>
/// <remarks>
/// Implement this interface on your handler (or as a separate class) and pass it to
/// <c>ProsodyClient.SubscribeAsync&lt;TPayload&gt;(handler, classifier)</c>
/// for a fully trim-safe and NativeAOT-compatible subscribe path.
/// </remarks>
public interface IPermanentErrorClassifier
{
    /// <summary>
    /// Determines whether an exception thrown from <see cref="IProsodyHandler{TPayload}.OnMessageAsync"/> is permanent.
    /// </summary>
    /// <param name="exception">The exception to classify.</param>
    /// <returns><see langword="true"/> if the error is permanent (do not retry); otherwise, <see langword="false"/>.</returns>
    bool IsMessageErrorPermanent(Exception exception);

    /// <summary>
    /// Determines whether an exception thrown from <see cref="IProsodyHandler{TPayload}.OnTimerAsync"/> is permanent.
    /// </summary>
    /// <param name="exception">The exception to classify.</param>
    /// <returns><see langword="true"/> if the error is permanent (do not retry); otherwise, <see langword="false"/>.</returns>
    bool IsTimerErrorPermanent(Exception exception);
}
