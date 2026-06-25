using System.Runtime.InteropServices;

// Assembly-wide P/Invoke search-path hardening. CA5392 (P/Invoke should
// declare DefaultDllImportSearchPaths) for every P/Invoke in the assembly, including
// the LibraryImport partials and the generated DllImport fallbacks in ProsodyFfi.cs.
// It is safe here because we load only our own native library shipped alongside the managed assembly.
// https://learn.microsoft.com/en-us/dotnet/core/compatibility/interop/10.0/native-library-search
[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.SafeDirectories)]
