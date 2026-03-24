namespace Prosody.Infrastructure;

/// <summary>
/// Classifies a handler error for Sentry event tagging.
/// </summary>
internal enum ErrorClass
{
    Permanent = 0,
    Transient = 1,
}
