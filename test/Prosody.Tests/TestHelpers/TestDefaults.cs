namespace Prosody.Tests.TestHelpers;

internal static class TestDefaults
{
    internal const string BootstrapServers = "localhost:9092";

    internal static Dictionary<string, string> EmptyCarrier => new(StringComparer.OrdinalIgnoreCase);
}
