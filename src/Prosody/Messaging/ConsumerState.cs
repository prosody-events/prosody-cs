namespace Prosody.Messaging;

/// <summary>
/// Consumer state.
/// </summary>
public enum ConsumerState
{
    /// <summary>
    /// Consumer has not been configured.
    /// </summary>
    Unconfigured = 0,

    /// <summary>
    /// Consumer is configured but not running.
    /// </summary>
    Configured = 1,

    /// <summary>
    /// Consumer is actively processing messages.
    /// </summary>
    Running = 2,
}
