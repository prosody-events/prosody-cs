using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Prosody.Errors;
using Prosody.Messaging;

namespace Prosody.Infrastructure;

/// <summary>
/// Resolves <see cref="PermanentErrorAttribute"/> instances from handler methods. Each bridge
/// resolves them once, when it is built.
/// </summary>
internal static class PermanentErrorResolver
{
    /// <summary>
    /// Creates a classifier from the <see cref="PermanentErrorAttribute"/> on each handler method of
    /// <paramref name="handler"/>. A method without the attribute classifies every error as transient.
    /// </summary>
    /// <param name="handler">The handler implementation.</param>
    /// <param name="interfaceType">
    /// The implemented handler interface. <see cref="IProsodyHandler{TPayload}"/> and
    /// <see cref="IProsodyRequestHandler{TPayload, TResponse}"/> use the same method names.
    /// </param>
    [RequiresUnreferencedCode("Reads PermanentErrorAttribute from handler methods via reflection.")]
    [RequiresDynamicCode("GetInterfaceMap requires the handler type's methods to be preserved at runtime.")]
    internal static IPermanentErrorClassifier Classifier(object handler, Type interfaceType)
    {
        ArgumentNullException.ThrowIfNull(handler);
        var handlerType = handler.GetType();
        return new AttributeClassifier(
            ResolveAttribute(handlerType, interfaceType, nameof(IProsodyHandler<object>.OnMessageAsync)),
            ResolveAttribute(handlerType, interfaceType, nameof(IProsodyHandler<object>.OnExciseAsync)),
            ResolveAttribute(handlerType, interfaceType, nameof(IProsodyHandler<object>.OnTimerAsync))
        );
    }

    [RequiresUnreferencedCode("Reads PermanentErrorAttribute from handler methods via reflection.")]
    [RequiresDynamicCode("GetInterfaceMap requires the handler type's methods to be preserved at runtime.")]
    private static PermanentErrorAttribute? ResolveAttribute(
        [DynamicallyAccessedMembers(
            DynamicallyAccessedMemberTypes.PublicMethods | DynamicallyAccessedMemberTypes.NonPublicMethods
        )]
            Type handlerType,
        [DynamicallyAccessedMembers(
            DynamicallyAccessedMemberTypes.PublicMethods | DynamicallyAccessedMemberTypes.NonPublicMethods
        )]
            Type interfaceType,
        string methodName
    )
    {
        // Resolve through the interface map — this finds the concrete method that implements
        // the interface method, which carries the attribute even for explicit implementations
        // (e.g. `Task IProsodyHandler<T>.OnMessageAsync(...)`) where the target method's
        // mangled name doesn't match the interface method name.
        var interfaceMethod = Array.Find(
            interfaceType.GetMethods(),
            m => string.Equals(m.Name, methodName, StringComparison.Ordinal)
        );
        var mapping = handlerType.GetInterfaceMap(interfaceType);
        var index = Array.IndexOf(mapping.InterfaceMethods, interfaceMethod);
        return index < 0
            ? null
            : mapping.TargetMethods[index].GetCustomAttribute<PermanentErrorAttribute>(inherit: true);
    }

    /// <summary>Classifies an error by the <see cref="PermanentErrorAttribute"/> of the method that threw it.</summary>
    private sealed class AttributeClassifier(
        PermanentErrorAttribute? message,
        PermanentErrorAttribute? excise,
        PermanentErrorAttribute? timer
    ) : IPermanentErrorClassifier
    {
        public bool IsMessageErrorPermanent(Exception exception) => message?.IsMatch(exception) == true;

        public bool IsExciseErrorPermanent(Exception exception) => excise?.IsMatch(exception) == true;

        public bool IsTimerErrorPermanent(Exception exception) => timer?.IsMatch(exception) == true;
    }
}
