using NINA.Astrometry;
using NINA.Core.Utility;
using NINA.Core.Utility.Notification;
using NINA.Plugin.Speckle.Model;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Plugin.Speckle.Workflow {

    public enum GateOutcome {
        Confirm,
        Retry,
        Skip,
        SkipTarget
    }

    public enum GateKind {
        SlewConfirmation,
        ImageConfirmation,
        Error
    }

    public class GateRequest {
        public GateKind Kind { get; init; }
        public WorkflowPhase Phase { get; init; }
        public string Title { get; init; }
        public string Detail { get; init; }
        public SpeckleTarget Target { get; init; }
        public ReferenceStar Reference { get; init; }
        public Coordinates Coordinates { get; init; }
        public bool IsReference { get; init; }
        public bool IsSkippable { get; init; }
        public int Attempt { get; init; }
        public Exception Error { get; init; }
    }

    public sealed class WorkflowGate {
        private TaskCompletionSource<GateOutcome> pendingSource;

        public GateRequest Pending { get; private set; }

        public event EventHandler PendingChanged;

        public async Task<GateOutcome> WaitAsync(GateRequest request, bool fullAuto, CancellationToken ct) {
            ct.ThrowIfCancellationRequested();
            if (fullAuto) {
                return ResolveAutomatically(request);
            }
            var source = new TaskCompletionSource<GateOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
            pendingSource = source;
            Pending = request;
            PendingChanged?.Invoke(this, EventArgs.Empty);
            using (ct.Register(() => source.TrySetCanceled(ct))) {
                try {
                    return await source.Task.ConfigureAwait(false);
                } finally {
                    pendingSource = null;
                    Pending = null;
                    PendingChanged?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        private static GateOutcome ResolveAutomatically(GateRequest request) {
            if (request.Kind != GateKind.Error) {
                return GateOutcome.Confirm;
            }
            var outcome = request.Attempt < 1 ? GateOutcome.Retry
                : request.IsSkippable ? GateOutcome.Skip
                : GateOutcome.SkipTarget;
            var subject = string.IsNullOrWhiteSpace(request.Target?.Name) ? "this target" : request.Target.Name;
            var action = outcome == GateOutcome.Retry ? "trying again"
                : outcome == GateOutcome.Skip ? "carrying on without it"
                : "moving to the next target";
            var reason = request.Error != null && !string.IsNullOrWhiteSpace(request.Error.Message)
                ? " (" + request.Error.Message + ")"
                : "";
            Logger.Warning("Automatic run: " + request.Title + " for " + subject + " -> " + outcome + reason);
            Notification.ShowWarning(request.Title + " for " + subject + ". The run is " + action + ".");
            return outcome;
        }

        public void Resolve(GateOutcome outcome) {
            pendingSource?.TrySetResult(outcome);
        }

        public void Confirm() {
            Resolve(GateOutcome.Confirm);
        }

        public void Retry() {
            Resolve(GateOutcome.Retry);
        }

        public void Skip() {
            Resolve(GateOutcome.Skip);
        }

        public void SkipTarget() {
            Resolve(GateOutcome.SkipTarget);
        }
    }
}
