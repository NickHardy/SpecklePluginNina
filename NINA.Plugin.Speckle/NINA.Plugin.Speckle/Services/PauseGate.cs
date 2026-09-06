using System;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Plugin.Speckle.Services {

    public sealed class PauseGate {
        private TaskCompletionSource<bool> pauseSource;

        public bool IsPauseRequested => Volatile.Read(ref pauseSource) != null;

        public event EventHandler Engaged;

        public void RequestPause() {
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Interlocked.CompareExchange(ref pauseSource, tcs, null);
        }

        public void Resume() {
            Interlocked.Exchange(ref pauseSource, null)?.TrySetResult(true);
        }

        public async Task WaitWhilePausedAsync(CancellationToken ct) {
            var tcs = Volatile.Read(ref pauseSource);
            if (tcs == null) {
                return;
            }
            Engaged?.Invoke(this, EventArgs.Empty);
            var cancellation = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using (ct.Register(() => cancellation.TrySetResult(true))) {
                await Task.WhenAny(tcs.Task, cancellation.Task).ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();
            }
        }
    }
}
