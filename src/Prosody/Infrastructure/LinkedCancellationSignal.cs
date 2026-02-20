namespace Prosody.Infrastructure;

/// <summary>
/// A native cancellation signal linked to a <see cref="CancellationToken"/>.
/// Both the signal and registration must be disposed by the caller.
/// </summary>
internal readonly record struct LinkedCancellationSignal(
    Native.CancellationSignal Signal,
    CancellationTokenRegistration Registration
);
