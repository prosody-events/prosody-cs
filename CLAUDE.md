# CLAUDE.md

Development patterns and practices for prosody-cs: C# bindings for the Prosody Kafka client library.

## Critical Rules

**Error Handling (Rust):**

- Never use `expect`, `unwrap`, `panic`, or `ok()` - forbidden by lints
- Propagate errors with `?` unless explicitly authorized to swallow
- Use `thiserror` for structured errors; box only when Clippy warns

**Code Quality:**

- Clippy must pass for code and tests - zero warnings tolerated
- Never suppress warnings with `#[allow(...)]` without permission
- Run: `cargo clippy`, `cargo clippy --tests`, `cargo doc`, `cargo +nightly fmt`

**Debugging Discipline:**

- Never claim "found the issue" without rigorous proof
- Evidence first (logs, tests, reproducible behavior) → hypothesis → test → verify

**Style:**

- Prefer `use` statements over fully qualified prefixes
- Methods without `self` should be functions (except `new` and similar)
- Ask before large structural changes

**Git:**

- Never add self-attribution to commits, PR descriptions, or code comments
- Use conventional commits (e.g., `fix:`, `feat:`, `docs:`, `refactor:`)
- Always use `gh` for GitHub operations (issues, PRs, API queries) instead of web URLs

## Code Organization

**Order within Rust files (topological by dependencies):**

1. Constants → Statics → Types → Implementations → Functions → Errors (bottom)

```rust
const MAX_RETRIES: usize = 3;
static CONFIG: LazyLock<Config> = LazyLock::new(Config::default);

pub struct Manager {
    /* ... */
}
impl Manager { /* ... */ }
pub fn helper_fn() { /* ... */ }

#[derive(Debug, Error)]
pub enum ManagerError { /* ... */ }
```

## Project Structure

```text
ffi/                           # Rust FFI crate (prosody-ffi) - produces cdylib
src/Prosody/                   # C# library
src/Prosody/Generated/         # Generated C# bindings (uniffi-bindgen-cs output)
test/Prosody.Tests/            # C# tests (unit, integration, helpers)
bench/Prosody.Benchmarks/      # BenchmarkDotNet performance benchmarks
```

## Commands

Use the Makefile for all common tasks. Run `make help` for a quick reference.

**Primary commands:**
```bash
make setup      # Install all dependencies (run once after cloning)
make build      # Build FFI crate (debug), generate bindings, build .NET
make test       # Build and run all tests (requires docker-compose services)
make bench      # Run all benchmarks (Release mode)
make lint       # Run all linters (Rust clippy + C# analyzers/format check)
make format     # Format all code (Rust + C#)
make clean      # Clean all build artifacts
```

**Build commands:**
```bash
make build-ffi         # Build only the Rust FFI crate (debug)
make build-ffi-release # Build only the Rust FFI crate (release)
make build-release     # Full release build (FFI + bindings + .NET)
make bindgen           # Generate only C# bindings from debug cdylib
make bindgen-release   # Generate only C# bindings from release cdylib
make pack              # Build NuGet package locally (current platform only)
```

**Lint commands:**
```bash
make lint-rust    # Run Rust clippy (--workspace and --all-targets)
make lint-csharp  # Run C# build --warnaserror and dotnet format --verify-no-changes
```

**Format commands:**
```bash
make format-rust        # Format Rust code (cargo +nightly fmt)
make format-csharp      # Format C# code (CSharpier)
make format-check       # Check all formatting without changes
make format-check-rust  # Check Rust formatting only
make format-check-toml  # Check TOML formatting (taplo)
make format-check-csharp # Check C# formatting only
```

## Native Library

The FFI crate produces a platform-specific native library:
- macOS: `libprosody_ffi.dylib`
- Linux: `libprosody_ffi.so`
- Windows: `prosody_ffi.dll`

The Makefile handles platform detection automatically and copies the library to the correct location for .NET to find it.

## Error Classification

Distinguish permanent from transient errors for retry logic:

```rust
#[derive(Debug, Clone, Copy)]
pub enum ErrorType {
    Permanent,  // Business logic - don't retry
    Transient,  // Network/timeout - retry with backoff
}

trait ClassifyError {
    fn classify_error(&self) -> ErrorType;
}
```

## Testing

**ALL tests must be written in C#**, not Rust. The Rust FFI crate (`ffi/`) contains zero tests.

- Unit tests: `test/Prosody.Tests/Unit/`
- Integration tests: `test/Prosody.Tests/Integration/`
- Test helpers: `test/Prosody.Tests/TestHelpers/`

**Infrastructure requirements:**

Integration tests require Kafka and Cassandra. Start with:
```bash
docker-compose up -d kafka cassandra
```

Environment variables (defaults set in Makefile):
- `PROSODY_BOOTSTRAP_SERVERS=localhost:9094`
- `PROSODY_CASSANDRA_NODES=localhost:9042`
- `PROSODY_CASSANDRA_KEYSPACE=prosody_test`

Run tests with `make test` or directly:
```bash
dotnet run --project test/Prosody.Tests --framework net10.0 --no-build
```

**Test organization:**
- Use xUnit v3 (tests are executable projects, not test assemblies)
- Integration tests inherit from `IntegrationTestBase`
- Use `TestHelpers/` for shared fixtures and utilities

**Integration tests:** When running slow integration tests, write output to a temp file rather than piping to `grep`,
`head`, or `tail`. Re-running tests is expensive; keep output files around for exploration:

```bash
# Good: preserve output for exploration
dotnet test 2>&1 | tee /tmp/test_output.log
grep FAILED /tmp/test_output.log

# Bad: loses output, forces expensive re-runs
dotnet test 2>&1 | grep FAILED
```

## Lint Policy

**ALL clippy warnings must be fixed.** Run `make lint` and ensure zero warnings.

### Forbidden: `#[allow(...)]` attributes

`#[allow(...)]` is **FORBIDDEN** in this project. Never add them.

If you encounter an existing `#[allow(...)]`:
1. Remove it
2. Fix the underlying issue properly

If you believe an exception is truly necessary:
1. **Ask permission first** - explain why the warning cannot be fixed
2. If granted, use `#[expect(...)]` (not `#[allow(...)]`) with a reason:
   ```rust
   #[expect(clippy::some_lint, reason = "explanation approved by maintainer")]
   ```

### Warnings from macro-generated code

Some warnings may come from proc-macro generated code (e.g., UniFFI macros).
These must still be addressed - either by:
1. Adjusting the source code to avoid triggering the warning
2. Requesting an exception with `#[expect]` if unavoidable

## API Design

**Traits:** Keep generic with associated types; use type erasure only for FFI (JS/Python/Ruby/C#)

**Configuration:** Use `#[derive(Builder, Validate)]`, mark builders with `#[must_use]`

```rust
#[derive(Builder, Clone, Debug, Validate)]
pub struct Configuration {
    #[validate(length(min = 1_u64))]
    bootstrap_servers: Vec<String>,

    #[validate(range(min = 1, max = 10000))]
    max_concurrency: usize,
}
```

## C# Style

- .NET 8.0/9.0/10.0 multi-targeting
- File-scoped namespaces (enforced by `.editorconfig`)
- Private fields use `_camelCase` prefix
- Private static readonly fields use `PascalCase`
- CSharpier for formatting (`dotnet tool run dotnet-csharpier .`)

## FFI / UniFFI

The project uses UniFFI with proc-macro approach (no UDL file). Key files:
- `ffi/src/lib.rs` - main entry point with `uniffi::setup_scaffolding!()`
- `uniffi.toml` - bindgen configuration
- Generated bindings go to `src/Prosody/Generated/ProsodyFfi.cs`

Regenerate bindings after Rust API changes:
```bash
make bindgen  # or make build to rebuild everything
```

**Note:** Generated bindings are patched to add `#pragma warning disable CA5392` for P/Invoke warnings that cannot be fixed in generated code.

## Common Patterns

- Use `parking_lot` over `std::sync`
- Use `tokio::sync` primitives (`Notify`, channels, `select!`) for async
- Mark builders with `#[must_use]`
- Use `LazyLock` for expensive static initialization
- Dependencies: `parking_lot`, `simd-json` (non-ARM)

## Commit Guidelines

- Do not reference internal phase numbers, task IDs, or spec document sections in commits or code comments
- Commit messages should describe what changed and why, not which planning artifact it came from

## Research

- Automatically use context7 for code generation and library documentation

## Active Technologies

- Rust 2024 Edition (1.85+), C# .NET 8.0/9.0/10.0
- UniFFI for FFI bindings (via uniffi-bindgen-cs)
- xUnit v3 for testing
- Kafka (bitnami 3.7) and Cassandra for integration tests
- OpenTelemetry for tracing
