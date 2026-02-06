namespace Prosody;

/// <summary>
/// Helper methods for bridging .NET CancellationToken to native CancellationSignal.
/// </summary>
internal static class CancellationHelper
{
    /// <summary>
    /// Creates a native CancellationSignal linked to a CancellationToken.
    /// </summary>
    /// <param name="cancellationToken">The token to monitor for cancellation.</param>
    /// <returns>
    /// A CancellationSignal that will be signaled when the token is cancelled,
    /// or null if the token is default/none.
    /// </returns>
    internal static Native.CancellationSignal? CreateSignal(CancellationToken cancellationToken)
    {
        if (cancellationToken == CancellationToken.None)
            return null;

        var signal = new Native.CancellationSignal();

        // Register to cancel the signal when the token is cancelled
        cancellationToken.Register(() => signal.Cancel());

        return signal;
    }
}
