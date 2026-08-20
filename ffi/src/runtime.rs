//! Tokio runtime access for futures polled by `BoltFFI`.

use std::future::Future;
use std::pin::Pin;
use std::process::abort;
use std::sync::LazyLock;
use std::task::{Context, Poll};

use pin_project_lite::pin_project;
use tokio::runtime::{Builder, Runtime};

static RUNTIME: LazyLock<Runtime> =
    LazyLock::new(|| match Builder::new_multi_thread().enable_all().build() {
        Ok(runtime) => runtime,
        Err(error) => {
            tracing::error!(%error, "failed to create the Prosody runtime");
            abort();
        }
    });

pin_project! {
    /// A future that enters the Prosody runtime for each poll.
    pub(crate) struct Entered<F> {
        #[pin]
        future: F,
    }
}

impl<F: Future> Future for Entered<F> {
    type Output = F::Output;

    fn poll(self: Pin<&mut Self>, context: &mut Context<'_>) -> Poll<Self::Output> {
        let _guard = RUNTIME.enter();
        self.project().future.poll(context)
    }
}

pub(crate) fn enter<F: Future>(future: F) -> Entered<F> {
    Entered { future }
}
