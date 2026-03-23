using Sentry;

namespace Prosody.Infrastructure;

/// <summary>
/// Manages Sentry error reporting for handler dispatch failures.
/// </summary>
/// <remarks>
/// <para>
/// Prosody never calls <c>SentrySdk.Init</c>. If the host application has initialized
/// Sentry, Prosody automatically enriches captured events with handler context. If Sentry
/// is not initialized, all capture calls are silent no-ops.
/// </para>
/// <para>
/// Sentry failures never affect handler results — all capture errors are caught and
/// logged, ensuring Sentry issues cannot mask or replace the original exception at
/// the FFI boundary.
/// </para>
/// </remarks>
internal static class SentryIntegration
{
    // Checked live on each call so that Sentry initialized after Prosody starts is picked up.
    internal static bool IsEnabled => SentrySdk.IsEnabled;

    internal static void CaptureException(
        Exception exception,
        string eventType,
        Dictionary<string, string>? context = null,
        ErrorClass? errorClass = null
    )
    {
        if (!IsEnabled)
            return;

        SentrySdk.CaptureException(
            exception,
            scope =>
            {
                scope.SetTag("prosody.event_type", eventType);
                if (errorClass is not null)
                {
                    scope.SetTag(
                        "prosody.error_class",
                        errorClass.Value switch
                        {
                            ErrorClass.Permanent => "permanent",
                            _ => "transient",
                        }
                    );
                }
                if (context is not null)
                    scope.Contexts["prosody"] = context;
            }
        );
    }
}
