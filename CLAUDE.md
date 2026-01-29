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

Use the Makefile for all common tasks:

```bash
make setup      # Install all dependencies (run once after cloning)
make build      # Build FFI crate and generate C# bindings
make test       # Run lints and tests
make lint       # Run Rust clippy and C# build
make format     # Format Rust code
make clean      # Clean build artifacts
```

## Code Style

Rust 2024 Edition (1.85+), C# .NET 8.0/9.0/10.0: Follow standard conventions

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

## Commit Guidelines

- Do not reference internal phase numbers, task IDs, or spec document sections in commits or code comments
- Commit messages should describe what changed and why, not which planning artifact it came from

<!-- MANUAL ADDITIONS START -->

## GitHub CLI

Always use `gh` for GitHub operations (issues, PRs, API queries) instead of web URLs.

<!-- MANUAL ADDITIONS END -->
