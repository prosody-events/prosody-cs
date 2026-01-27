# prosody-cs Development Guidelines

Auto-generated from all feature plans. Last updated: 2026-01-26

## Active Technologies

- Rust 2024 Edition (1.85+), C# .NET 8.0/9.0/10.0 (001-csharp-interoptopus-bindings)

## Project Structure

```text
ffi/                    # Rust FFI crate (prosody-ffi) - FFI definitions (rlib only)
ffi-cdylib/             # Rust cdylib wrapper (prosody-ffi-cdylib) - produces .dylib/.so/.dll
ffi-build/              # Rust build crate (prosody-ffi-build) - C# binding generation
src/Prosody/            # C# library
test/Prosody.Tests/     # C# tests
```

The three-crate Rust pattern (ffi + ffi-cdylib + ffi-build) avoids Cargo filename
collisions. See: https://github.com/rust-lang/cargo/issues/6313

## Commands

```bash
# Build cdylib (produces libprosody_ffi.dylib/.so/.dll for C# to load)
cargo build -p prosody-ffi-cdylib

# Generate C# bindings (via build.rs)
cargo build -p prosody-ffi-build

# Run Rust tests
cargo test -p prosody-ffi

# Run lints
cargo clippy --workspace

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

Some warnings may come from proc-macro generated code (e.g., Interoptopus `#[ffi_service]`).
These must still be addressed - either by:
1. Adjusting the source code to avoid triggering the warning
2. Requesting an exception with `#[expect]` if unavoidable

## Commit Guidelines

- Do not reference internal phase numbers, task IDs, or spec document sections in commits or code comments
- Commit messages should describe what changed and why, not which planning artifact it came from

<!-- MANUAL ADDITIONS START -->
<!-- MANUAL ADDITIONS END -->
