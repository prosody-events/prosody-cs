//! Tokio runtime that drives the background consumer and timer pipeline.
//!
//! `UniFFI` polls exported async functions from whatever C# thread calls into
//! the FFI boundary, using that thread's own native stack. Once
//! [`ProsodyClient::subscribe`](crate::client::ProsodyClient::subscribe)
//! starts the consumer, though, message and timer dispatch keep running as
//! background Tokio tasks for the client's whole lifetime. This module gives
//! those tasks a runtime sized for the deep await chains in the consumer
//! pipeline, instead of leaving them on a foreign thread's default stack.

use std::io;
use std::sync::LazyLock;

use tokio::runtime::{Builder, Runtime};

/// Stack size of each Tokio worker thread.
///
/// Core futures are large in debug builds. A timer write that polls through
/// the Cassandra driver overflows the Tokio default of 2 MiB.
const WORKER_STACK_SIZE: usize = 8 * 1024 * 1024;

static RUNTIME: LazyLock<io::Result<Runtime>> = LazyLock::new(|| {
    Builder::new_multi_thread()
        .enable_all()
        .thread_stack_size(WORKER_STACK_SIZE)
        .build()
});

/// Returns the runtime that drives the consumer and timer pipeline.
///
/// # Errors
///
/// Returns the error the OS gave when Tokio failed to create the runtime's
/// worker threads.
pub(crate) fn consumer() -> io::Result<&'static Runtime> {
    RUNTIME
        .as_ref()
        .map_err(|error| io::Error::new(error.kind(), error.to_string()))
}
