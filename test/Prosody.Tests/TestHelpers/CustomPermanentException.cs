using Prosody.Errors;

namespace Prosody.Tests.TestHelpers;

/// <summary>A test exception that declares a permanent error through <see cref="IPermanentError"/>.</summary>
public sealed class CustomPermanentException : Exception, IPermanentError
{
    public CustomPermanentException() { }

    public CustomPermanentException(string message)
        : base(message) { }

    public CustomPermanentException(string message, Exception innerException)
        : base(message, innerException) { }
}
