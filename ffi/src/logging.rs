//! Logging bridge from Rust tracing to C# `ILoggerFactory`.
//!
//! This module bridges Rust's [`tracing`] system to C#'s
//! `Microsoft.Extensions.Logging.ILoggerFactory` via a `UniFFI` callback
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
//!     ▼ UniFFI callback
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
//! atomic operations for lock-free access, making it safe to call
//! [`configure_log_sink`] and [`clear_log_sink`] from any thread.

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

/// Log severity level for messages from Rust to C#.
///
/// These values map directly to C#'s `Microsoft.Extensions.Logging.LogLevel`
/// enum, preserving integer discriminants for efficient FFI conversion.
#[derive(Debug, Clone, Copy, PartialEq, Eq, uniffi::Enum)]
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
#[derive(Debug, Clone, Default, uniffi::Record)]
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
/// Starts empty (logging disabled) until configured via [`configure_log_sink`].
/// Uses `Arc<Arc<dyn LogSink>>` because [`ArcSwapOption`] requires `Sized`
/// types, and `dyn LogSink` is unsized.
static LOG_SINK: ArcSwapOption<Arc<dyn LogSink>> = ArcSwapOption::const_empty();

/// Callback interface for forwarding log messages from Rust to C#.
///
/// This trait is implemented by C# via `UniFFI`'s callback interface mechanism.
/// The C# `LogSinkBridge` class implements this interface and forwards log
/// messages to `Microsoft.Extensions.Logging.ILogger`.
///
/// # Implementation Notes
///
/// Implementations must be thread-safe (`Send + Sync`) as log events may
/// originate from any thread in the Prosody runtime.
#[uniffi::export(with_foreign)]
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
/// This function is thread-safe and may be called multiple times. Each call
/// atomically replaces the previous log sink; there is no gap where log
/// events would be lost during replacement.
///
/// Also ensures the tracing system is initialized on first call.
///
/// # Parameters
///
/// - `sink`: The [`LogSink`] implementation provided by C#.
#[uniffi::export]
pub fn configure_log_sink(sink: Arc<dyn LogSink>) {
    // Ensure tracing is initialized before configuring the sink
    ensure_tracing_initialized();

    LOG_SINK.store(Some(Arc::new(sink)));
}

/// Clears the global log sink, disabling logging to C#.
///
/// After calling this function, log events are silently discarded until
/// a new log sink is configured via [`configure_log_sink`].
///
/// This is useful for graceful shutdown or temporarily disabling logging.
#[uniffi::export]
pub fn clear_log_sink() {
    LOG_SINK.store(None);
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
