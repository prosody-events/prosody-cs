namespace Prosody.Tests.TestHelpers;

internal static class TestDefaults
{
    internal const string BootstrapServers = "localhost:9092";

    internal static Func<Task> NeverCancel => () => new TaskCompletionSource().Task;
    internal static Dictionary<string, string> EmptyCarrier => new(StringComparer.OrdinalIgnoreCase);
}
