using NINA.Astrometry;
using NINA.Core.Utility;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Plugin.Speckle.Services;
using NINA.Plugin.Speckle.Workflow;
using NINA.Profile.Interfaces;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace NINA.Plugin.Speckle.Web {

    public static class OperatorPageHost {
        private static readonly object sync = new object();
        private static readonly HashSet<string> owners = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static ISpeckleWorkflowCoordinator coordinator;
        private static ISpeckleOptionsProvider optionsProvider;
        private static MicroHttpServer server;
        private static IReadOnlyList<string> urls = Array.Empty<string>();
        private static long publishedId;
        private static int requestedPort;
        private static bool lanVisible;

        public static event EventHandler Changed;

        public static OperatorState State { get; } = new OperatorState();

        public static bool IsRunning => server?.IsRunning == true;

        public static int Port => server?.Port ?? 0;

        public static IReadOnlyList<string> Urls {
            get { lock (sync) { return urls; } }
        }

        public static string CurrentTargetName {
            get {
                var target = coordinator?.Session?.CurrentTarget;
                return target?.Name ?? string.Empty;
            }
        }

        internal static void Attach(
            IProfileService profileService,
            ITelescopeMediator telescopeMediator,
            ISpeckleWorkflowCoordinator workflowCoordinator,
            ISpeckleOptionsProvider options) {
            lock (sync) {
                if (server != null) {
                    return;
                }
                coordinator = workflowCoordinator;
                optionsProvider = options;
                server = new MicroHttpServer(new OperatorRouter(State, profileService, telescopeMediator).Route);
                State.Changed += OnStateChanged;
                workflowCoordinator.Gate.PendingChanged += OnGatePendingChanged;
            }
            ApplyOptions();
        }

        public static bool Acquire(string owner) {
            lock (sync) {
                if (server == null) {
                    Logger.Warning("Speckle operator page was requested by " + owner + " before the plugin finished loading");
                    return false;
                }
                owners.Add(owner);
                if (!server.IsRunning) {
                    StartLocked();
                }
            }
            RaiseChanged();
            return IsRunning;
        }

        public static void Release(string owner) {
            lock (sync) {
                if (server == null || !owners.Remove(owner) || owners.Count > 0) {
                    return;
                }
                StopLocked();
            }
            RaiseChanged();
        }

        public static void ApplyOptions() {
            var options = optionsProvider?.Current;
            if (options == null) {
                return;
            }
            if (options.EnableOperatorPage) {
                Acquire("options");
            } else {
                Release("options");
            }
            lock (sync) {
                if (server == null || !server.IsRunning) {
                    return;
                }
                if (requestedPort == options.OperatorPagePort && lanVisible == LanWanted(options)) {
                    return;
                }
                StopLocked();
                StartLocked();
            }
            RaiseChanged();
        }

        public static void Stop() {
            lock (sync) {
                owners.Clear();
                StopLocked();
            }
            RaiseChanged();
        }

        private static bool LanWanted(Speckle options) {
            return options.EnableOperatorPage && options.OperatorPageLanVisible;
        }

        private static void StartLocked() {
            var options = optionsProvider?.Current;
            requestedPort = options?.OperatorPagePort ?? 32323;
            lanVisible = options != null && LanWanted(options);
            try {
                server.Start(requestedPort, lanVisible);
                urls = LanAddress.Urls(server.Port, lanVisible);
                Logger.Info("Speckle operator page bound to " + (lanVisible ? "all network adapters" : "loopback only")
                    + " on port " + server.Port.ToString(CultureInfo.InvariantCulture));
            } catch (Exception ex) {
                Logger.Error("Speckle operator page could not be started", ex);
                urls = Array.Empty<string>();
            }
        }

        private static void StopLocked() {
            server?.Stop();
            urls = Array.Empty<string>();
        }

        private static void OnStateChanged(object sender, EventArgs e) {
            RaiseChanged();
        }

        private static void OnGatePendingChanged(object sender, EventArgs e) {
            try {
                var pending = coordinator.Gate.Pending;
                if (pending == null) {
                    var withdrawn = publishedId;
                    publishedId = 0;
                    if (withdrawn != 0) {
                        State.Withdraw(withdrawn);
                    }
                    return;
                }
                if (pending.Kind != GateKind.SlewConfirmation && pending.Kind != GateKind.ImageConfirmation) {
                    return;
                }
                publishedId = State.Publish(new OperatorRequest {
                    Kind = pending.Kind == GateKind.SlewConfirmation ? OperatorRequestKind.SlewConfirmation : OperatorRequestKind.ImageConfirmation,
                    TargetName = NameOf(pending),
                    Target = pending.Coordinates?.Transform(Epoch.JNOW),
                    CanSkip = pending.IsSkippable,
                    StartedUtc = DateTime.UtcNow,
                    Message = pending.Kind == GateKind.SlewConfirmation
                        ? "Slew to target, then press ON TARGET"
                        : "Ready to image, press ON TARGET to start",
                    OnConfirm = coordinator.Confirm,
                    OnSkip = pending.IsSkippable ? coordinator.SkipStep : (Action)null
                });
            } catch (Exception ex) {
                Logger.Error("Speckle operator page could not mirror the workflow gate", ex);
            }
        }

        private static string NameOf(GateRequest pending) {
            if (pending.IsReference && pending.Reference != null) {
                return pending.Reference.Name;
            }
            return pending.Target?.Name ?? string.Empty;
        }

        private static void RaiseChanged() {
            Changed?.Invoke(null, EventArgs.Empty);
        }
    }
}
