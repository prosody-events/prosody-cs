//! The one Tokio runtime of the native library.
//!
//! Every async export runs its body on this runtime through [`run`]. Tasks
//! that core spawns from an export body therefore land here too, including
//! the consumer and timer pipeline that `subscribe` starts. `UniFFI` polls an
//! export on the C# thread that calls it, but that poll only waits for the
//! spawned task.
//!
//! Do not add `async_runtime = "tokio"` to an export. It polls the export
//! through `async-compat`, whose fallback runtime is a second runtime on one
//! 2 MiB thread.

use std::future::Future;
use std::io::{self, Write};
use std::panic::resume_unwind;
use std::process;
use std::sync::LazyLock;

use tokio::runtime::{Builder, Runtime};
use tokio_util::task::AbortOnDropHandle;

/// Name of each runtime worker thread.
const WORKER_NAME: &str = "prosody-worker";

/// Stack size of each Tokio worker thread.
///
/// Core futures are large in debug builds. A timer write that polls through
/// the Cassandra driver overflows the Tokio default of 2 MiB.
const WORKER_STACK_SIZE: usize = 8 * 1024 * 1024;

static RUNTIME: LazyLock<Runtime> = LazyLock::new(|| {
    let runtime = Builder::new_multi_thread()
        .enable_all()
        .thread_name(WORKER_NAME)
        .thread_stack_size(WORKER_STACK_SIZE)
        .build();

    match runtime {
        Ok(runtime) => runtime,
        Err(error) => {
            drop(writeln!(
                io::stderr().lock(),
                "failed to create Tokio runtime: {error:#}"
            ));
            process::abort();
        }
    }
});

/// Runs `future` to completion on a runtime worker thread.
///
/// Dropping the returned future aborts the task, so a call never outlives its
/// caller. A panic in `future` resumes on the polling thread, where `UniFFI`
/// reports it to C#. The task cannot end cancelled: nothing else aborts it,
/// and the static runtime never shuts down.
pub(crate) async fn run<F>(future: F) -> F::Output
where
    F: Future + Send + 'static,
    F::Output: Send + 'static,
{
    match AbortOnDropHandle::new(RUNTIME.spawn(future)).await {
        Ok(output) => output,
        Err(error) => resume_unwind(error.into_panic()),
    }
}

#[cfg(test)]
mod tests;
