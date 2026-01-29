//! Prosody FFI bindings for C#.
//!
//! This crate provides FFI bindings for the Prosody Kafka client library,
//! using `UniFFI` for automatic C# bindings generation via uniffi-bindgen-cs.
//!
//! ## Building
//!
//! ```bash
//! # Build the cdylib (produces libprosody_ffi.dylib/.so/.dll)
//! cargo build -p prosody-ffi --release
//!
//! # Generate C# bindings (via uniffi-bindgen-cs)
//! uniffi-bindgen-cs --library target/release/libprosody_ffi.dylib -o src/Prosody/Generated
//! ```
//!
//! ## Module Organization
//!
//! - [`error`]: Error types for FFI boundary crossing
//! - [`events`]: Message and Timer event types passed to C# handlers
//! - [`handler`]: `NativeEventHandler` trait for FFI callback interface
//! - [`client`]: `ProsodyClient` service implementation
//! - [`types`]: Configuration types (`ClientOptions`)

use std::sync::LazyLock;
use tokio::runtime::Runtime;

pub mod client;
pub mod config;
pub mod error;
pub mod events;
pub mod handler;
pub mod types;

// Re-export key types for the UDL interface
pub use client::ProsodyClient;
pub use error::ProsodyError;
pub use events::{Context, Message, Timer};
pub use handler::{HandlerResultCode, NativeEventHandler};
pub use types::{ClientOptions, ConsumerState};

/// Global Tokio runtime for all async operations.
///
/// This runtime powers all async operations in the extension, including
/// message processing, scheduling, and communication with C#.
/// Lazily initialized on first use - critical for fork safety.
#[expect(
    clippy::expect_used,
    reason = "Runtime initialization failure is unrecoverable - library cannot function without it"
)]
pub static RUNTIME: LazyLock<Runtime> =
    LazyLock::new(|| Runtime::new().expect("Failed to create Tokio runtime"));

// Setup UniFFI scaffolding using proc-macro approach (no UDL file)
uniffi::setup_scaffolding!();
