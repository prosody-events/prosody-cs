namespace Prosody.Infrastructure;

/// <summary>
/// Seam over the native cancel watch, so unit tests can drive cancellation
/// without P/Invoke. <see cref="Watch"/> registers the action the native side
/// invokes when cancellation fires; <see cref="Stop"/> retires the watch.
/// </summary>
internal readonly record struct CancelWatch(Action<Action> Watch, Action Stop);
