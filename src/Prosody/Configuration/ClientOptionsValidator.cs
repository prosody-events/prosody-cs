using Microsoft.Extensions.Options;

namespace Prosody.Configuration;

internal sealed class ClientOptionsValidator : IValidateOptions<ClientOptions>
{
    public ValidateOptionsResult Validate(string? name, ClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        List<string> failures = [];

        if (options.BootstrapServers is { Length: 0 })
        {
            failures.Add("BootstrapServers must not be empty.");
        }

        if (options.SubscribedTopics is { Length: 0 })
        {
            failures.Add("SubscribedTopics must not be empty.");
        }

        if (options.Mode is ClientMode.LowLatency && string.IsNullOrWhiteSpace(options.FailureTopic))
        {
            failures.Add("FailureTopic is required when Mode is LowLatency.");
        }

        CheckUnitInterval(options.DeferFailureThreshold, nameof(ClientOptions.DeferFailureThreshold), failures);
        CheckUnitInterval(options.MonopolizationThreshold, nameof(ClientOptions.MonopolizationThreshold), failures);
        CheckUnitInterval(options.SchedulerFailureWeight, nameof(ClientOptions.SchedulerFailureWeight), failures);

        if (options.SchedulerWaitWeight is { } schedulerWaitWeight)
        {
            if (!double.IsFinite(schedulerWaitWeight))
            {
                failures.Add("SchedulerWaitWeight must be finite.");
            }
            else if (schedulerWaitWeight < 0.0)
            {
                failures.Add("SchedulerWaitWeight must not be negative.");
            }
        }

        CheckTimeSpans(options, failures);

        if (options.IdempotenceVersion is { } idempotenceVersion && string.IsNullOrWhiteSpace(idempotenceVersion))
        {
            failures.Add("IdempotenceVersion must not be empty or whitespace.");
        }

        CheckArrayEntries(options.BootstrapServers, nameof(ClientOptions.BootstrapServers), failures);
        CheckArrayEntries(options.SubscribedTopics, nameof(ClientOptions.SubscribedTopics), failures);
        CheckArrayEntries(options.AllowedEvents, nameof(ClientOptions.AllowedEvents), failures);
        CheckArrayEntries(options.CassandraNodes, nameof(ClientOptions.CassandraNodes), failures);

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static void CheckUnitInterval(double? value, string name, List<string> failures)
    {
        if (value is { } intervalValue && (!double.IsFinite(intervalValue) || intervalValue is < 0.0 or > 1.0))
        {
            failures.Add($"{name} must be between 0.0 and 1.0.");
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
        CheckNonNegative(options.DeferSeekTimeout, nameof(ClientOptions.DeferSeekTimeout), failures);
        CheckNonNegative(options.MonopolizationWindow, nameof(ClientOptions.MonopolizationWindow), failures);
        CheckNonNegative(options.SchedulerMaxWait, nameof(ClientOptions.SchedulerMaxWait), failures);
        CheckNonNegative(options.CassandraRetention, nameof(ClientOptions.CassandraRetention), failures);
        CheckNonNegative(options.IdempotenceTtl, nameof(ClientOptions.IdempotenceTtl), failures);

        if (options.IdempotenceTtl is { Ticks: >= 0 } idempotenceTtl && idempotenceTtl < TimeSpan.FromMinutes(1))
        {
            failures.Add("IdempotenceTtl must be at least 1 minute.");
        }
    }

    private static void CheckNonNegative(TimeSpan? value, string name, List<string> failures)
    {
        if (value is { Ticks: < 0 })
        {
            failures.Add($"{name} must not be negative.");
        }
    }

    private static void CheckArrayEntries(string[]? list, string name, List<string> failures)
    {
        if (list is null)
        {
            return;
        }

        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < list.Length; i++)
        {
            var item = list[i];

            if (string.IsNullOrWhiteSpace(item))
            {
                failures.Add($"{name}[{i}] must not be empty or whitespace.");
                continue;
            }

            var normalizedItem = item.Trim();
            if (!string.Equals(item, normalizedItem, StringComparison.Ordinal))
            {
                failures.Add($"{name}[{i}] must not contain leading or trailing whitespace.");
            }

            if (!seen.Add(normalizedItem))
            {
                failures.Add($"{name} must not contain duplicates. Found duplicate: '{normalizedItem}'.");
            }
        }
    }
}
