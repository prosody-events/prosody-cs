using System.Diagnostics.CodeAnalysis;

namespace Prosody.Messaging;

/// <summary>Contains one successful subsystem response.</summary>
/// <remarks>
/// A subsystem can respond with JSON <see langword="null"/>, and that response is a success whose
/// <see cref="Value"/> is <see langword="null"/>. A caller that can receive null uses a nullable
/// <typeparamref name="T"/>, such as <c>RequestAsync&lt;Order, Order?&gt;</c>.
/// </remarks>
/// <param name="Value">The decoded response.</param>
public sealed record Success<T>([AllowNull] [property: AllowNull] T Value) : Outcome<T>;
