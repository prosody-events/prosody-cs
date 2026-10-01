using Prosody.Messaging;

namespace Prosody.Tests.TestHelpers;

/// <summary>
/// A typed handler that delegates messages and timers to optional lambdas. It has no
/// <see cref="Prosody.Errors.PermanentErrorAttribute"/>, and its excise handler does nothing.
/// </summary>
internal sealed class LambdaHandler<T>(
    Func<ProsodyContext, Message<T>, CancellationToken, Task>? onMessage = null,
    Func<ProsodyContext, ProsodyTimer, CancellationToken, Task>? onTimer = null
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
    ) => Task.CompletedTask;

    public Task OnTimerAsync(ProsodyContext prosodyContext, ProsodyTimer timer, CancellationToken cancellationToken) =>
        onTimer?.Invoke(prosodyContext, timer, cancellationToken) ?? Task.CompletedTask;
}
