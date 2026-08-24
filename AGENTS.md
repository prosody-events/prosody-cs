# CLAUDE.md

Development patterns and practices for prosody-cs: C# bindings for the
Prosody Kafka client library. A UniFFI crate (`ffi/`) wraps the published
`prosody` Rust crate; the C# library (`src/Prosody/`) wraps the generated
bindings.

## Design Principles

These come before everything else. Every change is judged against them.

**Write code that is simple, clear, well-factored, elegant, easy to
understand, correct, and idiomatic.** A reader should grasp the intent without
effort. If a change makes the code harder to read, the change is wrong, even
if it is faster or shorter. If two designs are correct, pick the one that is
easier to delete.

**Make invalid states unrepresentable in the type system.** When a compiler
can prove a contract, no test, comment, or convention has to. In Rust, prefer
distinct types for distinct concepts, restricted constructors, and `enum` sum
types over flag fields. In C#, give the public surface precise types instead
of loose ones. If a bug class can be made uncompilable, do that instead of
writing a runtime check.

**Delete more than you add.** Every change should leave the codebase smaller,
simpler, or both. If you must add code, look first for duplication you can
fold, abstractions that no longer pay rent, dead branches, and stale comments.
The end-state diff should net negative whenever the task allows. Line count is
not the only axis: plain duplicated arms often read better than generic
machinery.

**Identify, document, and enforce invariants.** For every load-bearing piece
of state: name the invariant, write it down near the type or function that
owns it, enforce it in the type system if you can, otherwise assert it at the
boundary, and cover it with a test. If you cannot name the invariant, you do
not yet understand the code well enough to change it.

**Leave the codebase better than you found it.** Drive-by simplifications are
encouraged when they are scoped to the area you are already touching. Do not
sprawl — but do not walk past obvious cleanup either.

## Definition of Done

No change is complete until every line below holds. These are acts, not
aspirations — perform each one; do not merely agree with it:

1. `make lint` — zero warnings (Rust clippy `--workspace --all-targets` plus
   C# `--warnaserror` and format checks). `cargo doc` — zero warnings.
   `make format-check` passes.
2. After any Rust API change, regenerate the bindings (`make bindgen`) and
   commit the regenerated output with the change.
3. `make test 2>&1 | tee /tmp/test_output.log` — re-running slow suites is
   expensive; grep the file, not the pipe.
4. Every new or converted test was proved falsifiable once: inject the
   failure, watch it go red, revert.
5. Every deleted test names its surviving stronger test in the commit message.
6. Everything the change replaces is gone — code, tests, config fields, doc
   vocabulary (see Redesign hygiene). "The new thing works" is half done.
7. Every claim written this session — doc cross-reference, "covered by" note,
   exemplar path — was verified to resolve, not recalled from memory.
8. The diff is net-negative, or each addition is individually justified.

## Critical Rules

**Error Handling (Rust):**

- Never use `expect`, `unwrap`, `panic`, or `ok()` - forbidden by lints
- Propagate errors with `?` unless explicitly authorized to swallow
- Use `thiserror` for structured errors; box only when Clippy warns

**Memory (Rust):**

- **Never leak memory.** `std::mem::forget`, `Box::leak`, and `ManuallyDrop`
  without an explicit reclamation path are forbidden. If a test must simulate
  "Drop never ran", seed the underlying state directly; forgetting is never
  the shortcut.
- **No unbounded keyed RAM.** Any in-memory structure keyed by message key or
  collection must have a fixed capacity bound. Every in-memory map names its
  removal path; self-draining maps are fine, but the drain is still named.

**Allocation and layout (tiger style — https://tigerstyle.dev/):**

- No hot-path allocation that is not upfront and bounded. A steady-state path
  (per message, per timer fire, per handler call) must not allocate a buffer
  whose size is discovered at runtime and grown as needed.
- Pick the buffer by what is known about the size: compile-time constant →
  stack array; runtime-varying but almost always small → `SmallVec` sized to
  the common case; genuinely unbounded → `Vec::with_capacity` sized once.
- `with_capacity` excuses the sizing, never the allocation. A per-call heap
  allocation on a steady-state path is the defect itself.
- Never add a gratuitous allocation to satisfy the borrow checker. Reach for
  a function item, an index, or a borrow before a scratch `Vec`.
- No amortized resize buffers on the hot path. If a reusable scratch buffer
  is unavoidable, allocate it once at construction with a fixed bound.
- **Lay data out for the access pattern.** A hot path that scans one or two
  fields across many entries must find those fields contiguously. Reach the
  full record only for the entry the scan selects. An array of `Option<Arc<T>>`
  turns a two-word decision into one heap dereference per entry, and thrashes
  the CPU cache. Memory bandwidth is the bottleneck today, so the scan decides
  the layout, not the record. Don't thrash the cache. False sharing counts:
  keep atomics that different threads write off one line.
- Simplicity is not sacrificed for this. When zero-alloc and simple genuinely
  conflict, keep it simple and leave a comment naming the allocation.

**Code Quality:**

- Lint, doc, and format gates live in Definition of Done — zero warnings
  tolerated. See Lint Policy below for the `#[allow(...)]` rules.
- Never introduce `dyn` without permission — prefer generics and associated
  types. The type-erased surface this binding consumes already lives in the
  published `prosody` crate.

**JSON codec:**

- This binding never defines its own payload codec. Payload encoding and
  decoding belong to the `prosody` crate's codec; the binding passes payload
  bytes through it.
- `serde_json`, `simd_json`, and the `json!` macro are banned in Rust
  production code here for payload handling. Tests may use `serde_json::Value`
  as a concrete payload type.

**Redesign hygiene:**

When a design is replaced, remove *all* of it in the same change —
half-deleted designs are where bloat and bug re-introduction live:

- Sweep the old design's vocabulary from every doc comment, binding, and
  example. A stale doc can instruct a reader to re-introduce a fixed bug.
- Code whose only caller is its own test is dead — delete both together.
- Struct fields threaded through configs but only read at construction are
  residue from a superseded design — remove them end-to-end.
- Do not build surface ahead of a caller: delete zero-caller paths, or make
  them owner-confirmed, tested features.

**Debugging Discipline:**

- Never claim "found the issue" without rigorous proof
- Evidence first (logs, tests, reproducible behavior) → hypothesis → test → verify

**Documentation:**

- **All written text for this project must conform to ASD-STE100 (Simplified
  Technical English). No written text is exempt.** This rule applies to
  documentation, comments, READMEs, plans, issues, reviews, chat responses,
  commit messages, PR text, and user-facing text. Apply these primary STE rules:
  - Use the active voice. Write instructions in the imperative.
  - Write short sentences. Use 20 words or fewer for instructions. Use 25
    words or fewer for descriptions.
  - Write one instruction per sentence. Keep one topic per paragraph. Use a
    maximum of six sentences in each paragraph.
  - Use a word with only one meaning. Use the same word for the same thing.
  - Use simple verb tenses. Do not use an "-ing" form as a verb when a simple
    tense is correct.
  - Do not use a noun cluster of more than three nouns.
  - Use approved technical names and technical verbs consistently.
- Write doc comments for a reader unfamiliar with the codebase. Lead with
  what the thing is, how to use it, and what guarantee it gives — not the
  internal mechanism.
- Short declarative sentences, one idea each. At most one parenthetical aside
  per comment, never nested.
- Never argue with an imagined reviewer. State what the code does and the
  invariant it upholds. Mention a rejected alternative only when a maintainer
  would plausibly reintroduce it, as its own plain sentence.
- No invented compound jargon. Spell the idea out in ordinary words;
  established terms keep their standard form.
- State an invariant at the type or function that owns it, once. Reference
  the owning type elsewhere instead of restating.
- Be concise. Bad or needless docs hurt readability — prefer fewer, sharper
  words.
- Avoid vague metaphor filler in prose, comments, and commit/PR text ("north
  star", "surface area", "lean into", "double-click", "first-class citizen").
  Say the concrete thing instead.

**Style:**

- Structure function bodies as logical paragraphs. Keep statements for one
  operation together. Add a blank line before a new concept or operation.
- Prefer `use` statements over fully qualified prefixes
- Methods without `self` should be functions (except `new` and similar)
- Ask before large structural changes
- Default to `pub(crate)`/`pub(super)` in Rust; make something `pub` only as
  a deliberate API decision.
- Keep trait constraints as local as possible: put a constraint on the
  function that needs it, not the struct.
- When a proposed simplification is examined and rejected, record the ruling
  in one sentence at the site so the next pass does not re-litigate it.

**Git:**

- Never add self-attribution to branch names, commits, PR titles, PR
  descriptions, or code comments.
- Use conventional commits for commit titles and PR titles (e.g., `fix:`,
  `feat:`, `docs:`, `refactor:`).
- PR titles and descriptions are written for a reader who is not intimately
  familiar with the project. Lead with what changed and why.
- Never hard-wrap paragraphs in GitHub PR descriptions, PR comments, or issue
  text. Each prose paragraph is one single line; blank lines separate
  paragraphs.
- PR descriptions never include a test plan or a list of verification steps.
- Do not reference internal phase numbers, task IDs, or spec document
  sections in commits or code comments. Commit messages describe what changed
  and why, not which planning artifact it came from.
- Never run `git reset` or `git checkout` that would destroy uncommitted or
  committed changes without explicit human permission. Prefer `git stash`, an
  explicit commit, or `git restore --staged <path>`.
- Always use `gh` for GitHub operations (issues, PRs, API queries) instead of
  web URLs.

## Code Organization

**Maximum file size: 500 lines.** A file that exceeds it is subdivided into
modules. Split along a seam the code already has, and give each module a doc
comment naming what it owns. Re-export from the parent so the split is
invisible to callers. A split that only balances line counts is worse than
the long file; find the real seam.

**Prefer one-word module names.** A two-word name usually means the module
owns more than one concern, or the name restates its parent's path. A
compound name is right only when the compound is the domain term.

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
examples/                      # Runnable C# examples
```

## Types, Bindings, and Examples

- The C# library is the public typed surface. Every Rust API change flows
  through regenerated bindings into hand-written C# wrappers in the same
  commit.
- `src/Prosody/Generated/` is generated by uniffi-bindgen-cs; never edit it
  by hand. Regenerate with `make bindgen`.
- `examples/` holds runnable C# examples (e.g. `keyed_state_windowing.cs`);
  keep them compiling and current with the public API.

## Commands

Use the Makefile for all common tasks. Run `make help` for a quick reference.

**Primary commands:**
```bash
make setup      # Install all dependencies (run once after cloning)
make build      # Build FFI crate (debug), generate bindings, build .NET
make test       # Build and run all tests (requires docker-compose services)
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

A permanent error discards the in-flight message. An error the caller's code
causes (bad input, wrong argument shape) classifies as transient unless the
caller explicitly declares it permanent — a transient error retries and stays
visible, so no message is silently lost.

## Concurrency Invariants (inherited from prosody)

- **One handler per key, system-wide.** The framework guarantees at most one
  message or timer handler for a given key executes anywhere in the cluster
  at any moment. Never design for concurrent writers on the same key — that
  scenario cannot occur.
- **At most one partition owner.** Kafka partition assignment guarantees one
  consumer group member owns each partition at a time.
- These invariants are why distributed locks and optimistic concurrency are
  never needed for per-key state. The framework provides the exclusivity;
  binding code and examples can assume it.

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

**Test principles:**

- Drive tests by invariants, not by paths. Name the invariant (round-trip,
  parity, idempotence) and prefer few broad tests over many narrow example
  tests. Use realistic inputs, not happy-path toys.
- A test must be able to fail. When you write or convert a test, prove it can
  go red once: inject the failure, watch it fail, revert.
- Never delete a test without naming, in the commit, the surviving test that
  covers the same invariant at least as strongly.
- Never use `sleep` except for backpressure simulation. Wait on events,
  channels, or notifications with a deadline — the deadline is a hang-guard,
  never the assertion.
- Root-cause every intermittent failure. A passing re-run proves nothing.
  Extract the reproducer and land it as a deterministic regression test.
- Use assertions; never swallow errors in test code.

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

A `Validate` derive with zero rules is a false promise. Every field that can
express a degenerate value either gets a validation rule or the consuming
code must provably tolerate it.

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
- For concurrent hash sets/maps, use `scc` (`scc::HashSet` / `scc::HashMap`),
  never a `Mutex<HashSet>` / `Mutex<HashMap>`; pair it with
  `ahash::RandomState`. In async code prefer its async interface.
- Use `tokio::sync` primitives (`Notify`, channels, `select!`) for async
- Independent I/O runs concurrently, never serially. Drive N independent
  reads through a bounded `buffered(N)` (order-preserving) or
  `buffer_unordered(N)` (unordered). Reserve serial `await` for genuinely
  dependent reads, where each result determines the next.
- Drive futures over non-tokio primitives through the cooperative budget:
  wrap each per-item future with `tokio::task::coop::cooperative` inside the
  producing closure, so a drain of ready items cannot starve the worker.
- Mark builders with `#[must_use]`
- Use `LazyLock` for expensive static initialization
- Dependencies: `parking_lot`, `simd-json` (non-ARM)

## Tracing / OpenTelemetry

- Instrument with `#[instrument]`, never a hand-built `info_span!` +
  `.instrument(...)`. Use `skip_all` plus explicit `fields`, and `err` to
  record failures on the span.
- Span level is audience: spans the user's own code causes export at info;
  framework-internal spans use `level = "debug"`.
- Record unsigned integers as `i64` — the OTel layer stringifies
  `u64`/`usize`. Record attribute values with `%` (Display) where the type
  allows.
- Import tracing macros from `tracing` directly — never `use tracing::log::…`;
  no bridge is installed, so events logged through it silently vanish.
- Never cache a `Span` — cache an `opentelemetry::Context` and recreate spans
  on read. Cloning a span creates another reference to the same underlying
  span; finishing one finishes all.

## Workflows

When launching multi-agent workflows:

- Select model and effort per task by complexity — do not let every agent
  inherit the session model. Never downgrade a stage whose output gates a
  commit or ship decision.
- Disable the advisor in every agent prompt.
- Keep structured-output schemata trivially simple: flat objects with a few
  short bounded fields; put detail in report files.

## Research

- Automatically use context7 for code generation and library documentation

## CI planning

- Check Cargo Rail after each CI path or repository layout change.
- Confirm that README-only changes select documentation jobs only.
- Confirm that source changes select all required build and test jobs.
- Add `rail.toml` only when the default rules classify a path incorrectly.

## Active Technologies

- Rust 2024 Edition (1.85+), C# .NET 8.0/9.0/10.0
- UniFFI for FFI bindings (via uniffi-bindgen-cs)
- xUnit v3 for testing
- Kafka (bitnami 3.7) and Cassandra for integration tests
- OpenTelemetry for tracing
