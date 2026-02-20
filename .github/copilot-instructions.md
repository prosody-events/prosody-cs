# Copilot instructions for prosody-cs

## Build, test, and lint commands

- One-time setup: `make setup`
- Full local build (Rust FFI + generated C# bindings + .NET build): `make build`
- FFI-only build: `make build-ffi` (or `make build-ffi-release`)
- Regenerate C# bindings after Rust FFI API changes: `make bindgen` (or `make bindgen-release`)
- Start integration dependencies: `docker-compose up -d kafka cassandra`
- Run all tests (build + test runner): `make test`
- Run tests directly (xUnit v3 executable runner): `dotnet run --project test/Prosody.Tests --framework net10.0 --no-build`
- Run a single test method: `dotnet run --project test/Prosody.Tests --framework net10.0 --no-build -- --filter-method "Prosody.Tests.Unit.ProsodyClientBuilderTests.CreateClientReturnsBuilder"`
- Run a single test class: `dotnet run --project test/Prosody.Tests --framework net10.0 --no-build -- --filter-class "Prosody.Tests.Unit.ProsodyClientBuilderTests"`
- List discoverable tests: `dotnet run --project test/Prosody.Tests --framework net10.0 --no-build -- --list-tests`
- Run all linters: `make lint`
- Rust lint only: `make lint-rust`
- C# lint only: `make lint-csharp`
- Formatting check (no edits): `make format-check`

## High-level architecture

- `ffi/` is the Rust UniFFI boundary crate (`prosody-ffi`) over the upstream Rust `prosody` client library.
  - `ffi/src/client.rs` exports the FFI `ProsodyClient` object, subscription lifecycle, send path, and state accessors.
  - `ffi/src/config.rs` converts `ClientOptions` into Prosody builder components (consumer/retry/defer/monopolization/scheduler/Cassandra).
  - `ffi/src/lib.rs` re-exports FFI types and enables UniFFI scaffolding.
- `src/Prosody/Generated/ProsodyFfi.cs` is generated interop code from `uniffi-bindgen-cs` and is consumed by hand-written wrappers.
- `src/Prosody/` is the public C# API layer:
  - `ProsodyClientBuilder` + `ClientOptions` provide fluent configuration plus advanced tuning via `Configure(...)`.
  - `ProsodyClient` wraps native calls with typed JSON serialization, `CancellationToken` bridging, and trace-carrier propagation.
  - `EventHandlerBridge` adapts `IProsodyHandler` callbacks to native handler results (success/transient/permanent).
  - `ProsodyLogging` + `Logging/LogSinkBridge.cs` bridge Rust tracing into `ILogger` (`Prosody.Native` category).
- `test/Prosody.Tests/` is a C# xUnit v3 executable test project (multi-target net8/net9/net10) with:
  - unit tests in `Unit/`
  - integration tests in `Integration/` using `IntegrationTestFixture` and Kafka/Cassandra
  - shared helpers in `TestHelpers/`

## Key conventions for this repository

- Use Makefile targets as the default workflow entry points (build/test/lint/format/bindgen).
- Do not manually edit `src/Prosody/Generated/ProsodyFfi.cs`; regenerate it with `make bindgen`/`make build`.
- All tests are written in C# (`test/Prosody.Tests`); Rust FFI crate tests are not used in this repo.
- Integration tests assume Kafka + Cassandra and use:
  - `PROSODY_BOOTSTRAP_SERVERS` (default `localhost:9094`)
  - `PROSODY_CASSANDRA_NODES` (default `localhost:9042`)
  - `PROSODY_CASSANDRA_KEYSPACE` (default `prosody_test`)
- Rust error/lint policy is strict:
  - no `unwrap`, `expect`, `panic`, or `ok()`
  - fix clippy warnings (including tests) rather than suppressing with `#[allow(...)]`
- Rust file layout convention is topological: constants/statics -> types -> impls -> functions -> error types at bottom.
- `ClientOptions` semantics across C# and Rust FFI use nullable/optional fields to mean “fall back to environment variable or library default”.
- Handler exception classification convention:
  - `IPermanentError` or `[PermanentError(...)]` => permanent (non-retry)
  - otherwise => transient (retryable)
- C# style conventions are analyzer-enforced (see `.editorconfig`)
