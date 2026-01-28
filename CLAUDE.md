# prosody-cs Development Guidelines

Auto-generated from all feature plans. Last updated: 2026-01-28

## Active Technologies

- Rust 2024 Edition (1.85+), C# .NET 8.0/9.0/10.0
- UniFFI for FFI bindings (via uniffi-bindgen-cs)

## Project Structure

```text
ffi/                           # Rust FFI crate (prosody-ffi) - produces cdylib
src/Prosody/                   # C# library
src/Prosody/Generated/         # Generated C# bindings (uniffi-bindgen-cs output)
test/Prosody.Tests/            # C# tests
```

## Commands

```bash
# Build cdylib (produces libprosody_ffi.dylib/.so/.dll)
cargo build -p prosody-ffi --release

# Generate C# bindings (use --config for namespace and access modifier)
uniffi-bindgen-cs --library target/release/libprosody_ffi.dylib --config uniffi.toml -o src/Prosody/Generated

# Run Rust tests
cargo test -p prosody-ffi

# Run lints
cargo clippy --workspace
cargo clippy --workspace --all-targets  # includes tests

# Build C# project
dotnet build

# Run C# tests
dotnet test
```

## Code Style

Rust 2024 Edition (1.85+), C# .NET 8.0/9.0/10.0: Follow standard conventions

## Lint Policy

**ALL clippy warnings must be fixed.** Run both commands and ensure zero warnings:

```bash
cargo clippy --workspace
cargo clippy --workspace --all-targets  # includes tests
```

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

## Commit Guidelines

- Do not reference internal phase numbers, task IDs, or spec document sections in commits or code comments
- Commit messages should describe what changed and why, not which planning artifact it came from

<!-- MANUAL ADDITIONS START -->
<!-- MANUAL ADDITIONS END -->
