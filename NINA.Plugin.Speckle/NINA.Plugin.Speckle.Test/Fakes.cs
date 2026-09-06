using NINA.Astrometry;
using NINA.Core.Enum;
using NINA.Core.Model;
using NINA.Core.Model.Equipment;
using NINA.Equipment.Equipment.MyCamera;
using NINA.Equipment.Equipment.MyFilterWheel;
using NINA.Equipment.Equipment.MyTelescope;
using NINA.Equipment.Interfaces;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Equipment.Interfaces.ViewModel;
using NINA.Equipment.Model;
using NINA.Image.Interfaces;
using NINA.PlateSolving;
using NINA.Plugin.Speckle.Model;
using NINA.Plugin.Speckle.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Plugin.Speckle.Test {

    public class Recorder {
        private readonly List<string> entries = new List<string>();

        public void Add(string entry) {
            lock (entries) {
                entries.Add(entry);
            }
        }

        public IReadOnlyList<string> Entries {
            get {
                lock (entries) {
                    return entries.ToList();
                }
            }
        }

        public int Count(string prefix) {
            return Entries.Count(entry => entry.StartsWith(prefix, StringComparison.Ordinal));
        }

        public int IndexOf(string entry, int occurrence = 1) {
            var seen = 0;
            var snapshot = Entries;
            for (var i = 0; i < snapshot.Count; i++) {
                if (snapshot[i] == entry) {
                    seen++;
                    if (seen == occurrence) {
                        return i;
                    }
                }
            }
            return -1;
        }
    }

    public class FakeOptionsProvider : ISpeckleOptionsProvider {
        public Speckle Current => null;
    }

    public class FakeTargetListService : ITargetListService {
        private readonly Recorder recorder;

        public FakeTargetListService(Recorder recorder) {
            this.recorder = recorder;
        }

        public IReadOnlyList<SpeckleTarget> TargetsToLoad { get; set; } = new List<SpeckleTarget>();

        public int SnapshotCount;

        public Func<Task> OnSaveSnapshot { get; set; }

        public NighttimeData GetNighttimeData(DateTime now) {
            return null;
        }

        public Task<IReadOnlyList<SpeckleTarget>> LoadTargetsAsync(string csvPath, TargetListDefaults defaults, bool ignoreLimits, CancellationToken ct) {
            recorder.Add("load:" + csvPath);
            foreach (var target in TargetsToLoad) {
                target.SourceList = csvPath;
            }
            return Task.FromResult(TargetsToLoad);
        }

        public IReadOnlyList<SpeckleTarget> Order(IEnumerable<SpeckleTarget> targets) {
            return targets.ToList();
        }

        public List<AltTime> ComputeAltList(Coordinates coords, ObserverInfo observer, CustomHorizon horizon, double stepHours) {
            return new List<AltTime>();
        }

        public void ApplyVisibilityWindows(SpeckleTarget target, NighttimeData night, SchedulingOptions opts) {
        }

        public async Task SaveSnapshotAsync(IEnumerable<SpeckleTarget> targets, string directory) {
            if (OnSaveSnapshot != null) {
                await OnSaveSnapshot();
            }
            Interlocked.Increment(ref SnapshotCount);
            recorder.Add("snapshot");
        }

        public List<string> WrittenPaths { get; } = new List<string>();

        public void WriteTargets(IEnumerable<SpeckleTarget> targets, string csvPath) {
            WrittenPaths.Add(csvPath);
            recorder.Add("write:" + csvPath);
        }

        public Task<ReferenceStarExportResult> ExportWithReferenceStarsAsync(IReadOnlyList<SpeckleTarget> targets, ExportFormat format, string directory, IProgress<ApplicationStatus> progress, CancellationToken ct) {
            return Task.FromResult(new ReferenceStarExportResult());
        }
    }

    public class FakeSchedulerService : ITargetSchedulerService {
        private readonly Recorder recorder;

        public FakeSchedulerService(Recorder recorder) {
            this.recorder = recorder;
        }

        public Queue<SpeckleTarget> Picks { get; } = new Queue<SpeckleTarget>();

        public int PickCount;

        public SpeckleTarget PickNextTarget(IReadOnlyList<SpeckleTarget> targets, SchedulingOptions opts, double? currentRa, DateTime now) {
            Interlocked.Increment(ref PickCount);
            recorder.Add("pick");
            return Picks.Count > 0 ? Picks.Dequeue() : null;
        }

        public void MarkCycleComplete(SpeckleTarget target, DateTime imagedAt) {
            recorder.Add("mark:" + target.Name1);
            target.ImagedAt = imagedAt;
            target.Completed_cycles += 1;
            if (target.Completed_cycles == target.Cycles) {
                target.Completed_nights += 1;
            }
        }

        public void PromoteTarget(SpeckleTarget target) {
        }
    }

    public class FakeReferenceStarService : IReferenceStarService {
        private readonly Recorder recorder;

        public FakeReferenceStarService(Recorder recorder) {
            this.recorder = recorder;
            OnResolve = target => new ReferenceStarResult {
                Chosen = new ReferenceStar { Name2 = "REF_" + target.Name1, RA2000 = 33, Dec2000 = 44 }
            };
        }

        public Func<SpeckleTarget, ReferenceStarResult> OnResolve { get; set; }

        public IReadOnlyList<ReferenceStar> ReferenceStars => new List<ReferenceStar>();

        public IReadOnlyList<GaiaReferenceStar> GaiaReferenceStars => new List<GaiaReferenceStar>();

        public Task EnsureReferenceListsLoadedAsync(CancellationToken ct) {
            return Task.CompletedTask;
        }

        public Task<ReferenceStarResult> ResolveAsync(SpeckleTarget target, IReadOnlyList<SpeckleTarget> allTargets, ReferenceStarQueryOptions opts, IProgress<ApplicationStatus> progress, CancellationToken ct) {
            recorder.Add("resolve:" + target.Name1);
            return Task.FromResult(OnResolve(target));
        }
    }

    public class FakeRoiPositioningService : IRoiPositioningService {
        private readonly Recorder recorder;

        public FakeRoiPositioningService(Recorder recorder) {
            this.recorder = recorder;
            OnLocate = request => new RoiPositionResult {
                RoiX = 100,
                RoiY = 120,
                Orientation = 1.5,
                ArcsecPerPix = 0.2,
                PlatesolveSucceeded = true
            };
        }

        public Func<RoiPositionRequest, RoiPositionResult> OnLocate { get; set; }

        public int CallCount;

        public Task<RoiPositionResult> LocateTargetAsync(RoiPositionRequest request, IProgress<ApplicationStatus> progress, CancellationToken ct) {
            Interlocked.Increment(ref CallCount);
            recorder.Add("roi:" + request.SpeckleTarget?.Name1);
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(OnLocate(request));
        }
    }

    public class FakeExposureCalibrationService : IExposureCalibrationService {
        private readonly Recorder recorder;

        public FakeExposureCalibrationService(Recorder recorder) {
            this.recorder = recorder;
            OnCalibrate = request => 0.05;
        }

        public Func<ExposureCalibrationRequest, double> OnCalibrate { get; set; }

        public int CallCount;

        public Task<double> FindRoiExposureTimeAsync(ExposureCalibrationRequest request, IProgress<ApplicationStatus> progress, CancellationToken ct) {
            Interlocked.Increment(ref CallCount);
            recorder.Add("calibrate");
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(OnCalibrate(request));
        }

        public double EstimateExposureTime(ExposureEstimateRequest request) {
            return 0.05;
        }
    }

    public class FakeAcquisitionService : ISpeckleAcquisitionService {
        private readonly Recorder recorder;
        private readonly List<RoiSeriesRequest> requests = new List<RoiSeriesRequest>();

        public Action OnEnsureFramesCanBeSaved { get; set; }

        public void EnsureFramesCanBeSaved() {
            recorder.Add("save:check");
            OnEnsureFramesCanBeSaved?.Invoke();
        }

        public FakeAcquisitionService(Recorder recorder) {
            this.recorder = recorder;
            OnRunRoiSeries = (request, ct) => Task.FromResult(new ExposureRunResult {
                FramesCaptured = request.TotalExposureCount,
                Fps = 10,
                PendingSaves = Task.CompletedTask
            });
        }

        public Func<RoiSeriesRequest, CancellationToken, Task<ExposureRunResult>> OnRunRoiSeries { get; set; }

        public IReadOnlyList<RoiSeriesRequest> Requests {
            get {
                lock (requests) {
                    return requests.ToList();
                }
            }
        }

        public async Task<ExposureRunResult> RunRoiSeriesAsync(RoiSeriesRequest request, IProgress<ApplicationStatus> progress, CancellationToken ct) {
            lock (requests) {
                requests.Add(request);
            }
            recorder.Add("video:start");
            var result = await OnRunRoiSeries(request, ct);
            recorder.Add("video:done");
            return result;
        }

        public Task TakeSingleAsync(SingleExposureRequest request, IProgress<ApplicationStatus> progress, CancellationToken ct) {
            return Task.CompletedTask;
        }

        public List<double> PreviewExposureTimes { get; } = new List<double>();

        public async Task RunPreviewLoopAsync(PreviewLoopRequest request, IProgress<ApplicationStatus> progress, CancellationToken ct) {
            PreviewExposureTimes.Add(request.GetExposureTime());
            recorder.Add("preview:start");
            try {
                await Task.Delay(Timeout.Infinite, ct);
            } finally {
                recorder.Add("preview:stop");
            }
        }
    }

    public class FakeTemplateValidationService : ITemplateValidationService {

        public int FilledFromTemplates { get; set; }

        public int FillFiltersFromTemplates(IEnumerable<SpeckleTarget> targets) {
            return FilledFromTemplates;
        }

        private readonly Recorder recorder;

        public FakeTemplateValidationService(Recorder recorder) {
            this.recorder = recorder;
        }

        public Func<IEnumerable<SpeckleTarget>, IReadOnlyList<TemplateIssue>> OnValidate { get; set; }

        public Func<SpeckleTarget, TargetPlan, TemplateIssue> OnInspect { get; set; }

        public IReadOnlyList<TemplateIssue> Validate(IEnumerable<SpeckleTarget> targets, TargetPlanDefaults defaults) {
            recorder.Add("templates:validate");
            return OnValidate != null ? OnValidate(targets) : new List<TemplateIssue>();
        }

        public TemplateIssue Inspect(SpeckleTarget target, TargetPlan plan) {
            recorder.Add("templates:inspect:" + target?.Name1);
            return OnInspect?.Invoke(target, plan);
        }
    }

    public class FakeLightPathService : ILightPathService {
        private readonly Recorder recorder;

        public FakeLightPathService(Recorder recorder) {
            this.recorder = recorder;
        }

        public LightPath? CurrentPath { get; private set; }

        public Task SwitchAsync(LightPath path, IProgress<ApplicationStatus> progress, CancellationToken ct) {
            recorder.Add("light:" + path);
            CurrentPath = path;
            return Task.CompletedTask;
        }
    }

    public class FakeTelescopeMediator : ITelescopeMediator {
        private readonly Recorder recorder;

        public FakeTelescopeMediator(Recorder recorder) {
            this.recorder = recorder;
        }

        public TelescopeInfo Info { get; set; } = new TelescopeInfo { Connected = true, CanSlew = true, AtPark = false };

        public Func<Coordinates, Task<bool>> OnSlew { get; set; }

        public Task<bool> SlewToTopocentricCoordinates(TopocentricCoordinates coordinates, CancellationToken token) {
            return Task.FromResult(false);
        }

        public event Func<object, EventArgs, Task> Parked { add { } remove { } }

        public event Func<object, EventArgs, Task> Unparked { add { } remove { } }

        public event Func<object, EventArgs, Task> Homed { add { } remove { } }

        public event Func<object, MountSlewedEventArgs, Task> Slewed { add { } remove { } }

        public TelescopeInfo GetInfo() {
            return Info;
        }

        public Task<bool> SlewToCoordinatesAsync(Coordinates coords, CancellationToken token) {
            recorder.Add("slew");
            return OnSlew != null ? OnSlew(coords) : Task.FromResult(true);
        }

        public Task<bool> SlewToCoordinatesAsync(TopocentricCoordinates coords, CancellationToken token) {
            recorder.Add("slew");
            return Task.FromResult(true);
        }

        public void MoveAxis(TelescopeAxes axis, double rate) {
        }

        public void PulseGuide(GuideDirections direction, int duration) {
        }

        public Task<bool> Sync(Coordinates coordinates) {
            return Task.FromResult(true);
        }

        public Task<bool> MeridianFlip(Coordinates targetCoordinates, CancellationToken token) {
            return Task.FromResult(true);
        }

        public bool SetTrackingEnabled(bool trackingEnabled) {
            return true;
        }

        public bool SetTrackingMode(TrackingMode trackingMode) {
            return true;
        }

        public bool SetCustomTrackingRate(SiderealShiftTrackingRate rate) {
            return true;
        }

        public bool SendToSnapPort(bool start) {
            return true;
        }

        public Coordinates GetCurrentPosition() {
            return new Coordinates(Angle.ByDegree(0), Angle.ByDegree(0), Epoch.J2000);
        }

        public Task<bool> ParkTelescope(IProgress<ApplicationStatus> progress, CancellationToken token) {
            return Task.FromResult(true);
        }

        public Task<bool> UnparkTelescope(IProgress<ApplicationStatus> progress, CancellationToken token) {
            return Task.FromResult(true);
        }

        public Task WaitForSlew(CancellationToken token) {
            return Task.CompletedTask;
        }

        public Task<bool> FindHome(IProgress<ApplicationStatus> progress, CancellationToken token) {
            return Task.FromResult(true);
        }

        public void StopSlew() {
        }

        public PierSide DestinationSideOfPier(Coordinates coordinates) {
            return PierSide.pierUnknown;
        }

        public event Func<object, BeforeMeridianFlipEventArgs, Task> BeforeMeridianFlip { add { } remove { } }

        public Task RaiseBeforeMeridianFlip(BeforeMeridianFlipEventArgs args) {
            return Task.CompletedTask;
        }

        public event Func<object, AfterMeridianFlipEventArgs, Task> AfterMeridianFlip { add { } remove { } }

        public Task RaiseAfterMeridianFlip(AfterMeridianFlipEventArgs args) {
            return Task.CompletedTask;
        }

        public void RegisterHandler(ITelescopeVM handler) {
        }

        public void RegisterConsumer(ITelescopeConsumer consumer) {
        }

        public void RemoveConsumer(ITelescopeConsumer consumer) {
        }

        public Task<IList<string>> Rescan() {
            return Task.FromResult<IList<string>>(new List<string>());
        }

        public Task<bool> Connect() {
            return Task.FromResult(true);
        }

        public Task Disconnect() {
            return Task.CompletedTask;
        }

        public void Broadcast(TelescopeInfo deviceInfo) {
        }

        public string Action(string actionName, string actionParameters) {
            return string.Empty;
        }

        public string SendCommandString(string command, bool raw = true) {
            return string.Empty;
        }

        public bool SendCommandBool(string command, bool raw = true) {
            return false;
        }

        public void SendCommandBlind(string command, bool raw = true) {
        }

        public IDevice GetDevice() {
            return null;
        }

        public event Func<object, EventArgs, Task> Connected { add { } remove { } }

        public event Func<object, EventArgs, Task> Disconnected { add { } remove { } }
    }

    public class FakeCameraMediator : ICameraMediator {
        public CameraInfo Info { get; set; } = new CameraInfo { Connected = true, BitDepth = 16 };

        public CameraInfo GetInfo() {
            return Info;
        }

        public Task Capture(CaptureSequence sequence, CancellationToken token, IProgress<ApplicationStatus> progress) {
            return Task.CompletedTask;
        }

        public void SetSubSambleRectangle(NINA.Core.Utility.ObservableRectangle rectangle) {
        }

        public IAsyncEnumerable<IExposureData> LiveView(CancellationToken token) {
            return null;
        }

        public IAsyncEnumerable<IExposureData> LiveView(CaptureSequence sequence, CancellationToken token) {
            return null;
        }

        public Task<IExposureData> Download(CancellationToken token) {
            return Task.FromResult<IExposureData>(null);
        }

        public void AbortExposure() {
        }

        public void SetReadoutMode(short mode) {
        }

        public void SetReadoutModeForNormalImages(short mode) {
        }

        public void SetBinning(short binningX, short binningY) {
        }

        public void SetDewHeater(bool onOff) {
        }

        public bool AtTargetTemp => true;

        public double TargetTemp => 0;

        public Task<bool> CoolCamera(double temperature, TimeSpan duration, IProgress<ApplicationStatus> progress, CancellationToken ct) {
            return Task.FromResult(true);
        }

        public Task<bool> WarmCamera(TimeSpan duration, IProgress<ApplicationStatus> progress, CancellationToken ct) {
            return Task.FromResult(true);
        }

        public void RegisterCaptureBlock(ICameraConsumer cameraConsumer) {
        }

        public void ReleaseCaptureBlock(ICameraConsumer cameraConsumer) {
        }

        public bool IsFreeToCapture(ICameraConsumer cameraConsumer) {
            return true;
        }

        public void RegisterCaptureBlock(object cameraConsumer) {
        }

        public void ReleaseCaptureBlock(object cameraConsumer) {
        }

        public bool IsFreeToCapture(object cameraConsumer) {
            return true;
        }

        public void SetUSBLimit(int usbLimit) {
        }

        public event Func<object, EventArgs, Task> DownloadTimeout { add { } remove { } }

        public void RegisterHandler(ICameraVM handler) {
        }

        public void RegisterConsumer(ICameraConsumer consumer) {
        }

        public void RemoveConsumer(ICameraConsumer consumer) {
        }

        public Task<IList<string>> Rescan() {
            return Task.FromResult<IList<string>>(new List<string>());
        }

        public Task<bool> Connect() {
            return Task.FromResult(true);
        }

        public Task Disconnect() {
            return Task.CompletedTask;
        }

        public void Broadcast(CameraInfo deviceInfo) {
        }

        public string Action(string actionName, string actionParameters) {
            return string.Empty;
        }

        public string SendCommandString(string command, bool raw = true) {
            return string.Empty;
        }

        public bool SendCommandBool(string command, bool raw = true) {
            return false;
        }

        public void SendCommandBlind(string command, bool raw = true) {
        }

        public IDevice GetDevice() {
            return null;
        }

        public event Func<object, EventArgs, Task> Connected { add { } remove { } }

        public event Func<object, EventArgs, Task> Disconnected { add { } remove { } }
    }

    public class FakeFilterWheelMediator : IFilterWheelMediator {
        private readonly Recorder recorder;

        public FakeFilterWheelMediator(Recorder recorder) {
            this.recorder = recorder;
        }

        public FilterWheelInfo Info { get; set; } = new FilterWheelInfo { Connected = false };

        public event Func<object, FilterChangedEventArgs, Task> FilterChanged { add { } remove { } }

        public FilterWheelInfo GetInfo() {
            return Info;
        }

        public Task<FilterInfo> ChangeFilter(FilterInfo inputFilter, CancellationToken token = default, IProgress<ApplicationStatus> progress = null) {
            recorder.Add("filter:" + inputFilter?.Name);
            Info.SelectedFilter = inputFilter;
            return Task.FromResult(inputFilter);
        }

        public void RegisterHandler(IFilterWheelVM handler) {
        }

        public void RegisterConsumer(IFilterWheelConsumer consumer) {
        }

        public void RemoveConsumer(IFilterWheelConsumer consumer) {
        }

        public Task<IList<string>> Rescan() {
            return Task.FromResult<IList<string>>(new List<string>());
        }

        public Task<bool> Connect() {
            return Task.FromResult(true);
        }

        public Task Disconnect() {
            return Task.CompletedTask;
        }

        public void Broadcast(FilterWheelInfo deviceInfo) {
        }

        public string Action(string actionName, string actionParameters) {
            return string.Empty;
        }

        public string SendCommandString(string command, bool raw = true) {
            return string.Empty;
        }

        public bool SendCommandBool(string command, bool raw = true) {
            return false;
        }

        public void SendCommandBlind(string command, bool raw = true) {
        }

        public IDevice GetDevice() {
            return null;
        }

        public event Func<object, EventArgs, Task> Connected { add { } remove { } }

        public event Func<object, EventArgs, Task> Disconnected { add { } remove { } }
    }
}
