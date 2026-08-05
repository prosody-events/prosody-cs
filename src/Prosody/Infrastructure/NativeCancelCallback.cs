namespace Prosody.Infrastructure;

/// <summary>
/// Forwards the native cancellation push to the bridge's registered action.
/// The one per-message FFI allocation the push design keeps.
/// </summary>
internal sealed class NativeCancelCallback(Action onCancelled) : Native.CancelCallback
{
    public void Cancel() => onCancelled();
}
