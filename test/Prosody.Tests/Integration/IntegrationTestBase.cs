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
    }

    /// <summary>
    /// Configurable handler for tests. Pass lambdas for OnMessage/OnTimer callbacks.
    /// </summary>
    protected sealed class TestProsodyHandler : IProsodyHandler
    {
        private readonly Func<ProsodyContext, Message, CancellationToken, Task>? _onMessage;
        private readonly Func<ProsodyContext, Timer, CancellationToken, Task>? _onTimer;

        public TestProsodyHandler(
            Func<ProsodyContext, Message, CancellationToken, Task>? onMessage = null,
            Func<ProsodyContext, Timer, CancellationToken, Task>? onTimer = null
        )
        {
            _onMessage = onMessage;
            _onTimer = onTimer;
        }

        public Task OnMessageAsync(ProsodyContext prosodyContext, Message message, CancellationToken cancellationToken)
        {
            return _onMessage?.Invoke(prosodyContext, message, cancellationToken) ?? Task.CompletedTask;
        }

        public Task OnTimerAsync(ProsodyContext prosodyContext, Timer timer, CancellationToken cancellationToken)
        {
            return _onTimer?.Invoke(prosodyContext, timer, cancellationToken) ?? Task.CompletedTask;
        }
    }
}
