using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Prosody.Errors;
using Prosody.Messaging;

namespace Prosody.Infrastructure;

/// <summary>
/// Resolves <see cref="PermanentErrorAttribute"/> instances from handler methods,
/// caching results per handler type to avoid repeated reflection.
/// </summary>
internal static class PermanentErrorResolver
{
    /// <summary>
    /// Cached attribute lookup keyed by (handler type, interface type, method name).
    /// A <see langword="null"/> value means the method was inspected but had no attribute.
    /// </summary>
    private static readonly ConcurrentDictionary<
        (Type HandlerType, Type InterfaceType, string MethodName),
        PermanentErrorAttribute?
    > PermanentErrorHandlerCache = new();

    /// <summary>
    /// Gets the <see cref="PermanentErrorAttribute"/> from a handler method, if present.
    /// Results are cached so that repeated construction of bridges for the same handler type does not re-invoke reflection.
    /// </summary>
    /// <param name="handlerType">The handler implementation type.</param>
    /// <param name="interfaceType">The implemented handler interface type.</param>
    /// <param name="methodName">The method name to inspect.</param>
    /// <returns>The attribute if found; otherwise, <see langword="null"/>.</returns>
    [RequiresUnreferencedCode("Reads PermanentErrorAttribute from handler methods via reflection.")]
    [RequiresDynamicCode("GetInterfaceMap requires the handler type's methods to be preserved at runtime.")]
    private static PermanentErrorAttribute? GetAttribute(
        [DynamicallyAccessedMembers(
            DynamicallyAccessedMemberTypes.PublicMethods | DynamicallyAccessedMemberTypes.NonPublicMethods
        )]
            Type handlerType,
        [DynamicallyAccessedMembers(
            DynamicallyAccessedMemberTypes.PublicMethods | DynamicallyAccessedMemberTypes.NonPublicMethods
        )]
            Type interfaceType,
        string methodName
    ) =>
        PermanentErrorHandlerCache.GetOrAdd(
            (handlerType, interfaceType, methodName),
            static key => ResolveAttribute(key.HandlerType, key.InterfaceType, key.MethodName)
        );

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
            GetAttribute(handlerType, interfaceType, nameof(IProsodyHandler<object>.OnMessageAsync)),
            GetAttribute(handlerType, interfaceType, nameof(IProsodyHandler<object>.OnExciseAsync)),
            GetAttribute(handlerType, interfaceType, nameof(IProsodyHandler<object>.OnTimerAsync))
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
        if (interfaceMethod is not null)
        {
            var mapping = handlerType.GetInterfaceMap(interfaceType);
            for (var i = 0; i < mapping.InterfaceMethods.Length; i++)
            {
                if (mapping.InterfaceMethods[i] == interfaceMethod)
                {
                    return mapping.TargetMethods[i].GetCustomAttribute<PermanentErrorAttribute>(inherit: true);
                }
            }
        }

        return null;
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
