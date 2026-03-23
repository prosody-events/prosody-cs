namespace Prosody.Tests.TestHelpers;

internal static class LoggingIsolationCollection
{
    internal const string Name = "LoggingIsolation";
}

[CollectionDefinition(LoggingIsolationCollection.Name, DisableParallelization = true)]
public sealed class LoggingIsolationCollectionDefinition;
