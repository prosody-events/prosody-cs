using System.Runtime.InteropServices;

// Assembly-wide P/Invoke search-path hardening. Satisfies CA5392 (P/Invoke should
// declare DefaultDllImportSearchPaths) for every P/Invoke in the assembly, including
// the LibraryImport partials and the generated DllImport fallbacks in ProsodyFfi.cs.
// AssemblyDirectory is required so net10 single-file/AOT consumers probe the
// app-local prosody_ffi: the .NET 10 native-library-search breaking change stops
// single-file apps from adding the executable directory to the native search path
// and stops NativeAOT from setting rpath, so the app directory is probed only when
// the search flags include AssemblyDirectory. It is safe here because we load only
// our own native library shipped alongside the managed assembly. SafeDirectories is
// retained for the secure OS-level fallback (System32 + user dirs) for any transitive
// native dependencies.
// https://learn.microsoft.com/en-us/dotnet/core/compatibility/interop/10.0/native-library-search
[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.SafeDirectories)]
