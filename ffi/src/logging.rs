//! Logging module for Prosody-CS.
//!
//! This module provides functionality to bridge Rust's tracing system to C#'s
//! `ILoggerFactory` via a `UniFFI` callback interface. The logging configuration
//! is global - once configured, all Prosody clients use the same logger.
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

use arc_swap::ArcSwap;
use prosody::tracing::initialize_tracing;
use std::error::Error;
use std::fmt::Debug;
use std::sync::{Arc, LazyLock, Once};
use tracing::field::{Field, Visit};
use tracing::{Event, Level, Metadata, Subscriber};
use tracing_subscriber::layer::Context;
use tracing_subscriber::Layer;

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

impl From<LogLevel> for Level {
    fn from(level: LogLevel) -> Self {
        match level {
            LogLevel::Trace => Self::TRACE,
            LogLevel::Debug => Self::DEBUG,
            LogLevel::Information => Self::INFO,
            LogLevel::Warning => Self::WARN,
            LogLevel::Error | LogLevel::Critical => Self::ERROR,
        }
    }
}

/// A no-op log sink that discards all messages.
struct NullLogSink;

impl LogSink for NullLogSink {
    fn is_enabled(&self, _level: LogLevel) -> bool {
        false
    }

    fn log(
        &self,
        _level: LogLevel,
        _target: String,
        _message: String,
        _file: Option<String>,
        _line: Option<u32>,
    ) {
    }
}

/// Global log sink instance. Defaults to a no-op sink that discards all messages.
/// Uses `Arc<Arc<dyn LogSink>>` to work around arc-swap's `Sized` requirement.
static LOG_SINK: LazyLock<ArcSwap<Arc<dyn LogSink>>> =
    LazyLock::new(|| ArcSwap::from_pointee(Arc::new(NullLogSink) as Arc<dyn LogSink>));

/// Guard for one-time tracing initialization.
static TRACING_INIT: Once = Once::new();

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
    fn log(
        &self,
        level: LogLevel,
        target: String,
        message: String,
        file: Option<String>,
        line: Option<u32>,
    );
}

/// Initialize the tracing system.
///
/// This function sets up the Rust tracing infrastructure with the `LogSinkLayer`.
/// It should be called once at application startup, typically by the C# wrapper
/// before any logging configuration.
///
/// This function is idempotent - subsequent calls after the first are no-ops.
#[allow(clippy::print_stderr, reason = "tracing is not initialized yet")]
pub(crate) fn ensure_tracing_initialized() {
    TRACING_INIT.call_once(|| {
        if let Err(error) = initialize_tracing(Some(LogSinkLayer)) {
            eprintln!("failed to initialize tracing: {error:#}");
        }
    });
}

/// Configure the global log sink.
///
/// Call this once at application startup before creating any `ProsodyClient`
/// instances. The log sink receives all tracing events from the Prosody library.
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

    LOG_SINK.store(Arc::new(sink));
}

/// Clear the global log sink (disable logging).
///
/// After calling this function, log events will be silently discarded until
/// a new log sink is configured via `configure_log_sink`.
#[uniffi::export]
pub fn clear_log_sink() {
    LOG_SINK.store(Arc::new(Arc::new(NullLogSink) as Arc<dyn LogSink>));
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
        // Load the current log sink
        let sink = LOG_SINK.load();

        let metadata = event.metadata();
        let level = LogLevel::from(*metadata.level());

        // Check if this level is enabled before doing any formatting work
        if !sink.is_enabled(level) {
            return;
        }

        // Extract the message and fields from the event
        let mut visitor = MessageVisitor::new(metadata);
        event.record(&mut visitor);

        let message = visitor.message.unwrap_or_default();
        let target = metadata.target().to_owned();
        let file = metadata.file().map(ToOwned::to_owned);
        let line = metadata.line();

        // Forward to the C# log sink
        sink.log(level, target, message, file, line);
    }
}

/// A visitor that extracts the message from a tracing event.
struct MessageVisitor {
    message: Option<String>,
}

impl MessageVisitor {
    /// Create a new visitor for the given event metadata.
    fn new(_metadata: &'static Metadata<'static>) -> Self {
        Self { message: None }
    }
}

impl Visit for MessageVisitor {
    fn record_f64(&mut self, _field: &Field, _value: f64) {}

    fn record_i64(&mut self, _field: &Field, _value: i64) {}

    fn record_u64(&mut self, _field: &Field, _value: u64) {}

    fn record_i128(&mut self, _field: &Field, _value: i128) {}

    fn record_u128(&mut self, _field: &Field, _value: u128) {}

    fn record_bool(&mut self, _field: &Field, _value: bool) {}

    fn record_str(&mut self, field: &Field, value: &str) {
        if field.name() == "message" {
            self.message = Some(value.to_owned());
        }
    }

    fn record_error(&mut self, _field: &Field, _value: &(dyn Error + 'static)) {}

    fn record_debug(&mut self, field: &Field, value: &dyn Debug) {
        if field.name() == "message" {
            self.message = Some(format!("{value:?}"));
        }
    }
}
