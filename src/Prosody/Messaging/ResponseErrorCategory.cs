namespace Prosody.Messaging;

/// <summary>Classifies a handler response error.</summary>
public enum ResponseErrorCategory
{
    /// <summary>No category was supplied.</summary>
    Unknown = 0,

    /// <summary>Retry can succeed.</summary>
    Transient = 1,

    /// <summary>Retry cannot succeed for this message.</summary>
    Permanent = 2,

    /// <summary>The responding client cannot continue.</summary>
    Terminal = 3,
}
