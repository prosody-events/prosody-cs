using Prosody.Configuration;
using Prosody.Messaging;
using Prosody.Tests.TestHelpers;

[assembly: AssemblyFixture(typeof(IntegrationTestFixture))]

namespace Prosody.Tests.Integration;

/// <summary>
/// Base class for integration tests providing shared utilities.
/// Test classes run in parallel (each class is its own implicit collection).
/// The fixture is shared via assembly fixture injection.
/// </summary>
public abstract class IntegrationTestBase(IntegrationTestFixture fixture)
{
    protected IntegrationTestFixture Fixture { get; } = fixture;

    private protected Task<IntegrationTestContext> CreateTestContextAsync() =>
        IntegrationTestContext.CreateAsync(Fixture.Admin);

    private protected Task<IntegrationTestContext> CreateTestContextAsync(Action<ClientOptions> configure) =>
        IntegrationTestContext.CreateAsync(Fixture.Admin, configure);

    protected static void AssertTimerApproximatelyEqual(DateTimeOffset actual, DateTimeOffset expected)
    {
        // Round both times to seconds for comparison (timer precision)
        var actualSeconds = (long)Math.Floor(actual.ToUnixTimeMilliseconds() / 1000.0);
        var expectedSeconds = (long)Math.Floor(expected.ToUnixTimeMilliseconds() / 1000.0);
        var diff = Math.Abs(actualSeconds - expectedSeconds);

        Assert.True(diff <= 1, $"Timer times differ by {diff} seconds. Expected ~{expected:O}, got {actual:O}");
    }

    protected sealed record TestPayload
    {
        public string Content { get; init; } = "";
        public int Sequence { get; init; }
        public string MessageContent { get; init; } = "";
    }

    /// <summary>
    /// Configurable handler for tests. Pass lambdas for OnMessage/OnTimer callbacks.
    /// </summary>
    protected sealed class TestProsodyHandler<T>(
        Func<ProsodyContext, Message<T>, CancellationToken, Task>? onMessage = null,
        Func<ProsodyContext, ExciseMessage, CancellationToken, Task>? onExcise = null,
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
        ) => onExcise?.Invoke(prosodyContext, message, cancellationToken) ?? Task.CompletedTask;

        public Task OnTimerAsync(
            ProsodyContext prosodyContext,
            ProsodyTimer timer,
            CancellationToken cancellationToken
        ) => onTimer?.Invoke(prosodyContext, timer, cancellationToken) ?? Task.CompletedTask;
    }
}
