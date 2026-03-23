using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Prosody.Errors;
using Prosody.Infrastructure;
using Prosody.Logging;
using Prosody.Tests.TestHelpers;
using static Prosody.Tests.TestHelpers.TestDefaults;
using NativeResultCode = Prosody.Native.HandlerResultCode;

namespace Prosody.Tests.Unit;

/// <summary>
/// Tests for Sentry capture behavior in <see cref="EventHandlerBridge"/>.
/// </summary>
/// <remarks>
/// These tests call <see cref="EventHandlerBridge.InvokeHandlerAsync"/> directly,
/// injecting a <c>buildSentryContext</c> delegate without requiring Sentry to be
/// initialized. The call-site <c>SentryIntegration.IsEnabled</c> check in
/// <c>HandleMessageAsync</c> / <c>HandleTimerAsync</c> is bypassed; the delegate is
/// invoked unconditionally inside <c>TryCaptureToSentry</c> regardless of whether
/// Sentry is enabled. Prosody never calls <c>SentrySdk.Init</c> — it only enriches
/// an already-initialized instance.
/// </remarks>
public sealed class SentryCaptureBehaviorTests : IDisposable
{
    public SentryCaptureBehaviorTests()
    {
        ProsodyLogging.Clear();
    }

    public void Dispose() => ProsodyLogging.Clear();

    [Fact]
    public async Task TransientErrorResult_IsUnchanged_WhenBuildSentryContextThrows()
    {
        var result = await EventHandlerBridge.InvokeHandlerAsync(
            _ => throw new InvalidOperationException("transient"),
            permanentErrorAttribute: null,
            NeverCancel,
            EmptyCarrier,
            buildSentryContext: () => throw new InvalidOperationException("sentry context failed")
        );

        Assert.Equal(NativeResultCode.TransientError, result.Code);
    }

    [Fact]
    public async Task PermanentErrorResult_IsUnchanged_WhenBuildSentryContextThrows()
    {
        var result = await EventHandlerBridge.InvokeHandlerAsync(
            _ => throw new PermanentException("permanent"),
            permanentErrorAttribute: null,
            NeverCancel,
            EmptyCarrier,
            buildSentryContext: () => throw new InvalidOperationException("sentry context failed")
        );

        Assert.Equal(NativeResultCode.PermanentError, result.Code);
    }

    [Fact]
    public async Task BuildSentryContext_IsInvoked_OnErrorPath()
    {
        var invoked = false;

        await EventHandlerBridge.InvokeHandlerAsync(
            _ => throw new InvalidOperationException("error"),
            permanentErrorAttribute: null,
            NeverCancel,
            EmptyCarrier,
            buildSentryContext: () =>
            {
                invoked = true;
                return null;
            }
        );

        Assert.True(invoked);
    }

    [Fact]
    public async Task BuildSentryContext_NotInvoked_OnSuccessPath()
    {
        var invoked = false;

        await EventHandlerBridge.InvokeHandlerAsync(
            _ => Task.CompletedTask,
            permanentErrorAttribute: null,
            NeverCancel,
            EmptyCarrier,
            buildSentryContext: () =>
            {
                invoked = true;
                return null;
            }
        );

        Assert.False(invoked);
    }

    [Fact]
    public async Task BuildSentryContext_NotInvoked_OnCancellationPath()
    {
        var invoked = false;

        var result = await EventHandlerBridge.InvokeHandlerAsync(
            _ => throw new OperationCanceledException("shutdown"),
            permanentErrorAttribute: null,
            NeverCancel,
            EmptyCarrier,
            buildSentryContext: () =>
            {
                invoked = true;
                return null;
            }
        );

        Assert.Equal(NativeResultCode.TransientError, result.Code);
        Assert.False(invoked);
    }

    [Fact]
    public async Task LogSentryCaptureFailed_IsEmitted_WhenBuildSentryContextThrows()
    {
        var collector = new FakeLogCollector();
        using var factory = new FakeLoggerFactory(collector);
        ProsodyLogging.Configure(factory);

        await EventHandlerBridge.InvokeHandlerAsync(
            _ => throw new InvalidOperationException("error"),
            permanentErrorAttribute: null,
            NeverCancel,
            EmptyCarrier,
            buildSentryContext: () => throw new InvalidOperationException("sentry context failed")
        );

        var logs = collector.GetSnapshot();
        Assert.Contains(logs, r => r.Id.Id == 4 && r.Level == LogLevel.Error);
    }
}
