using Sentry;

namespace Prosody.Infrastructure;

internal static class SentryIntegration
{
    internal static void CaptureException(
        Exception exception,
        string eventType,
        object? context = null,
        string? errorClass = null)
    {
        if (!SentrySdk.IsEnabled)
            return;

        SentrySdk.CaptureException(
            exception,
            scope =>
            {
                scope.SetTag("prosody.event_type", eventType);
                if (errorClass is not null)
                    scope.SetTag("prosody.error_class", errorClass);
                if (context is not null)
                    scope.Contexts["prosody"] = context;
            }
        );
    }
}
