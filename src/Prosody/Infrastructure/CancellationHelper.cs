namespace Prosody.Infrastructure;

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
#pragma warning disable CA2000 // Ownership of signal is transferred to the caller via the returned struct
    internal static LinkedCancellationSignal? CreateSignal(CancellationToken cancellationToken)
    {
        if (!cancellationToken.CanBeCanceled)
            return null;

        var signal = new Native.CancellationSignal();
        CancellationTokenRegistration registration = cancellationToken.Register(
            static state => ((Native.CancellationSignal)state!).Cancel(),
            signal
        );

        return new LinkedCancellationSignal(signal, registration);
    }
#pragma warning restore CA2000
}
