namespace Prosody.Messaging;

/// <summary>Classifies a handler response error.</summary>
public enum ResponseErrorCategory
{
    /// <summary>Retry can succeed.</summary>
    Transient = 0,

    /// <summary>Retry cannot succeed for this message.</summary>
    Permanent = 1,

    /// <summary>The responding client cannot continue.</summary>
    Terminal = 2,
}
