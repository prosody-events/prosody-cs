//! Logging module for Prosody-CS.
//!
//! This module provides functionality to bridge Rust's tracing system to C#'s
//! `ILoggerFactory` via a `UniFFI` callback interface. The logging
//! configuration is global - once configured, all Prosody clients use the same
//! logger.
//!
//! ## Log Event Flow
//!
//! ```text
//! Rust tracing event (info!, warn!, etc.)
//!     │
//!     ▼ on_event()
//! LogSinkLayer (Rust, tracing Layer)
//!     │
//!     ▼ UniFFI callback
//! LogSinkBridge (C#, implements LogSink)
//!     │
//!     ▼ ILogger.Log()
//! C# logging infrastructure
//! ```
//!
//! ## Usage from C#
//!
//! ```csharp
//! // Configure once at startup
//! ProsodyLogging.Configure(loggerFactory);
//!
//! // All clients automatically use the configured logger
//! var client = new ProsodyClient(options);
//! ```

use arc_swap::ArcSwapOption;
use prosody::tracing::initialize_tracing;
use std::collections::HashMap;
use std::error::Error;
use std::fmt::Debug;
use std::sync::{Arc, Once};
use tracing::field::{Field, Visit};
use tracing::{Event, Level, Subscriber};
use tracing_subscriber::Layer;
use tracing_subscriber::layer::Context;

/// Log level for messages from Rust to C#.
///
/// Maps directly to C#'s `Microsoft.Extensions.Logging.LogLevel` enum values.
#[derive(Debug, Clone, Copy, PartialEq, Eq, uniffi::Enum)]
pub enum LogLevel {
    /// Most detailed logging, may contain sensitive data.
    Trace = 0,
    /// Detailed information useful during development.
    Debug = 1,
    /// General operational information.
    Information = 2,
    /// Potential issues or unexpected behavior.
    Warning = 3,
    /// Errors that prevent normal operation.
    Error = 4,
    /// Unrecoverable application/system crashes.
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

/// Structured fields from a tracing event, organized by type.
///
/// Fields are separated by their native types to preserve type information
/// across the FFI boundary, enabling proper structured logging in C#.
#[derive(Debug, Clone, Default, uniffi::Record)]
pub struct LogFields {
    /// String fields (includes debug-formatted values)
    pub strings: HashMap<String, String>,
    /// Signed integer fields (i64 / C# long)
    pub i64s: HashMap<String, i64>,
    /// Unsigned integer fields (u64 / C# ulong)
    pub u64s: HashMap<String, u64>,
    /// Floating point fields (f64 / C# double)
    pub f64s: HashMap<String, f64>,
    /// Boolean fields
    pub bools: HashMap<String, bool>,
}

/// Global log sink instance. Starts empty (logging disabled) until configured.
/// Uses `Arc<Arc<dyn LogSink>>` because arc-swap requires `Sized` types.
static LOG_SINK: ArcSwapOption<Arc<dyn LogSink>> = ArcSwapOption::const_empty();

/// Callback interface for log messages from Rust to C#.
///
/// This trait is implemented by C# via `UniFFI`'s callback interface mechanism.
/// The C# `LogSinkBridge` class implements this interface and forwards log
/// messages to `ILogger`.
#[uniffi::export(with_foreign)]
pub trait LogSink: Send + Sync {
    /// Check if a log level is enabled.
    ///
    /// This is called before formatting the log message to avoid unnecessary
    /// work when the level is filtered out on the C# side.
    fn is_enabled(&self, level: LogLevel) -> bool;

    /// Log a message.
    ///
    /// # Arguments
    ///
    /// * `level` - The log level
    /// * `target` - The module path (e.g., `prosody::consumer`)
    /// * `message` - The formatted log message
    /// * `file` - Source file path (if available)
    /// * `line` - Source line number (if available)
    /// * `fields` - Additional structured fields from the tracing event,
    ///   organized by type
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

/// Initialize the tracing system with the `LogSinkLayer`.
///
/// Idempotent - subsequent calls after the first are no-ops.
#[expect(clippy::print_stderr, reason = "tracing is not initialized yet")]
pub(crate) fn ensure_tracing_initialized() {
    static TRACING_INIT: Once = Once::new();

    TRACING_INIT.call_once(|| {
        if let Err(error) = initialize_tracing(Some(LogSinkLayer)) {
            eprintln!("failed to initialize tracing: {error:#}");
        }
    });
}

/// Configure the global log sink.
///
/// Call this once at application startup before creating any `ProsodyClient`
/// instances. The log sink receives all tracing events from the Prosody
/// library.
///
/// This function is thread-safe and can be called multiple times. Each call
/// replaces the previous log sink configuration.
///
/// This also ensures the tracing system is initialized.
///
/// # Arguments
///
/// * `sink` - The log sink implementation (from C#)
#[uniffi::export]
pub fn configure_log_sink(sink: Arc<dyn LogSink>) {
    // Ensure tracing is initialized before configuring the sink
    ensure_tracing_initialized();

    LOG_SINK.store(Some(Arc::new(sink)));
}

/// Clear the global log sink (disable logging).
///
/// After calling this function, log events will be silently discarded until
/// a new log sink is configured via `configure_log_sink`.
#[uniffi::export]
pub fn clear_log_sink() {
    LOG_SINK.store(None);
}

/// A tracing layer that forwards events to the configured C# log sink.
///
/// This layer is registered with the tracing subscriber during initialization.
/// When log events occur, it checks if a log sink is configured and forwards
/// the event to C# via the `UniFFI` callback interface.
#[derive(Clone, Default)]
pub struct LogSinkLayer;

impl<S: Subscriber> Layer<S> for LogSinkLayer {
    fn on_event(&self, event: &Event<'_>, _ctx: Context<'_, S>) {
        // Load the current log sink, return early if none configured
        let sink = LOG_SINK.load();
        let Some(sink) = sink.as_ref() else {
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

/// A visitor that extracts the message and all structured fields from a tracing
/// event.
struct MessageVisitor {
    message: String,
    fields: LogFields,
}

impl MessageVisitor {
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
