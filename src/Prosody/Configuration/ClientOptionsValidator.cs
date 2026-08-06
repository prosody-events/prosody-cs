using Microsoft.Extensions.Options;
using Prosody.State;

namespace Prosody.Configuration;

internal sealed class ClientOptionsValidator : IValidateOptions<ClientOptions>
{
    public ValidateOptionsResult Validate(string? name, ClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        List<string> failures = [];

        CheckMaxConcurrency(options, failures);
        CheckTimeSpans(options, failures);
        CheckStateCollections(options, failures);

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    // The native scheduler validates the same bound; this check reports the
    // failure before any native resources are created.
    private static void CheckMaxConcurrency(ClientOptions options, List<string> failures)
    {
        if (options.MaxConcurrency is < 1)
        {
            failures.Add("MaxConcurrency must be at least 1.");
        }
    }

    private static void CheckStateCollections(ClientOptions options, List<string> failures)
    {
        if (options.StateCollections is not { } definitions)
        {
            return;
        }

        for (var i = 0; i < definitions.Length; i++)
        {
            if (definitions[i] is null)
            {
                failures.Add($"StateCollections[{i}] must not be null.");
            }
        }
    }

    private static void CheckTimeSpans(ClientOptions options, List<string> failures)
    {
        CheckNonNegative(options.Timeout, nameof(ClientOptions.Timeout), failures);
        CheckNonNegative(options.StallThreshold, nameof(ClientOptions.StallThreshold), failures);
        CheckNonNegative(options.ShutdownTimeout, nameof(ClientOptions.ShutdownTimeout), failures);
        CheckNonNegative(options.PollInterval, nameof(ClientOptions.PollInterval), failures);
        CheckNonNegative(options.CommitInterval, nameof(ClientOptions.CommitInterval), failures);
        CheckNonNegative(options.SlabSize, nameof(ClientOptions.SlabSize), failures);
        CheckNonNegative(options.SendTimeout, nameof(ClientOptions.SendTimeout), failures);
        CheckNonNegative(options.RetryBase, nameof(ClientOptions.RetryBase), failures);
        CheckNonNegative(options.MaxRetryDelay, nameof(ClientOptions.MaxRetryDelay), failures);
        CheckNonNegative(options.DeferBase, nameof(ClientOptions.DeferBase), failures);
        CheckNonNegative(options.DeferMaxDelay, nameof(ClientOptions.DeferMaxDelay), failures);
        CheckNonNegative(options.DeferFailureWindow, nameof(ClientOptions.DeferFailureWindow), failures);
        CheckNonNegative(options.LoaderSeekTimeout, nameof(ClientOptions.LoaderSeekTimeout), failures);
        CheckNonNegative(options.MonopolizationWindow, nameof(ClientOptions.MonopolizationWindow), failures);
        CheckNonNegative(options.SchedulerMaxWait, nameof(ClientOptions.SchedulerMaxWait), failures);
        CheckNonNegative(options.CassandraRetention, nameof(ClientOptions.CassandraRetention), failures);
    }

    private static void CheckNonNegative(TimeSpan? value, string name, List<string> failures)
    {
        if (value is { Ticks: < 0 })
        {
            failures.Add($"{name} must not be negative.");
        }
    }
}
