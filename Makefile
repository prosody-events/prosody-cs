# prosody-cs Makefile
#
# Usage:
#   make setup      - Install all required dependencies
#   make build      - Build the FFI crate and generate C# bindings
#   make test       - Run all tests
#   make lint       - Run all linters
#   make format     - Format all code
#   make clean      - Clean build artifacts

.PHONY: setup build build-ffi bindgen test lint lint-rust lint-csharp format format-rust format-csharp clean help

# Detect OS for library extension
UNAME := $(shell uname)
ifeq ($(UNAME), Darwin)
    LIB_EXT := dylib
    LIB_PREFIX := lib
else ifeq ($(UNAME), Linux)
    LIB_EXT := so
    LIB_PREFIX := lib
else
    LIB_EXT := dll
    LIB_PREFIX :=
endif

CDYLIB := target/debug/$(LIB_PREFIX)prosody_ffi.$(LIB_EXT)
CDYLIB_RELEASE := target/release/$(LIB_PREFIX)prosody_ffi.$(LIB_EXT)
BINDGEN_BRANCH := fix/issue-152-pascalcase-record-properties
BINDGEN_REPO := https://github.com/hadronzoo/uniffi-bindgen-cs.git

help:
	@echo "prosody-cs build commands:"
	@echo ""
	@echo "  make setup        - Install all required dependencies"
	@echo "  make build        - Build FFI crate and generate C# bindings"
	@echo "  make test         - Run all tests"
	@echo "  make lint         - Run all linters (Rust + C#)"
	@echo "  make format       - Format all code (Rust + C#)"
	@echo "  make clean        - Clean build artifacts"
	@echo ""
	@echo "Individual commands:"
	@echo "  make build-ffi    - Build only the Rust FFI crate"
	@echo "  make bindgen      - Generate only C# bindings"
	@echo "  make lint-rust    - Run Rust clippy"
	@echo "  make lint-csharp  - Run C# analyzers"
	@echo "  make format-rust  - Format Rust code"
	@echo "  make format-csharp- Format C# code"

# Install all required dependencies
setup:
	@echo "==> Installing Rust toolchains..."
	rustup install stable
	rustup install nightly
	@echo ""
	@echo "==> Installing Rust dependencies..."
	cargo fetch
	@echo ""
	@echo "==> Installing uniffi-bindgen-cs..."
	cargo install uniffi-bindgen-cs --git $(BINDGEN_REPO) --branch $(BINDGEN_BRANCH) --force
	@echo ""
	@echo "==> Restoring .NET tools (CSharpier, etc.)..."
	dotnet tool restore
	@echo ""
	@echo "==> Restoring .NET dependencies..."
	dotnet restore
	@echo ""
	@echo "Setup complete!"

# Build FFI crate (debug)
build-ffi:
	cargo build -p prosody-ffi

# Build FFI crate (release)
build-ffi-release:
	cargo build -p prosody-ffi --release

# Generate C# bindings from compiled cdylib
bindgen: $(CDYLIB)
	@mkdir -p src/Prosody/Generated
	uniffi-bindgen-cs --library $(CDYLIB) --config uniffi.toml --out-dir src/Prosody/Generated
	@mv src/Prosody/Generated/prosody_ffi.cs src/Prosody/Generated/ProsodyFfi.cs

bindgen-release: $(CDYLIB_RELEASE)
	@mkdir -p src/Prosody/Generated
	uniffi-bindgen-cs --library $(CDYLIB_RELEASE) --config uniffi.toml --out-dir src/Prosody/Generated
	@mv src/Prosody/Generated/prosody_ffi.cs src/Prosody/Generated/ProsodyFfi.cs

# Build everything (FFI + bindings)
build: build-ffi bindgen
	@echo "Build complete!"

build-release: build-ffi-release bindgen-release
	@echo "Release build complete!"

# Rust linting
lint-rust:
	cargo clippy --workspace -- -D warnings
	cargo clippy --workspace --all-targets -- -D warnings

# C# linting (build with warnings as errors)
lint-csharp:
	dotnet build --warnaserror

# Run all linters
lint: lint-rust lint-csharp
	@echo "All lints passed!"

# Rust formatting (nightly required for rustfmt.toml options)
format-rust:
	cargo +nightly fmt --all

# C# formatting
format-csharp:
	dotnet tool run dotnet-csharpier .

# Format all code
format: format-rust format-csharp
	@echo "All code formatted!"

# Check formatting without modifying files
format-check: format-check-rust format-check-csharp

format-check-rust:
	cargo +nightly fmt --all -- --check

format-check-csharp:
	dotnet tool run dotnet-csharpier --check .

# Run all tests
test: build
	dotnet test

# Clean build artifacts
clean:
	cargo clean
	rm -rf src/Prosody/Generated/*.cs
	dotnet clean || true
	find . -type d -name 'bin' -not -path './target/*' -exec rm -rf {} + 2>/dev/null || true
	find . -type d -name 'obj' -not -path './target/*' -exec rm -rf {} + 2>/dev/null || true

# Ensure cdylib exists before bindgen
$(CDYLIB): build-ffi

$(CDYLIB_RELEASE): build-ffi-release
