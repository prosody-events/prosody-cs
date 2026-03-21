using Sentry;

namespace Prosody.Infrastructure;

internal static class SentryIntegration
{
    internal static void CaptureException(Exception exception, string eventType, object? context = null)
    {
        if (!SentrySdk.IsEnabled)
            return;

        SentrySdk.CaptureException(
            exception,
            scope =>
            {
                scope.SetTag("prosody.event_type", eventType);
                if (context is not null)
                    scope.Contexts["prosody"] = context;
            }
        );
    }
}
