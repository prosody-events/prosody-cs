using Sentry;

namespace Prosody.Infrastructure;

internal static class SentryIntegration
{
    private static readonly Lazy<bool> Initialized = new(() =>
    {
        var dsn = Environment.GetEnvironmentVariable("SENTRY_DSN");
        if (string.IsNullOrEmpty(dsn))
            return false;

        SentrySdk.Init(o =>
        {
            o.Dsn = dsn;
            o.IsGlobalModeEnabled = true;
            o.TracesSampleRate = 0; // OTel is already managed by prosody
        });
        return true;
    });

    internal static bool IsEnabled => Initialized.Value;

    internal static void CaptureException(
        Exception exception,
        string eventType,
        Dictionary<string, string>? context = null)
    {
        if (!IsEnabled)
            return;

        SentrySdk.WithScope(scope =>
        {
            scope.SetTag("prosody.event_type", eventType);
            if (context is not null)
                scope.Contexts["prosody"] = context;
            SentrySdk.CaptureException(exception);
        });
    }
}
