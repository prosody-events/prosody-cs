using Prosody.Messaging;

namespace Prosody.Tests.TestHelpers;

/// <summary>
/// A typed handler that delegates messages, excise records, and timers to optional lambdas.
/// It has no <see cref="Prosody.Errors.PermanentErrorAttribute"/>.
/// </summary>
internal sealed class LambdaHandler<T>(
    Func<ProsodyContext, Message<T>, CancellationToken, Task>? onMessage = null,
    Func<ProsodyContext, ProsodyTimer, CancellationToken, Task>? onTimer = null,
    Func<ProsodyContext, ExciseMessage, CancellationToken, Task>? onExcise = null
) : IProsodyHandler<T>
{
    public Task OnMessageAsync(
        ProsodyContext prosodyContext,
        Message<T> message,
        CancellationToken cancellationToken
    ) => onMessage?.Invoke(prosodyContext, message, cancellationToken) ?? Task.CompletedTask;

    public Task OnExciseAsync(
        ProsodyContext prosodyContext,
        ExciseMessage message,
        CancellationToken cancellationToken
    ) => onExcise?.Invoke(prosodyContext, message, cancellationToken) ?? Task.CompletedTask;

    public Task OnTimerAsync(ProsodyContext prosodyContext, ProsodyTimer timer, CancellationToken cancellationToken) =>
        onTimer?.Invoke(prosodyContext, timer, cancellationToken) ?? Task.CompletedTask;
}
