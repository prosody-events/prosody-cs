//! Logging bridge from Rust tracing to C# `ILoggerFactory`.
//!
//! This module bridges Rust's [`tracing`] system to C#'s
//! `Microsoft.Extensions.Logging.ILoggerFactory` via a `BoltFFI` callback
//! interface. The logging configuration is global and thread-safe: once
//! configured, all Prosody clients share the same logger.
//!
//! # Log Event Flow
//!
//! ```text
//! Rust tracing event (info!, warn!, etc.)
//!     │
//!     ▼ on_event()
//! LogSinkLayer (Rust, tracing Layer)
//!     │
//!     ▼ BoltFFI callback
//! LogSinkBridge (C#, implements LogSink)
//!     │
//!     ▼ ILogger.Log()
//! C# logging infrastructure
//! ```
//!
//! # Usage from C#
//!
//! ```csharp
//! // Configure once at startup
//! ProsodyLogging.Configure(loggerFactory);
//!
//! // All clients automatically use the configured logger
//! var client = new ProsodyClient(options);
//! ```
//!
//! # Thread Safety
//!
//! All functions in this module are thread-safe. The global log sink uses
//! write-once storage and supports lock-free reads.

use prosody::tracing::{
    flush_telemetry as flush_core_telemetry, initialize_tracing,
    shutdown_telemetry as shutdown_core_telemetry,
};
use std::collections::HashMap;
use std::error::Error;
use std::fmt::Debug;
use std::sync::{Arc, Once, OnceLock};
use tracing::field::{Field, Visit};
use tracing::{Event, Level, Subscriber};
use tracing_subscriber::Layer;
use tracing_subscriber::layer::Context;

use crate::error::FfiError;

/// Log severity level for messages from Rust to C#.
///
/// These values map directly to C#'s `Microsoft.Extensions.Logging.LogLevel`
/// enum, preserving integer discriminants for efficient FFI conversion.
#[boltffi::data]
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum LogLevel {
    /// Most detailed logging; may contain sensitive data.
    Trace = 0,
    /// Detailed information useful during development and debugging.
    Debug = 1,
    /// General operational information about application flow.
    Information = 2,
    /// Potential issues or unexpected behavior that does not prevent operation.
    Warning = 3,
    /// Errors that prevent a specific operation from completing.
    Error = 4,
    /// Unrecoverable errors that require immediate attention.
    Critical = 5,
}

impl From<Level> for LogLevel {
    fn from(level: Level) -> Self {
        match level {
            Level::TRACE => Self::Trace,
            Level::DEBUG => Self::Debug,
            Level::INFO => Self::Information,
            Level::WARN => Self::Warning,
            Level::ERROR => Self::Error,
        }
    }
}

/// Structured fields extracted from a tracing event, organized by type.
///
/// Fields are separated by their native types to preserve type information
/// across the FFI boundary, enabling proper structured logging in C#. This
/// allows C# loggers to format numeric values appropriately rather than
/// treating everything as strings.
///
/// Values that cannot be represented in the native type maps (such as `i128`
/// and `u128`) are converted to strings and stored in the
/// [`strings`](Self::strings) map.
#[boltffi::data]
#[derive(Debug, Clone, Default)]
pub struct LogFields {
    /// String-typed fields, including debug-formatted values and `i128`/`u128`.
    pub strings: HashMap<String, String>,
    /// Signed 64-bit integer fields (maps to C# `long`).
    pub i64s: HashMap<String, i64>,
    /// Unsigned 64-bit integer fields (maps to C# `ulong`).
    pub u64s: HashMap<String, u64>,
    /// 64-bit floating point fields (maps to C# `double`).
    pub f64s: HashMap<String, f64>,
    /// Boolean fields (maps to C# `bool`).
    pub bools: HashMap<String, bool>,
}

/// Global log sink instance.
///
/// Starts empty and accepts exactly one process-wide callback.
static LOG_SINK: OnceLock<Arc<dyn LogSink>> = OnceLock::new();

/// Callback interface for forwarding log messages from Rust to C#.
///
/// This trait is implemented by C# via `BoltFFI`'s callback interface
/// mechanism. The C# `LogSinkBridge` class implements this interface and
/// forwards log messages to `Microsoft.Extensions.Logging.ILogger`.
///
/// # Implementation Notes
///
/// Implementations must be thread-safe (`Send + Sync`) as log events may
/// originate from any thread in the Prosody runtime.
#[boltffi::export]
pub trait LogSink: Send + Sync {
    /// Checks whether logging is enabled for the specified level.
    ///
    /// Called before formatting the log message to avoid unnecessary string
    /// allocations when the level is filtered out on the C# side.
    fn is_enabled(&self, level: LogLevel) -> bool;

    /// Forwards a log event to the C# logging infrastructure.
    ///
    /// # Parameters
    ///
    /// - `level`: The severity level of this log event.
    /// - `target`: The Rust module path where the event originated (e.g.,
    ///   `prosody::consumer`).
    /// - `message`: The formatted log message text.
    /// - `file`: Source file path, if available from the tracing metadata.
    /// - `line`: Source line number, if available from the tracing metadata.
    /// - `fields`: Additional structured fields from the tracing event,
    ///   organized by type for proper C# type mapping.
    fn log(
        &self,
        level: LogLevel,
        target: String,
        message: String,
        file: Option<String>,
        line: Option<u32>,
        fields: LogFields,
    );
}

/// Initializes the tracing system with the [`LogSinkLayer`].
///
/// This function is idempotent: the first call initializes the tracing
/// subscriber, and subsequent calls are no-ops. Initialization failure
/// is logged to stderr since tracing is not yet available.
#[expect(clippy::print_stderr, reason = "tracing is not initialized yet")]
pub(crate) fn ensure_tracing_initialized() {
    static TRACING_INIT: Once = Once::new();

    TRACING_INIT.call_once(|| {
        if let Err(error) = initialize_tracing(Some(LogSinkLayer)) {
            eprintln!("failed to initialize tracing: {error:#}");
        }
    });
}

/// Configures the global log sink for forwarding Rust logs to C#.
///
/// Call this once at application startup before creating any `ProsodyClient`
/// instances. The log sink receives all tracing events from the Prosody
/// library.
///
/// This function is thread-safe. The first call stores the callback for the
/// process lifetime. Later calls return `false` and keep the first callback.
///
/// Also ensures the tracing system is initialized on first call.
///
/// # Parameters
///
/// - `sink`: The [`LogSink`] implementation provided by C#.
#[boltffi::export]
pub fn configure_log_sink(sink: Arc<dyn LogSink>) -> bool {
    // Ensure tracing is initialized before configuring the sink
    ensure_tracing_initialized();

    LOG_SINK.set(sink).is_ok()
}

/// Exports buffered OpenTelemetry spans and metrics without tearing the export
/// pipeline down.
///
/// Telemetry is process-global, so this is the correct call when a single
/// client is disposed while the process keeps running: it forces the batch span
/// processor and periodic metric reader to export immediately instead of on
/// their timers (~5s and 60s), so a short-lived client's tail telemetry is not
/// lost. A safe no-op when tracing was never initialized.
///
/// Blocks until the export completes; call it after async work has settled,
/// never from inside a handler.
///
/// # Errors
///
/// Returns [`FfiError::Tracing`] if the span or metric exporter fails to flush.
#[boltffi::export]
pub fn flush_telemetry() -> Result<(), FfiError> {
    flush_core_telemetry()?;
    Ok(())
}

/// Flushes all buffered telemetry and shuts the export pipeline down for the
/// whole process.
///
/// Because telemetry is process-global, this is only correct at actual process
/// exit — never per client, since it would tear down telemetry for every other
/// client in the process. A safe no-op when tracing was never initialized; see
/// [`flush_telemetry`] for the mid-run flush that keeps the pipeline alive.
///
/// Blocks until the final export completes; call it after async work has
/// settled, never from inside a handler.
///
/// # Errors
///
/// Returns [`FfiError::Tracing`] if the span or metric pipeline fails to shut
/// down.
#[boltffi::export]
pub fn shutdown_telemetry() -> Result<(), FfiError> {
    shutdown_core_telemetry()?;
    Ok(())
}

/// A [`tracing_subscriber::Layer`] that forwards events to the configured C#
/// log sink.
///
/// This layer is registered with the tracing subscriber during initialization.
/// When log events occur, it:
///
/// 1. Checks if a log sink is configured (early return if not).
/// 2. Queries [`LogSink::is_enabled`] to avoid formatting work for filtered
///    levels.
/// 3. Extracts the message and structured fields from the event.
/// 4. Forwards the complete event to C# via [`LogSink::log`].
#[derive(Clone, Default)]
pub struct LogSinkLayer;

impl<S: Subscriber> Layer<S> for LogSinkLayer {
    fn on_event(&self, event: &Event<'_>, _ctx: Context<'_, S>) {
        // Load the current log sink, return early if none configured
        let Some(sink) = LOG_SINK.get() else {
            return;
        };

        let metadata = event.metadata();
        if !metadata.is_event() {
            return;
        }

        // Check if this level is enabled before doing any formatting work
        let level = LogLevel::from(*metadata.level());
        if !sink.is_enabled(level) {
            return;
        }

        // Extract the message and fields from the event
        let mut visitor = MessageVisitor::new();
        event.record(&mut visitor);

        let target = metadata.target().to_owned();
        let file = metadata.file().map(ToOwned::to_owned);
        let line = metadata.line();

        // Forward to the C# log sink
        sink.log(level, target, visitor.message, file, line, visitor.fields);
    }
}

/// Visitor that extracts the message and structured fields from a tracing
/// event.
///
/// Implements [`tracing::field::Visit`] to collect all fields from an event.
/// The special `message` field is stored separately; all other fields are
/// placed into the appropriate type-specific map in [`LogFields`].
struct MessageVisitor {
    /// The extracted log message (from the `message` field).
    message: String,
    /// Structured fields organized by type.
    fields: LogFields,
}

impl MessageVisitor {
    /// Creates a new visitor with empty message and fields.
    fn new() -> Self {
        Self {
            message: String::new(),
            fields: LogFields::default(),
        }
    }
}

impl Visit for MessageVisitor {
    fn record_f64(&mut self, field: &Field, value: f64) {
        self.fields.f64s.insert(field.name().to_owned(), value);
    }

    fn record_i64(&mut self, field: &Field, value: i64) {
        self.fields.i64s.insert(field.name().to_owned(), value);
    }

    fn record_u64(&mut self, field: &Field, value: u64) {
        self.fields.u64s.insert(field.name().to_owned(), value);
    }

    fn record_i128(&mut self, field: &Field, value: i128) {
        self.fields
            .strings
            .insert(field.name().to_owned(), value.to_string());
    }

    fn record_u128(&mut self, field: &Field, value: u128) {
        self.fields
            .strings
            .insert(field.name().to_owned(), value.to_string());
    }

    fn record_bool(&mut self, field: &Field, value: bool) {
        self.fields.bools.insert(field.name().to_owned(), value);
    }

    fn record_str(&mut self, field: &Field, value: &str) {
        if field.name() == "message" {
            value.clone_into(&mut self.message);
        } else {
            self.fields
                .strings
                .insert(field.name().to_owned(), value.to_owned());
        }
    }

    fn record_error(&mut self, field: &Field, value: &(dyn Error + 'static)) {
        self.fields
            .strings
            .insert(field.name().to_owned(), value.to_string());
    }

    fn record_debug(&mut self, field: &Field, value: &dyn Debug) {
        if field.name() == "message" {
            self.message = format!("{value:?}");
        } else {
            self.fields
                .strings
                .insert(field.name().to_owned(), format!("{value:?}"));
        }
    }
}
