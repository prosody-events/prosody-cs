# prosody-cs Makefile
#
# Usage:
#   make setup      - Install all required dependencies
#   make build      - Build the FFI crate and generate C# bindings
#   make test       - Run all tests
#   make lint       - Run all linters
#   make format     - Format all code
#   make clean      - Clean build artifacts

.PHONY: setup build build-ffi build-ffi-release bindgen bindgen-release patch-generated-bindings test lint lint-rust lint-csharp format format-rust format-csharp format-check format-check-rust format-check-csharp clean help copy-native-to-test-output

# ==============================================================================
# Platform Detection
# ==============================================================================

UNAME := $(shell uname)
ARCH := $(shell uname -m)

ifeq ($(UNAME),Darwin)
    LIB_EXT := dylib
    LIB_PREFIX := lib
    ifeq ($(ARCH),arm64)
        RUST_TARGET := aarch64-apple-darwin
    else
        RUST_TARGET := x86_64-apple-darwin
    endif
else ifeq ($(UNAME),Linux)
    LIB_EXT := so
    LIB_PREFIX := lib
    ifeq ($(ARCH),aarch64)
        RUST_TARGET := aarch64-unknown-linux-gnu
    else
        RUST_TARGET := x86_64-unknown-linux-gnu
    endif
else
    # Windows (via MSYS2/Git Bash/etc.)
    LIB_EXT := dll
    LIB_PREFIX :=
    ifeq ($(ARCH),ARM64)
        RUST_TARGET := aarch64-pc-windows-msvc
    else
        RUST_TARGET := x86_64-pc-windows-msvc
    endif
endif

# ==============================================================================
# Path Configuration
# ==============================================================================

# Native library name (platform-specific prefix and extension)
LIB_NAME := $(LIB_PREFIX)prosody_ffi.$(LIB_EXT)

# Cargo output paths
CARGO_TARGET_DEBUG := target/debug
CARGO_TARGET_RELEASE := target/release
CDYLIB := $(CARGO_TARGET_DEBUG)/$(LIB_NAME)
CDYLIB_RELEASE := $(CARGO_TARGET_RELEASE)/$(LIB_NAME)

# Path where .NET csproj expects native libraries (matches RustOutputDebugPath in csproj)
# This creates: prosody-ffi/target/<rust-target>/debug/<lib>
DOTNET_NATIVE_BASE := prosody-ffi/target/$(RUST_TARGET)
DOTNET_NATIVE_DEBUG := $(DOTNET_NATIVE_BASE)/debug
DOTNET_NATIVE_RELEASE := $(DOTNET_NATIVE_BASE)/release

# C# generated bindings output
BINDINGS_DIR := src/Prosody/Generated

# uniffi-bindgen-cs configuration
BINDGEN_REPO := https://github.com/hadronzoo/uniffi-bindgen-cs.git
BINDGEN_BRANCH := fix/issue-152-pascalcase-record-properties

# ==============================================================================
# Help
# ==============================================================================

help:
	@echo "prosody-cs build commands:"
	@echo ""
	@echo "  make setup        - Install all required dependencies"
	@echo "  make build        - Build FFI crate and generate C# bindings"
	@echo "  make build-release- Build FFI crate (release) and generate C# bindings"
	@echo "  make test         - Run all tests"
	@echo "  make lint         - Run all linters (Rust + C#)"
	@echo "  make format       - Format all code (Rust + C#)"
	@echo "  make clean        - Clean build artifacts"
	@echo ""
	@echo "Individual commands:"
	@echo "  make build-ffi    - Build only the Rust FFI crate (debug)"
	@echo "  make bindgen      - Generate only C# bindings"
	@echo "  make lint-rust    - Run Rust clippy"
	@echo "  make lint-csharp  - Run C# analyzers"
	@echo "  make format-rust  - Format Rust code"
	@echo "  make format-csharp- Format C# code"

# ==============================================================================
# Setup
# ==============================================================================

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
	@echo "==> Restoring .NET tools..."
	dotnet tool restore
	@echo ""
	@echo "==> Restoring .NET dependencies..."
	dotnet restore
	@echo ""
	@echo "Setup complete!"

# ==============================================================================
# Build
# ==============================================================================

# Build FFI crate (debug) and copy to where .NET expects it
build-ffi:
	cargo build -p prosody-ffi
	@mkdir -p "$(DOTNET_NATIVE_DEBUG)"
	cp "$(CDYLIB)" "$(DOTNET_NATIVE_DEBUG)/$(LIB_NAME)"
	@echo "Native library copied to $(DOTNET_NATIVE_DEBUG)/$(LIB_NAME)"

# Build FFI crate (release) and copy to where .NET expects it
build-ffi-release:
	cargo build -p prosody-ffi --release
	@mkdir -p "$(DOTNET_NATIVE_RELEASE)"
	cp "$(CDYLIB_RELEASE)" "$(DOTNET_NATIVE_RELEASE)/$(LIB_NAME)"
	@echo "Native library copied to $(DOTNET_NATIVE_RELEASE)/$(LIB_NAME)"

# Generate C# bindings from compiled cdylib
bindgen: $(CDYLIB)
	@mkdir -p "$(BINDINGS_DIR)"
	@rm -f "$(BINDINGS_DIR)"/*.cs
	uniffi-bindgen-cs --library "$(CDYLIB)" --config uniffi.toml --out-dir "$(BINDINGS_DIR)"
	@mv "$(BINDINGS_DIR)/prosody_ffi.cs" "$(BINDINGS_DIR)/ProsodyFfi.cs"
	@# Add pragma to suppress P/Invoke warnings in generated code
	@$(MAKE) --no-print-directory patch-generated-bindings
	@echo "C# bindings generated in $(BINDINGS_DIR)"

bindgen-release: $(CDYLIB_RELEASE)
	@mkdir -p "$(BINDINGS_DIR)"
	@rm -f "$(BINDINGS_DIR)"/*.cs
	uniffi-bindgen-cs --library "$(CDYLIB_RELEASE)" --config uniffi.toml --out-dir "$(BINDINGS_DIR)"
	@mv "$(BINDINGS_DIR)/prosody_ffi.cs" "$(BINDINGS_DIR)/ProsodyFfi.cs"
	@# Add pragma to suppress P/Invoke warnings in generated code
	@$(MAKE) --no-print-directory patch-generated-bindings
	@echo "C# bindings generated in $(BINDINGS_DIR)"

# Patch generated bindings to add pragma warning disable for P/Invoke warnings
# CA5392: P/Invoke methods should use DefaultDllImportSearchPaths (generated code, can't fix)
patch-generated-bindings:
	@for f in "$(BINDINGS_DIR)"/*.cs; do \
		if [ -f "$$f" ]; then \
			tmp=$$(mktemp); \
			head -5 "$$f" > "$$tmp"; \
			echo "" >> "$$tmp"; \
			echo "#pragma warning disable CA5392" >> "$$tmp"; \
			echo "" >> "$$tmp"; \
			tail -n +6 "$$f" >> "$$tmp"; \
			echo "" >> "$$tmp"; \
			echo "#pragma warning restore CA5392" >> "$$tmp"; \
			mv "$$tmp" "$$f"; \
		fi; \
	done

# Build everything (FFI + bindings + .NET + copy native lib to test output)
build: build-ffi bindgen
	@echo "Building .NET project..."
	dotnet build
	@$(MAKE) --no-print-directory copy-native-to-test-output
	@echo "Build complete!"

build-release: build-ffi-release bindgen-release
	@echo "Building .NET project (Release)..."
	dotnet build -c Release
	@echo "Release build complete!"

# Ensure cdylib exists before bindgen (triggers build-ffi if needed)
$(CDYLIB): build-ffi

$(CDYLIB_RELEASE): build-ffi-release

# ==============================================================================
# Lint
# ==============================================================================

lint-rust:
	cargo clippy --workspace -- -D warnings
	cargo clippy --workspace --all-targets -- -D warnings

lint-csharp:
	dotnet build --warnaserror
	dotnet format --verify-no-changes --verbosity minimal

lint: lint-rust lint-csharp
	@echo "All lints passed!"

# ==============================================================================
# Format
# ==============================================================================

format-rust:
	cargo +nightly fmt --all

format-csharp:
	dotnet tool run dotnet-csharpier .

format: format-rust format-csharp
	@echo "All code formatted!"

format-check-rust:
	cargo +nightly fmt --all -- --check

format-check-csharp:
	dotnet tool run dotnet-csharpier --check .

format-check: format-check-rust format-check-csharp

# ==============================================================================
# Test
# ==============================================================================

# Copy native library to test output directories for runtime loading
# .NET's DllImport doesn't automatically probe runtimes/<rid>/native/ during tests
copy-native-to-test-output:
	@echo "Copying native library to test output directories..."
	@for tfm in net8.0 net9.0 net10.0; do \
		dir="test/Prosody.Tests/bin/Debug/$$tfm"; \
		if [ -d "$$dir" ]; then \
			cp "$(CDYLIB)" "$$dir/$(LIB_NAME)" && \
			echo "  -> $$dir/$(LIB_NAME)"; \
		fi; \
	done

# Run all tests
# xUnit v3 test projects are executables - run directly for proper output
test: build
	@echo "Running tests..."
	dotnet run --project test/Prosody.Tests --no-build

# ==============================================================================
# Clean
# ==============================================================================

clean:
	cargo clean
	rm -rf "$(BINDINGS_DIR)"/*.cs
	rm -rf prosody-ffi/target
	dotnet clean 2>/dev/null || true
	find . -type d -name 'bin' -not -path './target/*' -exec rm -rf {} + 2>/dev/null || true
	find . -type d -name 'obj' -not -path './target/*' -exec rm -rf {} + 2>/dev/null || true
	@echo "Clean complete!"
