using Microsoft.Extensions.Options;
using Prosody.State;

namespace Prosody.Configuration;

internal sealed class ClientOptionsValidator : IValidateOptions<ClientOptions>
{
    public ValidateOptionsResult Validate(string? name, ClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        List<string> failures = [];

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
}
