using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Prosody.Errors;

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
    internal static PermanentErrorAttribute? GetAttribute(
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
    /// Determines whether an exception represents a permanent error.
    /// </summary>
    /// <param name="exception">The exception to classify.</param>
    /// <param name="attribute">The method's <see cref="PermanentErrorAttribute"/>, if any.</param>
    /// <returns>
    /// <see langword="true"/> if the exception is permanent (should not retry); otherwise, <see langword="false"/>.
    /// </returns>
    internal static bool IsPermanentError(Exception exception, PermanentErrorAttribute? attribute)
    {
        // Priority 1: IPermanentError marker interface (runtime decision)
        if (exception is IPermanentError)
        {
            return true;
        }
        // Priority 2: PermanentErrorAttribute on the method (declaration-time)
        return attribute?.IsMatch(exception) == true;
        // Default: transient (will retry)
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
}
