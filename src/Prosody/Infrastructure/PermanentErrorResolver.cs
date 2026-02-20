using System.Collections.Concurrent;
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
    /// Cached attribute lookup keyed by (handler type, method name).
    /// A <see langword="null"/> value means the method was inspected but had no attribute.
    /// </summary>
    private static readonly ConcurrentDictionary<
        (Type HandlerType, string MethodName),
        PermanentErrorAttribute?
    > PermanentErrorHandlerCache = new();

    /// <summary>
    /// Gets the <see cref="PermanentErrorAttribute"/> from a handler method, if present.
    /// Results are cached so that repeated construction of bridges for the same handler type does not re-invoke reflection.
    /// </summary>
    /// <param name="handlerType">The handler implementation type.</param>
    /// <param name="methodName">The method name to inspect.</param>
    /// <returns>The attribute if found; otherwise, <see langword="null"/>.</returns>
    internal static PermanentErrorAttribute? GetAttribute(Type handlerType, string methodName) =>
        PermanentErrorHandlerCache.GetOrAdd(
            (handlerType, methodName),
            static key => ResolveAttribute(key.HandlerType, key.MethodName)
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

    private static PermanentErrorAttribute? ResolveAttribute(Type handlerType, string methodName)
    {
        // First, try the concrete type with both public and non-public bindings.
        // Non-public is needed for explicit interface implementations (which are private).
        // inherit: true walks the inheritance chain for base class attributes.
        // Uses GetMethods + Array.Find instead of GetMethod to avoid AmbiguousMatchException
        // if a handler declares overloads of the same method name.
        var method = Array.Find(
            handlerType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance),
            m => string.Equals(m.Name, methodName, StringComparison.Ordinal)
        );

        var attribute = method?.GetCustomAttribute<PermanentErrorAttribute>(inherit: true);
        if (attribute is not null)
        {
            return attribute;
        }

        // Fall back to the interface map — resolves the concrete method that implements
        // the interface method, which may carry the attribute even when the name doesn't
        // match (e.g., explicit implementations like IProsodyHandler.OnMessageAsync).
        var interfaceMethod = typeof(IProsodyHandler).GetMethod(methodName);
        if (interfaceMethod is null)
        {
            return null;
        }

        var mapping = handlerType.GetInterfaceMap(typeof(IProsodyHandler));
        for (var i = 0; i < mapping.InterfaceMethods.Length; i++)
        {
            if (mapping.InterfaceMethods[i] == interfaceMethod)
            {
                return mapping.TargetMethods[i].GetCustomAttribute<PermanentErrorAttribute>(inherit: true);
            }
        }

        return null;
    }
}
