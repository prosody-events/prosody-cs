using Prosody.Messaging;

namespace Prosody.Tests.Unit;

/// <summary>Preserves subscription signatures used by compiled callers.</summary>
public sealed class SubscriptionCompatibilityTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ExistingSubscribeSignaturesRemainAvailable(bool responds, bool classifier)
    {
        var handlerType = responds ? typeof(IProsodyRequestHandler<,>) : typeof(IProsodyHandler<>);
        var method = Assert.Single(
            typeof(ProsodyClient).GetMethods(),
            method =>
                method.Name == nameof(ProsodyClient.SubscribeAsync)
                && method.GetGenericArguments().Length == (responds ? 2 : 1)
                && method.GetParameters().Length == (classifier ? 2 : 1)
                && method.GetParameters()[0].ParameterType.GetGenericTypeDefinition() == handlerType
                && (!classifier || method.GetParameters()[1].ParameterType == typeof(IPermanentErrorClassifier))
        );

        Assert.Equal(typeof(Task), method.ReturnType);
        Assert.True(method.IsGenericMethodDefinition);
    }
}
