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
    /// A linked signal and registration, or null if the token is default/none.
    /// Both the signal and registration must be disposed by the caller.
    /// </returns>
    internal static (Native.CancellationSignal Signal, CancellationTokenRegistration Registration)? CreateSignal(
        CancellationToken cancellationToken
    )
    {
        if (cancellationToken == CancellationToken.None)
            return null;

        var signal = new Native.CancellationSignal();
        CancellationTokenRegistration registration = cancellationToken.Register(
            static state => ((Native.CancellationSignal)state!).Cancel(),
            signal
        );

        return (signal, registration);
    }
}
