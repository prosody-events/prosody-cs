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

    protected static void AssertTimerApproximatelyEqual(
        DateTimeOffset actual,
        DateTimeOffset expected
    )
    {
        // Round both times to seconds for comparison (timer precision)
        var actualSeconds = (long)Math.Floor(actual.ToUnixTimeMilliseconds() / 1000.0);
        var expectedSeconds = (long)Math.Floor(expected.ToUnixTimeMilliseconds() / 1000.0);
        var diff = Math.Abs(actualSeconds - expectedSeconds);

        Assert.True(
            diff <= 1,
            $"Timer times differ by {diff} seconds. Expected ~{expected:O}, got {actual:O}"
        );
    }

    /// <summary>
    /// Test payload for messages.
    /// </summary>
    protected sealed record TestPayload
    {
        public string Content { get; init; } = "";
        public int Sequence { get; init; }
    }

    /// <summary>
    /// Configurable test handler using the exception-based pattern.
    /// </summary>
    protected sealed class TestProsodyHandler : IProsodyHandler
    {
        private readonly Func<Context, Message, CancellationToken, Task>? _onMessage;
        private readonly Func<Context, Timer, CancellationToken, Task>? _onTimer;

        public TestProsodyHandler(
            Func<Context, Message, CancellationToken, Task>? onMessage = null,
            Func<Context, Timer, CancellationToken, Task>? onTimer = null
        )
        {
            _onMessage = onMessage;
            _onTimer = onTimer;
        }

        public Task OnMessageAsync(
            Context context,
            Message message,
            CancellationToken cancellationToken
        )
        {
            return _onMessage?.Invoke(context, message, cancellationToken) ?? Task.CompletedTask;
        }

        public Task OnTimerAsync(Context context, Timer timer, CancellationToken cancellationToken)
        {
            return _onTimer?.Invoke(context, timer, cancellationToken) ?? Task.CompletedTask;
        }
    }
}
