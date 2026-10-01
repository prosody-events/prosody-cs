namespace Prosody.Messaging;

/// <summary>The reason for one handler call.</summary>
public enum DemandKind
{
    /// <summary>The first attempt at an event.</summary>
    Normal = 0,

    /// <summary>An attempt after one or more failures.</summary>
    Failure = 1,
}
