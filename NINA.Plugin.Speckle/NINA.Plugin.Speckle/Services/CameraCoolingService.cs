using NINA.Core.Model;
using NINA.Core.Utility;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Profile.Interfaces;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Plugin.Speckle.Services {

    public class CameraCoolingService : IDisposable {
        private static readonly TimeSpan CheckEvery = TimeSpan.FromSeconds(20);
        private static readonly TimeSpan BackOffAfterAFailure = TimeSpan.FromMinutes(10);
        private static readonly TimeSpan ShutdownWait = TimeSpan.FromSeconds(5);
        private const double SetPointTolerance = 0.2;

        private readonly IProfileService profileService;
        private readonly ICameraMediator cameraMediator;
        private readonly IProgress<ApplicationStatus> quietProgress = new Progress<ApplicationStatus>(status => { });
        private readonly SemaphoreSlim nudged = new SemaphoreSlim(0);
        private CancellationTokenSource cts;
        private Task loop;
        private DateTime dontRetryBefore = DateTime.MinValue;
        private string lastCameraKept;

        public CameraCoolingService(IProfileService profileService, ICameraMediator cameraMediator) {
            this.profileService = profileService;
            this.cameraMediator = cameraMediator;
        }

        public void Start() {
            if (loop != null) {
                return;
            }
            cts = new CancellationTokenSource();
            var token = cts.Token;
            loop = Task.Run(() => RunAsync(token), token);
            CameraCoolingRequest.Interrupted += OnInterrupted;
            Logger.Info("Camera cooling will be kept on in the background whenever the connected camera has a cooler.");
        }

        private void OnInterrupted() {
            dontRetryBefore = DateTime.MinValue;
            try {
                nudged.Release();
            } catch (SemaphoreFullException) {
                Logger.Debug("A camera cooling nudge arrived while one was already pending, so this one is dropped.");
            } catch (ObjectDisposedException) {
                Logger.Debug("A camera cooling nudge arrived after the service was disposed, so it is ignored.");
            }
        }

        public void Stop() {
            CameraCoolingRequest.Interrupted -= OnInterrupted;
            cts?.Cancel();
        }

        public void Dispose() {
            Stop();
            var running = loop;
            if (running != null && !running.Wait(ShutdownWait)) {
                Logger.Warning("The camera cooling loop did not stop within " + ShutdownWait.TotalSeconds
                    + " seconds, so its cancellation source is being left for the finalizer.");
                loop = null;
                return;
            }
            loop = null;
            cts?.Dispose();
            cts = null;
            nudged.Dispose();
        }

        private async Task RunAsync(CancellationToken token) {
            while (!token.IsCancellationRequested) {
                try {
                    await KeepItCoolingAsync(token).ConfigureAwait(false);
                } catch (OperationCanceledException) {
                    return;
                } catch (Exception ex) {
                    Logger.Error("Keeping the camera cooling failed", ex);
                    dontRetryBefore = DateTime.UtcNow + BackOffAfterAFailure;
                }
                try {
                    await nudged.WaitAsync(CheckEvery, token).ConfigureAwait(false);
                } catch (OperationCanceledException) {
                    return;
                }
            }
        }

        private async Task KeepItCoolingAsync(CancellationToken token) {
            var camera = cameraMediator.GetInfo();
            if (camera == null || !camera.Connected || !camera.CanSetTemperature) {
                lastCameraKept = null;
                return;
            }
            var wanted = profileService.ActiveProfile?.CameraSettings?.Temperature;
            if (!wanted.HasValue) {
                lastCameraKept = null;
                return;
            }
            var cameraChanged = !string.Equals(lastCameraKept, camera.Name, StringComparison.Ordinal);
            var setPointIsWrong = double.IsNaN(camera.TemperatureSetPoint)
                || Math.Abs(camera.TemperatureSetPoint - wanted.Value) > SetPointTolerance;
            if (camera.CoolerOn && !setPointIsWrong) {
                lastCameraKept = camera.Name;
                return;
            }
            if (DateTime.UtcNow < dontRetryBefore) {
                return;
            }
            Logger.Info("Putting " + camera.Name + " back on its cooling settings: set point " + wanted.Value
                + " degrees, cooler was " + (camera.CoolerOn ? "on at " + camera.TemperatureSetPoint : "off")
                + ", sensor is at " + Math.Round(camera.Temperature, 1) + " degrees"
                + (cameraChanged ? " (a different camera than last time)" : "") + ".");
            lastCameraKept = camera.Name;
            var reached = await cameraMediator.CoolCamera(wanted.Value, TimeSpan.Zero, quietProgress, token).ConfigureAwait(false);
            if (!reached) {
                dontRetryBefore = DateTime.UtcNow + BackOffAfterAFailure;
                Logger.Warning(camera.Name + " did not reach " + wanted.Value + " degrees. The cooler stays on and this will be tried again in "
                    + BackOffAfterAFailure.TotalMinutes + " minutes.");
            } else {
                Logger.Info(camera.Name + " is cooling at " + wanted.Value + " degrees.");
            }
        }
    }
}
