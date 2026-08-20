namespace Prosody.Tests.TestHelpers;

internal static class TestDefaults
{
    internal const string BootstrapServers = "localhost:9092";

    internal static Func<CancellationToken, Task> NeverCancel => token => Task.Delay(Timeout.Infinite, token);
    internal static Dictionary<string, string> EmptyCarrier => new(StringComparer.OrdinalIgnoreCase);
}
