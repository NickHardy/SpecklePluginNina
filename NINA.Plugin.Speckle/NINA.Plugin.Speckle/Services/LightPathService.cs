using NINA.Core.Model;
using NINA.Core.Utility;
using NINA.Core.Utility.Notification;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Profile.Interfaces;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Plugin.Speckle.Services {

    [Export(typeof(ILightPathService))]
    [PartCreationPolicy(CreationPolicy.Shared)]
    public class LightPathService : ILightPathService {
        private readonly IProfileService profileService;
        private readonly ICameraMediator cameraMediator;
        private readonly IFilterWheelMediator filterWheelMediator;
        private readonly IFocuserMediator focuserMediator;
        private readonly ISpeckleOptionsProvider optionsProvider;

        [ImportingConstructor]
        public LightPathService(IProfileService profileService, ICameraMediator cameraMediator, IFilterWheelMediator filterWheelMediator, IFocuserMediator focuserMediator, ISpeckleOptionsProvider optionsProvider) {
            this.profileService = profileService;
            this.cameraMediator = cameraMediator;
            this.filterWheelMediator = filterWheelMediator;
            this.focuserMediator = focuserMediator;
            this.optionsProvider = optionsProvider;
        }

        public LightPath? CurrentPath { get; private set; }

        public async Task SwitchAsync(LightPath path, IProgress<ApplicationStatus> progress, CancellationToken ct) {
            var rig = TwoCameraSetup.Read(optionsProvider.Current);
            var automaticallyRun = optionsProvider.Current.AutomaticallyRun;
            var profileId = path == LightPath.Wide ? rig.WideProfileId : rig.ScienceProfileId;
            var mirrorPosition = path == LightPath.Wide ? rig.WideMirrorPosition : rig.ScienceMirrorPosition;
            var cameraName = path == LightPath.Wide ? rig.WideCameraName : rig.ScienceCameraName;

            if (!string.IsNullOrWhiteSpace(profileId) && !string.Equals(profileService.ActiveProfile.Id.ToString(), profileId, StringComparison.OrdinalIgnoreCase)) {
                var profileMeta = profileService.Profiles.FirstOrDefault(candidate => string.Equals(candidate.Id.ToString(), profileId, StringComparison.OrdinalIgnoreCase))
                    ?? throw new InvalidOperationException("Profile " + profileId + " for the " + path + " light path was not found");
                profileService.ActiveProfile.Save();
                await CloseCameraBeforeProfileChangeAsync(progress).ConfigureAwait(false);
                progress?.Report(new ApplicationStatus { Status = "Switching to profile " + profileMeta.Name });
                Logger.Info("Light path " + path + ": switching to profile " + profileMeta.Name);
                if (!profileService.SelectProfile(profileMeta)) {
                    throw new InvalidOperationException("Could not switch to profile " + profileMeta.Name);
                }
                rig.WriteTo(optionsProvider.Current, profileMeta.Name);
                if (optionsProvider.Current.AutomaticallyRun != automaticallyRun) {
                    optionsProvider.Current.AutomaticallyRun = automaticallyRun;
                    Logger.Info("Run automatically was carried into profile " + profileMeta.Name
                        + " so the light path switch does not change how the night is driven");
                }
                await ForceCameraAsync(path, rig, ct).ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();
                await ConnectAsync("camera", () => cameraMediator.Connect(), progress, ct).ConfigureAwait(false);
                await ConnectAsync("filter wheel", () => filterWheelMediator.Connect(), progress, ct).ConfigureAwait(false);
                await ConnectAsync("focuser", () => focuserMediator.Connect(), progress, ct).ConfigureAwait(false);
                CameraCoolingRequest.Raise();
            }

            LearnCameraId(path, cameraName);
            RememberCameraForPath(path);
            CheckCameraForPath(path, cameraName);
            ct.ThrowIfCancellationRequested();
            var mirrorIsConfigured = rig.WideMirrorPosition != rig.ScienceMirrorPosition;
            var focuserIsConnected = focuserMediator.GetInfo()?.Connected == true;
            if (mirrorIsConfigured && !focuserIsConnected) {
                throw new Exception("The flip mirror focuser is not connected, so the mirror cannot be moved to the "
                    + (path == LightPath.Wide ? "wide field" : "science") + " port. Connect the focuser before imaging, "
                    + "otherwise every frame would be taken through whichever port the mirror was last left in.");
            }
            if (mirrorIsConfigured) {
                progress?.Report(new ApplicationStatus { Status = "Moving flip mirror to " + mirrorPosition });
                Logger.Info("Light path " + path + ": moving flip mirror to " + mirrorPosition);
                await focuserMediator.MoveFocuser(mirrorPosition, ct).ConfigureAwait(false);
                CheckMirrorArrived(path, mirrorPosition);
            } else {
                Logger.Info("Light path " + path + ": both mirror positions are the same, so there is no flip mirror to move");
            }
            CurrentPath = path;
        }

        private string wideCameraName;
        private string scienceCameraName;
        private bool sameCameraReported;

        private async Task ForceCameraAsync(LightPath path, TwoCameraSetup rig, CancellationToken ct) {
            var wantedId = rig.CameraId(path);
            var wantedName = path == LightPath.Wide ? rig.WideCameraName : rig.ScienceCameraName;
            if (string.IsNullOrWhiteSpace(wantedId) && string.IsNullOrWhiteSpace(wantedName)) {
                return;
            }
            if (string.IsNullOrWhiteSpace(wantedId)) {
                wantedId = await FindCameraByNameAsync(wantedName, ct).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(wantedId)) {
                    Logger.Warning("The " + path + " light path wants camera " + wantedName
                        + " but no device of that name was found, so the profile's own choice is used");
                    return;
                }
                rig.LearnedCameraId(path, wantedId);
                StoreCameraId(path, wantedId);
            }
            var settings = profileService.ActiveProfile.CameraSettings;
            Logger.Info("Light path " + path + ": the profile selects camera " + settings.Id + ", choosing "
                + (string.IsNullOrWhiteSpace(wantedName) ? wantedId : wantedName) + " instead");
            settings.Id = wantedId;
            if (!string.IsNullOrWhiteSpace(wantedName)) {
                settings.LastDeviceName = wantedName;
            }
            await RescanCamerasAsync(ct).ConfigureAwait(false);
        }

        private async Task<IList<string>> RescanCamerasAsync(CancellationToken ct) {
            ct.ThrowIfCancellationRequested();
            var rescan = cameraMediator.Rescan();
            if (rescan == null) {
                return new List<string>();
            }
            var devices = await rescan.ConfigureAwait(false) ?? new List<string>();
            return devices;
        }

        private void StoreCameraId(LightPath path, string id) {
            var options = optionsProvider.Current;
            if (path == LightPath.Wide) {
                options.WideCameraId = id;
            } else {
                options.ScienceCameraId = id;
            }
        }

        private void CheckCameraForPath(LightPath path, string wantedName) {
            if (string.IsNullOrWhiteSpace(wantedName)) {
                return;
            }
            var camera = cameraMediator.GetInfo();
            var connected = camera != null && camera.Connected;
            if (connected && (CameraNameMatches(camera.Name, wantedName) || CameraNameMatches(camera.DisplayName, wantedName))) {
                return;
            }
            var actual = connected ? camera.Name : "nothing";
            throw new Exception("The " + (path == LightPath.Wide ? "wide field" : "science") + " light path should use "
                + wantedName + " but " + actual + " is connected. Nothing will be imaged until that matches.");
        }

        private const int MirrorTolerance = 5;

        private void CheckMirrorArrived(LightPath path, int wanted) {
            var focuser = focuserMediator.GetInfo();
            if (focuser == null || !focuser.Connected) {
                return;
            }
            var reached = focuser.Position;
            if (Math.Abs(reached - wanted) <= MirrorTolerance) {
                Logger.Info("Light path " + path + ": the flip mirror reached " + reached);
                return;
            }
            throw new Exception("The flip mirror was sent to " + wanted + " for the " + path
                + " light path but the focuser reports " + reached + ". Nothing will be imaged through the wrong path.");
        }

        private async Task CloseCameraBeforeProfileChangeAsync(IProgress<ApplicationStatus> progress) {
            var camera = cameraMediator.GetInfo();
            if (camera == null || !camera.Connected) {
                return;
            }
            var watch = Stopwatch.StartNew();
            progress?.Report(new ApplicationStatus { Status = "Closing " + camera.Name });
            Logger.Info("Light path: closing " + camera.Name + " before the profile changes, so the camera driver is not"
                + " enumerated while a camera of the same make is still open");
            try {
                await cameraMediator.Disconnect().ConfigureAwait(false);
                Logger.Info("Light path: " + camera.Name + " closed in " + watch.ElapsedMilliseconds + " ms");
            } catch (Exception ex) {
                Logger.Warning("Light path: " + camera.Name + " did not close cleanly, carrying on with the switch. " + ex.Message);
            }
        }

        private async Task<string> FindCameraByNameAsync(string wantedName, CancellationToken ct) {
            try {
                var devices = await RescanCamerasAsync(ct).ConfigureAwait(false);
                var wanted = Squash(wantedName);
                var match = devices.FirstOrDefault(id => Squash(id).Contains(wanted));
                if (match == null) {
                    Logger.Warning("No camera matching " + wantedName + " was found. Cameras seen: " + string.Join(", ", devices));
                    return null;
                }
                Logger.Info("Camera " + wantedName + " matched device " + match);
                return match;
            } catch (OperationCanceledException) {
                throw;
            } catch (Exception ex) {
                Logger.Error("Could not look up the camera named " + wantedName, ex);
                return null;
            }
        }

        private static bool CameraNameMatches(string reportedName, string wantedName) {
            var wanted = Squash(wantedName);
            return wanted.Length > 0 && Squash(reportedName).Contains(wanted);
        }

        private static string Squash(string value) {
            return new string((value ?? string.Empty).Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
        }

        private void LearnCameraId(LightPath path, string wantedName) {
            if (string.IsNullOrWhiteSpace(wantedName)) {
                return;
            }
            var camera = cameraMediator.GetInfo();
            if (camera == null || !camera.Connected || !string.Equals(camera.Name, wantedName, StringComparison.Ordinal)) {
                return;
            }
            var id = profileService.ActiveProfile.CameraSettings.Id;
            var options = optionsProvider.Current;
            var known = path == LightPath.Wide ? options.WideCameraId : options.ScienceCameraId;
            if (string.Equals(known, id, StringComparison.Ordinal)) {
                return;
            }
            StoreCameraId(path, id);
            Logger.Info(wantedName + " is device " + id + "; the " + path + " light path will select it from now on");
        }

        private void RememberCameraForPath(LightPath path) {
            var camera = cameraMediator.GetInfo();
            if (camera == null || !camera.Connected) {
                return;
            }
            if (path == LightPath.Wide) {
                wideCameraName = camera.Name;
            } else {
                scienceCameraName = camera.Name;
            }
            Logger.Info("Light path " + path + " is using camera " + camera.Name);
            if (sameCameraReported || wideCameraName == null || scienceCameraName == null) {
                return;
            }
            if (!string.Equals(wideCameraName, scienceCameraName, StringComparison.Ordinal)) {
                return;
            }
            sameCameraReported = true;
            Logger.Warning("Both light paths are on the same camera, " + wideCameraName
                + ", so the science camera is never used. Check which camera each profile selects.");
            Notification.ShowWarning("Both light paths use " + wideCameraName + ". Check the camera chosen in each profile.");
        }

        private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(90);

        private static async Task ConnectAsync(string device, Func<Task<bool>> connect, IProgress<ApplicationStatus> progress, CancellationToken ct) {
            var watch = Stopwatch.StartNew();
            progress?.Report(new ApplicationStatus { Status = "Connecting the " + device });
            Logger.Info("Light path: connecting the " + device);
            var connecting = connect();
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct)) {
                timeout.CancelAfter(ConnectTimeout);
                var finished = await Task.WhenAny(connecting, Task.Delay(Timeout.Infinite, timeout.Token)).ConfigureAwait(false);
                if (finished != connecting) {
                    ct.ThrowIfCancellationRequested();
                    throw new Exception("The " + device + " did not connect within " + ConnectTimeout.TotalSeconds
                        + " seconds of the profile switch. Connect it by hand and try this step again.");
                }
            }
            var connected = await connecting.ConfigureAwait(false);
            Logger.Info("Light path: the " + device + " reported connected " + connected + " after " + watch.ElapsedMilliseconds + " ms");
            if (!connected) {
                throw new Exception("The " + device + " did not connect after the profile switch.");
            }
        }

        private sealed class TwoCameraSetup {

            public bool DualCameraSetup { get; private set; }
            public string WideProfileId { get; private set; }
            public string ScienceProfileId { get; private set; }
            public int WideMirrorPosition { get; private set; }
            public int ScienceMirrorPosition { get; private set; }
            public string WideCameraName { get; private set; }
            public string ScienceCameraName { get; private set; }
            public string WideCameraId { get; private set; }
            public string ScienceCameraId { get; private set; }
            public string SlewFilter { get; private set; }
            public double WideCompassAngle { get; private set; }
            public double ScienceCompassAngle { get; private set; }
            public bool WideCompassMirrored { get; private set; }
            public bool ScienceCompassMirrored { get; private set; }

            public static TwoCameraSetup Read(Speckle options) {
                return new TwoCameraSetup {
                    DualCameraSetup = options.DualCameraSetup,
                    WideProfileId = options.WideProfileId,
                    ScienceProfileId = options.ScienceProfileId,
                    WideMirrorPosition = options.WideMirrorPosition,
                    ScienceMirrorPosition = options.ScienceMirrorPosition,
                    WideCameraName = options.WideCameraName,
                    ScienceCameraName = options.ScienceCameraName,
                    WideCameraId = options.WideCameraId,
                    ScienceCameraId = options.ScienceCameraId,
                    SlewFilter = options.SlewFilter,
                    WideCompassAngle = options.WideCompassAngle,
                    ScienceCompassAngle = options.ScienceCompassAngle,
                    WideCompassMirrored = options.WideCompassMirrored,
                    ScienceCompassMirrored = options.ScienceCompassMirrored
                };
            }

            public void LearnedCameraId(LightPath path, string id) {
                if (path == LightPath.Wide) {
                    WideCameraId = id;
                } else {
                    ScienceCameraId = id;
                }
            }

            public string CameraId(LightPath path) {
                return path == LightPath.Wide ? WideCameraId : ScienceCameraId;
            }

            public void WriteTo(Speckle options, string profileName) {
                if (!DualCameraSetup) {
                    return;
                }
                options.DualCameraSetup = DualCameraSetup;
                options.WideProfileId = WideProfileId;
                options.ScienceProfileId = ScienceProfileId;
                options.WideMirrorPosition = WideMirrorPosition;
                options.ScienceMirrorPosition = ScienceMirrorPosition;
                options.WideCameraName = WideCameraName;
                options.ScienceCameraName = ScienceCameraName;
                options.WideCameraId = WideCameraId;
                options.ScienceCameraId = ScienceCameraId;
                options.SlewFilter = SlewFilter;
                options.WideCompassAngle = WideCompassAngle;
                options.ScienceCompassAngle = ScienceCompassAngle;
                options.WideCompassMirrored = WideCompassMirrored;
                options.ScienceCompassMirrored = ScienceCompassMirrored;
                Logger.Info("The two camera setup was copied into profile " + profileName
                    + ", so the cameras, slew filter and compass are the same whichever profile a run starts from");
            }
        }
    }
}
