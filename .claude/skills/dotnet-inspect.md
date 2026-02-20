---
name: dotnet-inspect
description: Query .NET APIs across NuGet packages, platform libraries, and local files. Search for types, list API surfaces, compare versions, find extension methods and implementors. Use whenever you need to answer questions about .NET library contents.
invocable: false
---

> Source: [richlander/dotnet-inspect](https://github.com/richlander/dotnet-inspect)

# dotnet-inspect

Query .NET library APIs — the same commands work across NuGet packages, platform libraries (System.*, Microsoft.AspNetCore.*), and local .dll/.nupkg files.

## Installation

`dotnet-inspect` is installed as a global .NET tool. Invoke it directly:

```bash
dotnet-inspect <command> [options]
```

If not installed, install with:

```bash
dotnet tool install -g dotnet-inspect
```

## When to Use This Skill

- **"What types are in this package?"** — `find` searches by glob pattern
- **"What's the API surface?"** — `api` lists types, members, signatures, type shape
- **"What changed between versions?"** — `diff` classifies breaking/additive changes
- **"What extends this type?"** — `extensions` finds extension methods/properties
- **"What implements this interface?"** — `implements` finds concrete types
- **"What does this type depend on?"** — `depends` walks the type hierarchy upward
- **"What version/metadata does this have?"** — `package` and `library` inspect metadata

## Search Scope

Search commands (`find`, `extensions`, `implements`, `depends`) work across all of .NET:

```bash
dotnet-inspect find "Chat*"                                    # default scope (platform + curated)
dotnet-inspect find "Chat*" --platform                         # platform frameworks only
dotnet-inspect find "Chat*" --extensions                       # Microsoft.Extensions.* packages
dotnet-inspect find "Chat*" --aspnetcore                       # Microsoft.AspNetCore.* packages
dotnet-inspect find "Chat*" --platform --extensions            # combine scopes
dotnet-inspect find "Chat*" --package Foo                      # specific NuGet package
dotnet-inspect find "Chat*" --platform --package Foo           # platform + a specific package
```

Scope flags are combinable — use multiple flags to widen the search. `--package` works on all commands. `api`, `library`, `diff` also accept `--platform <name>` for a specific platform library.

## Examples by Task

### List API surface

```bash
dotnet-inspect api System.Text.Json                                           # all types in library
dotnet-inspect api System.Text.Json JsonSerializer                            # members of a type
dotnet-inspect api 'HashSet<T>' --platform System.Collections --shape         # type shape diagram
dotnet-inspect api JsonSerializer --package System.Text.Json -m Serialize     # filter to member
```

### Search for types

```bash
dotnet-inspect find "*Handler*" --package System.CommandLine
dotnet-inspect find "Option*,Argument*,Command*" --package System.CommandLine --terse
dotnet-inspect find "*Logger*"
```

### Compare versions (migrations)

```bash
dotnet-inspect diff --package System.CommandLine@2.0.0-beta4.22272.1..2.0.2
dotnet-inspect diff --package System.Text.Json@9.0.0..10.0.0 --breaking
dotnet-inspect diff JsonSerializer --package System.Text.Json@9.0.0..10.0.0
```

### Find extensions, implementors, and dependencies

```bash
dotnet-inspect extensions HttpClient                           # what extends HttpClient?
dotnet-inspect extensions IServiceCollection                   # across default scope
dotnet-inspect implements Stream                               # what extends Stream?
dotnet-inspect implements IDisposable --platform               # across all platform frameworks
dotnet-inspect depends 'INumber<TSelf>'                        # type dependency hierarchy
```

### Inspect packages and libraries

```bash
dotnet-inspect package System.Text.Json                        # metadata, latest version
dotnet-inspect package System.Text.Json --versions             # available versions
dotnet-inspect package search "Azure.AI"                       # search NuGet for packages
dotnet-inspect library System.Text.Json                        # library metadata, symbols
dotnet-inspect library ./bin/MyLib.dll                         # local file
```

### Search with prefix scoping

```bash
dotnet-inspect find "Chat*" --package-prefix Azure.AI                        # search all Azure.AI.* packages
dotnet-inspect extensions IChatClient --package-prefix Microsoft.Extensions.AI
```

## Command Reference

| Command | Purpose |
| ------- | ------- |
| `api` | Public API surface — types, members, signatures, `--shape` for hierarchy |
| `find` | Search for types by glob pattern across any scope |
| `diff` | Compare API surfaces between versions — breaking/additive classification |
| `extensions` | Find extension methods/properties for a type |
| `implements` | Find types implementing an interface or extending a base class |
| `depends` | Walk the type dependency hierarchy upward (interfaces, base classes) |
| `package` | Package metadata, files, versions, dependencies, `search` for NuGet discovery |
| `library` | Library metadata, symbols, references, dependencies |

## Key Syntax

- **Generic types** need quotes: `'Option<T>'`, `'IEnumerable<T>'`
- **Positional args** for `api`: `api <source> <type> <member>` (not flags)
- **Version pinning**: `--package Name@version` (e.g., `--package System.Text.Json@10.0.0`)
- **Diff ranges** use `..`: `--package System.Text.Json@9.0.0..10.0.0`
- **Signatures** include `params` and default values from metadata

## Full Documentation

For comprehensive syntax, edge cases, and the flag compatibility matrix:

```bash
dotnet-inspect llmstxt
```
