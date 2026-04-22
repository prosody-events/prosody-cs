namespace Prosody.Tests.TestHelpers;

internal static class ActivityListenerIsolationCollection
{
    internal const string Name = "ActivityListenerIsolation";
}

[CollectionDefinition(ActivityListenerIsolationCollection.Name, DisableParallelization = true)]
public sealed class ActivityListenerIsolationCollectionDefinition;
