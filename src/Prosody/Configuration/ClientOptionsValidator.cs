using Microsoft.Extensions.Options;
using Prosody.State;

namespace Prosody.Configuration;

internal sealed class ClientOptionsValidator : IValidateOptions<ClientOptions>
{
    public ValidateOptionsResult Validate(string? name, ClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        List<string> failures = [];

        CheckTimeSpans(options, failures);
        CheckStateCollections(options, failures);

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
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
        CheckNonNegative(options.PeerRegistrationTtl, nameof(ClientOptions.PeerRegistrationTtl), failures);
        if (options.PeerCacheCapacity == 0)
        {
            failures.Add($"{nameof(ClientOptions.PeerCacheCapacity)} must be greater than zero.");
        }
    }

    private static void CheckNonNegative(TimeSpan? value, string name, List<string> failures)
    {
        if (value is { Ticks: < 0 })
        {
            failures.Add($"{name} must not be negative.");
        }
    }
}
