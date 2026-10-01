using System.Runtime.InteropServices;

namespace Prosody.State;

/// <summary>
/// An ascending, half-open range of keys for <see cref="KeyQuery.Range"/>. The range includes
/// <see cref="Start"/> and excludes <see cref="End"/>. Keys sort in the order of their UTF-8 bytes.
/// </summary>
/// <param name="Start">The first key in the range, or <see langword="null"/> for no lower bound.</param>
/// <param name="End">The key after the range, or <see langword="null"/> for no upper bound.</param>
[StructLayout(LayoutKind.Auto)]
public readonly record struct KeyRange(string? Start, string? End);
