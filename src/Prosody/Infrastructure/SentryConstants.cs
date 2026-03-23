namespace Prosody.Infrastructure;

/// <summary>
/// Shared constants for Sentry tag keys, context keys, and tag values used by Prosody.
/// </summary>
internal static class SentryConstants
{
    internal static class Tags
    {
        internal const string EventType = "prosody.event_type";
        internal const string ErrorClass = "prosody.error_class";
    }

    internal static class TagValues
    {
        internal const string EventTypeMessage = "message";
        internal const string EventTypeTimer = "timer";
        internal const string ErrorClassPermanent = "permanent";
        internal const string ErrorClassTransient = "transient";
    }

    internal static class ContextKeys
    {
        internal const string Prosody = "prosody";
    }
}
