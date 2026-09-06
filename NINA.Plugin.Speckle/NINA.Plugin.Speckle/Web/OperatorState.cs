using NINA.Astrometry;
using NINA.Core.Utility;
using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Plugin.Speckle.Web {

    public enum OperatorRequestKind {
        MountSlew,
        SlewConfirmation,
        ImageConfirmation
    }

    public sealed class OperatorRequest {
        public long Id { get; internal set; }
        public OperatorRequestKind Kind { get; init; }
        public string TargetName { get; init; }
        public Coordinates Target { get; init; }
        public bool CanSkip { get; init; }
        public DateTime StartedUtc { get; init; }
        public string Message { get; init; }
        public Action OnConfirm { get; init; }
        public Action OnSkip { get; init; }
    }

    public sealed class OperatorState {
        private readonly object gate = new object();
        private long nextId;
        private OperatorRequest pending;
        private TaskCompletionSource<bool> pendingSource;
        private Coordinates reported;
        private bool manualMountConnected;

        public event EventHandler Changed;

        public OperatorRequest Pending {
            get { lock (gate) { return pending; } }
        }

        public bool IsWaiting => Pending != null;

        public Coordinates Reported {
            get { lock (gate) { return reported; } }
            set { lock (gate) { reported = value; } }
        }

        public bool ManualMountConnected {
            get { lock (gate) { return manualMountConnected; } }
            set { lock (gate) { manualMountConnected = value; } }
        }

        public async Task<bool> WaitAsync(OperatorRequest request, CancellationToken token) {
            token.ThrowIfCancellationRequested();
            var source = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var id = Interlocked.Increment(ref nextId);
            request.Id = id;
            TaskCompletionSource<bool> displaced;
            lock (gate) {
                displaced = Displace(request);
                pendingSource = source;
            }
            displaced?.TrySetResult(false);
            RaiseChanged();
            try {
                using (token.Register(() => source.TrySetCanceled(token))) {
                    return await source.Task.ConfigureAwait(false);
                }
            } finally {
                Withdraw(id);
            }
        }

        public long Publish(OperatorRequest request) {
            var id = Interlocked.Increment(ref nextId);
            request.Id = id;
            TaskCompletionSource<bool> displaced;
            lock (gate) {
                displaced = Displace(request);
                pendingSource = null;
            }
            displaced?.TrySetResult(false);
            RaiseChanged();
            return id;
        }

        public void Withdraw(long id) {
            lock (gate) {
                if (pending == null || pending.Id != id) {
                    return;
                }
                pending = null;
                pendingSource = null;
            }
            RaiseChanged();
        }

        public bool Confirm(long id) {
            return Resolve(id, true);
        }

        public bool Skip(long id) {
            return Resolve(id, false);
        }

        public void Abort(OperatorRequestKind kind) {
            TaskCompletionSource<bool> source;
            lock (gate) {
                if (pending == null || pending.Kind != kind) {
                    return;
                }
                source = pendingSource;
                pending = null;
                pendingSource = null;
            }
            source?.TrySetResult(false);
            RaiseChanged();
        }

        private TaskCompletionSource<bool> Displace(OperatorRequest request) {
            var previous = pendingSource;
            if (pending != null) {
                Logger.Warning("Operator request " + pending.Id.ToString(CultureInfo.InvariantCulture)
                    + " displaced by " + request.Id.ToString(CultureInfo.InvariantCulture));
            }
            pending = request;
            return previous;
        }

        private bool Resolve(long id, bool confirmed) {
            OperatorRequest request;
            TaskCompletionSource<bool> source;
            lock (gate) {
                if (pending == null || pending.Id != id) {
                    Logger.Warning("Ignoring stale operator " + (confirmed ? "confirm" : "skip") + " for "
                        + id.ToString(CultureInfo.InvariantCulture) + ", active is "
                        + (pending == null ? "none" : pending.Id.ToString(CultureInfo.InvariantCulture)));
                    return false;
                }
                if (!confirmed && !pending.CanSkip) {
                    return false;
                }
                request = pending;
                source = pendingSource;
                pending = null;
                pendingSource = null;
            }
            RaiseChanged();
            var handler = confirmed ? request.OnConfirm : request.OnSkip;
            if (handler != null) {
                handler();
                return true;
            }
            return source != null && source.TrySetResult(confirmed);
        }

        private void RaiseChanged() {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
