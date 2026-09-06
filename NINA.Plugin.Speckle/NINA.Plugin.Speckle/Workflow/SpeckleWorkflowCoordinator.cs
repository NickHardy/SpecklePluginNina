using NINA.Astrometry;
using NINA.Core.Model;
using NINA.Core.Model.Equipment;
using NINA.Core.Utility;
using NINA.Core.Utility.Notification;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Image.ImageData;
using NINA.Plugin.Speckle.ManualMount;
using NINA.Plugin.Speckle.Model;
using NINA.Plugin.Speckle.Services;
using NINA.Profile.Interfaces;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Plugin.Speckle.Workflow {

    [Export(typeof(ISpeckleWorkflowCoordinator))]
    [PartCreationPolicy(CreationPolicy.Shared)]
    public class SpeckleWorkflowCoordinator : ISpeckleWorkflowCoordinator {
        private readonly IProfileService profileService;
        private readonly ITelescopeMediator telescopeMediator;
        private readonly ICameraMediator cameraMediator;
        private readonly IFilterWheelMediator filterWheelMediator;
        private readonly ITargetListService targetListService;
        private readonly ITargetSchedulerService schedulerService;
        private readonly IReferenceStarService referenceStarService;
        private readonly ITargetPlanner planner;
        private readonly IRoiPositioningService roiPositioningService;
        private readonly IExposureCalibrationService exposureCalibrationService;
        private readonly ISpeckleAcquisitionService acquisitionService;
        private readonly ILightPathService lightPathService;
        private readonly ITemplateValidationService templateValidationService;
        private readonly ISpeckleOptionsProvider optionsProvider;
        private readonly IFringeAnalysisService fringeAnalysis;
        private const double SlitSearchAltitude = 70d;
        private const double SlitSearchRadius = 25d;
        private const double SlitSearchMinMagnitude = 0d;
        private const double SlitSearchMaxMagnitude = 6d;
        private const int SlitSearchCount = 12;

        private readonly ISimbadUtils simbadUtils;
        private readonly PauseGate pause = new PauseGate();
        private readonly object queueSync = new object();
        private readonly object captureSync = new object();
        private readonly List<SpeckleTarget> pinQueue = new List<SpeckleTarget>();
        private List<SpeckleTarget> targets = new List<SpeckleTarget>();
        private bool capturePlanLocked;
        private CaptureEntry triedEntry;
        private string filterBeforeTry;
        private bool filterWasTried;
        private volatile PhaseRewind rewindTo;
        private LegJump pendingLegJump;
        private readonly Dictionary<string, int> speckleRuns = new Dictionary<string, int>();
        private string appliedFilter;
        private string calibratedFilter;
        private double calibratedExposure;
        private WorkflowRunOptions runOptions;
        private const double RoiFallbackExposureTime = 5;
        private const double ExposureCalibrationTargetAdu = 0.2;
        private const double ExposureCalibrationStepTime = 0.01;
        private const double ExposureTimeMultiplier = 1;
        private const int CameraGain = -1;
        private const int CameraOffset = -1;
        private const string CaptureImageType = NINA.Equipment.Model.CaptureSequence.ImageTypes.LIGHT;
        private static readonly TimeSpan WindowLeadTime = TimeSpan.FromSeconds(60);

        private readonly object cancellationSync = new object();
        private CancellationTokenSource runCts;
        private CancellationTokenSource targetCts;
        private CancellationTokenSource legCts;
        private CancellationTokenSource phaseCts;
        private CancellationTokenSource windowCts;
        private CancellationTokenSource previewCts;
        private Task previewLoopTask;
        private volatile bool previewWillResume;
        private volatile TaskCompletionSource<bool> lightPathSwitchCompletion;
        private TaskCompletionSource<bool> runCompletion = CreateCompletion(true);
        private int running;

        [ImportingConstructor]
        public SpeckleWorkflowCoordinator(
            IProfileService profileService,
            ITelescopeMediator telescopeMediator,
            ICameraMediator cameraMediator,
            IFilterWheelMediator filterWheelMediator,
            ITargetListService targetListService,
            ITargetSchedulerService schedulerService,
            IReferenceStarService referenceStarService,
            ITargetPlanner planner,
            IRoiPositioningService roiPositioningService,
            IExposureCalibrationService exposureCalibrationService,
            ISpeckleAcquisitionService acquisitionService,
            ILightPathService lightPathService,
            ITemplateValidationService templateValidationService,
            ISpeckleOptionsProvider optionsProvider,
            IFringeAnalysisService fringeAnalysis,
            ISimbadUtils simbadUtils) {
            this.simbadUtils = simbadUtils;
            this.profileService = profileService;
            this.telescopeMediator = telescopeMediator;
            this.cameraMediator = cameraMediator;
            this.filterWheelMediator = filterWheelMediator;
            this.targetListService = targetListService;
            this.schedulerService = schedulerService;
            this.referenceStarService = referenceStarService;
            this.planner = planner;
            this.roiPositioningService = roiPositioningService;
            this.exposureCalibrationService = exposureCalibrationService;
            this.acquisitionService = acquisitionService;
            this.lightPathService = lightPathService;
            this.templateValidationService = templateValidationService;
            this.optionsProvider = optionsProvider;
            this.fringeAnalysis = fringeAnalysis;
            Gate.PendingChanged += (sender, args) => {
                Session.PendingGate = Gate.Pending;
                PendingChanged?.Invoke(this, EventArgs.Empty);
            };
            pause.Engaged += (sender, args) => {
                var wasHolding = Session.IsHolding;
                Session.IsHolding = true;
                if (!wasHolding) {
                    Logger.Info("Pause engaged at phase " + Session.Phase + " (target " + TargetLabel.Of(Session.CurrentTarget) + ", leg " + LegName(Session.ImagingReference) + ")");
                }
            };
        }

        public SpeckleWorkflowSession Session { get; } = new SpeckleWorkflowSession();

        public WorkflowGate Gate { get; } = new WorkflowGate();

        public bool IsRunning => Volatile.Read(ref running) == 1;

        public IReadOnlyList<SpeckleTarget> Targets => targets;

        public IReadOnlyList<string> LoadedLists => targets
            .Select(target => target.SourceList)
            .Where(path => !string.IsNullOrEmpty(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        public IReadOnlyList<TemplateIssue> TemplateIssues { get; private set; } = new List<TemplateIssue>();

        public IReadOnlyList<SpeckleTarget> PinQueue {
            get {
                lock (queueSync) {
                    return pinQueue.ToArray();
                }
            }
        }

        public bool StopAfterCurrentTarget {
            get => Session.StopAfterCurrentTarget;
            set => Session.StopAfterCurrentTarget = value;
        }

        public event EventHandler<WorkflowPhaseChangedEventArgs> PhaseChanged;

        public event EventHandler PendingChanged;

        public event EventHandler<RoiSeriesProgress> VideoExposuresProgress;

        public event EventHandler TargetsChanged;

        public event EventHandler QueueChanged;

        public event EventHandler CapturePlanChanged;

        public async Task LoadListAsync(string csvPath, CancellationToken ct) {
            var speckle = optionsProvider.Current;
            var defaults = new TargetListDefaults {
                User = speckle?.User,
                Template = speckle?.DefaultTemplate,
                TemplateRef = speckle?.DefaultRefTemplate,
                Cycles = speckle?.Cycles ?? 1,
                Exposures = speckle?.Exposures ?? 1000,
                ExposureTime = speckle?.ExposureTime ?? 1
            };
            var ignoreLimits = speckle?.IgnoreVisibilityLimits ?? false;
            if (ignoreLimits) {
                Logger.Info("Ignoring visibility limits; loading every target in the list");
            }
            Logger.Info("Loading target list " + csvPath);
            var loaded = await targetListService.LoadTargetsAsync(csvPath, defaults, ignoreLimits, ct).ConfigureAwait(false);
            var kept = targets.Where(target => !IsFromList(target, csvPath)).ToList();
            var reloaded = targets.Count - kept.Count;
            if (reloaded > 0) {
                Logger.Info("Target list " + csvPath + " was already loaded; its " + reloaded + " targets are replaced by the file as it reads now");
            }
            templateValidationService.FillFiltersFromTemplates(loaded);
            targets = targetListService.Order(kept.Concat(KeepTargetBeingImaged(loaded, csvPath))).ToList();
            LogTargetsInMoreThanOneList();
            Logger.Info("Target list " + csvPath + " loaded with " + loaded.Count + " targets; "
                + targets.Count + " targets from " + LoadedLists.Count + " lists are now loaded");
            DropQueuedTargetsThatAreGone();
            Session.TargetCount = targets.Count;
            ValidateTemplates();
            if (!IsRunning) {
                SetPhase(WorkflowPhase.ListLoaded);
            }
            TargetsChanged?.Invoke(this, EventArgs.Empty);
            QueueChanged?.Invoke(this, EventArgs.Empty);
        }

        private IEnumerable<SpeckleTarget> KeepTargetBeingImaged(IReadOnlyList<SpeckleTarget> loaded, string csvPath) {
            var imaging = Session.CurrentTarget;
            if (!IsRunning || imaging == null || !IsFromList(imaging, csvPath)) {
                return loaded;
            }
            Logger.Info(TargetLabel.Of(imaging) + " is being imaged, so its progress is kept rather than taken from " + csvPath + " again");
            return loaded.Where(target => !string.Equals(target.Name, imaging.Name, StringComparison.OrdinalIgnoreCase))
                .Concat(new[] { imaging });
        }

        public const string AddedByHand = "Added by hand";

        public SpeckleTarget AddTargetByHand(string name, double ra2000, double dec2000) {
            var speckle = optionsProvider.Current;
            var target = new SpeckleTarget {
                Name1 = string.IsNullOrWhiteSpace(name) ? "Star" : name.Trim(),
                Type = "M",
                Proj = "",
                Obs = speckle?.User ?? "",
                RA2000 = ra2000,
                Dec2000 = dec2000,
                GetRef = 0,
                Priority = 1,
                Cycles = 1,
                Nights = 1,
                Exp = speckle?.ExposureTime ?? 1,
                NExp = speckle?.Exposures ?? 1000,
                Template = speckle?.DefaultTemplate ?? "",
                TemplateRef = speckle?.DefaultRefTemplate ?? "",
                SourceList = AddedByHand
            };
            var night = targetListService.GetNighttimeData(DateTime.Now);
            var scheduling = SchedulingOptions.FromOptions(speckle, ignoreLimits: true);
            targetListService.ApplyVisibilityWindows(target, night, scheduling);
            targets = targetListService.Order(targets.Concat(new[] { target })).ToList();
            Session.TargetCount = targets.Count;
            Logger.Info("Target added by hand: " + TargetLabel.Of(target) + " at "
                + CoordinateFormat.RaHours(ra2000 / 15d) + " " + CoordinateFormat.DecDegrees(dec2000)
                + "; " + targets.Count + " targets are now loaded");
            ValidateTemplates();
            if (!IsRunning) {
                SetPhase(WorkflowPhase.ListLoaded);
            }
            TargetsChanged?.Invoke(this, EventArgs.Empty);
            return target;
        }

        public Task<IReadOnlyList<ReferenceStar>> FindBrightStarsInSlitAsync(CancellationToken ct) {
            var speckle = optionsProvider.Current;
            var astrometry = profileService.ActiveProfile.AstrometrySettings;
            var slitAzimuth = speckle == null
                ? 0d
                : speckle.DomePositionLock ? Dockables.Kepler.KeplerProjection.Mod360(speckle.DomePosition)
                : speckle.DomeSlitSouth ? 180d : 0d;
            var slit = new InputTopocentricCoordinates(Angle.ByDegree(astrometry.Latitude), Angle.ByDegree(astrometry.Longitude)) {
                AltDegrees = (int)SlitSearchAltitude,
                AzDegrees = (int)slitAzimuth
            };
            var centre = slit.Coordinates.Transform(Epoch.J2000);
            Logger.Info("Looking for bright stars within " + SlitSearchRadius + " degrees of altitude "
                + SlitSearchAltitude + " azimuth " + slitAzimuth);
            return FindBrightStarsAsync(centre, ct);
        }

        private async Task<IReadOnlyList<ReferenceStar>> FindBrightStarsAsync(Coordinates centre, CancellationToken ct) {
            var found = await simbadUtils.FindSingleBrightStars(new Progress<ApplicationStatus>(), ct, centre,
                                                               SlitSearchRadius, SlitSearchMinMagnitude, SlitSearchMaxMagnitude)
                .ConfigureAwait(false);
            var brightest = found.OrderBy(star => star.Rp).Take(SlitSearchCount).ToList();
            Logger.Info("Found " + found.Count + " bright stars near the dome slit; offering the brightest " + brightest.Count);
            return brightest;
        }

        public void RemoveList(string csvPath) {
            var removed = targets.Where(target => IsFromList(target, csvPath)).ToList();
            if (removed.Count == 0) {
                Logger.Info("Target list " + csvPath + " was asked to be removed but it is not loaded");
                return;
            }
            var imaging = Session.CurrentTarget;
            var stopsCurrentTarget = IsRunning && imaging != null && IsFromList(imaging, csvPath);
            Logger.Info("Removing target list " + csvPath + " with its " + removed.Count + " targets"
                + (stopsCurrentTarget ? "; " + TargetLabel.Of(imaging) + " is being imaged and is stopped" : ""));
            targets = targets.Where(target => !IsFromList(target, csvPath)).ToList();
            DropQueuedTargetsThatAreGone();
            Session.TargetCount = targets.Count;
            ValidateTemplates();
            if (!IsRunning) {
                SetPhase(targets.Count == 0 ? WorkflowPhase.Idle : WorkflowPhase.ListLoaded);
            }
            TargetsChanged?.Invoke(this, EventArgs.Empty);
            QueueChanged?.Invoke(this, EventArgs.Empty);
            if (stopsCurrentTarget) {
                CancelCurrentTarget("list removed");
            }
        }

        private static bool IsFromList(SpeckleTarget target, string csvPath) {
            return string.Equals(target.SourceList, csvPath, StringComparison.OrdinalIgnoreCase);
        }

        private void DropQueuedTargetsThatAreGone() {
            lock (queueSync) {
                pinQueue.RemoveAll(queued => !targets.Contains(queued));
            }
        }

        private void LogTargetsInMoreThanOneList() {
            var shared = targets
                .GroupBy(target => target.Name, StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Select(target => target.SourceList).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
                .Select(group => group.Key)
                .ToList();
            if (shared.Count > 0) {
                Logger.Warning(shared.Count + " targets appear in more than one loaded list and will be imaged once per list: "
                    + string.Join(", ", shared.Take(10)));
            }
        }

        public void ClearTargets() {
            var stopsCurrentTarget = IsRunning && Session.CurrentTarget != null;
            Logger.Info("Clearing the target list (" + targets.Count + " targets from " + LoadedLists.Count + " lists)"
                + (stopsCurrentTarget ? "; " + TargetLabel.Of(Session.CurrentTarget) + " is being imaged and is stopped" : ""));
            targets = new List<SpeckleTarget>();
            lock (queueSync) {
                pinQueue.Clear();
            }
            Session.TargetCount = 0;
            if (stopsCurrentTarget) {
                CancelCurrentTarget("list cleared");
            }
            Session.CurrentTarget = null;
            TemplateIssues = new List<TemplateIssue>();
            if (!IsRunning) {
                SetPhase(WorkflowPhase.Idle);
            }
            TargetsChanged?.Invoke(this, EventArgs.Empty);
            QueueChanged?.Invoke(this, EventArgs.Empty);
        }

        public IReadOnlyList<TemplateIssue> ValidateTemplates() {
            var issues = templateValidationService.Validate(targets, CurrentPlanDefaults());
            foreach (var issue in issues) {
                Logger.Warning(issue.Message);
            }
            TemplateIssues = issues;
            return issues;
        }

        public WorkflowRunOptions BuildRunOptions() {
            var speckle = optionsProvider.Current;
            var profile = profileService.ActiveProfile;
            return new WorkflowRunOptions {
                Scheduling = SchedulingOptions.FromOptions(speckle, ignoreLimits: speckle.IgnoreVisibilityLimits),
                ReferenceQuery = ReferenceStarQueryOptions.FromOptions(speckle),
                PlanDefaults = TargetPlanDefaults.FromOptions(speckle),
                VideoExposuresMode = ResolveVideoExposuresMode(speckle),
                ReadVideoExposuresMode = () => ResolveVideoExposuresMode(optionsProvider.Current),
                AutoSkipFailedReference = speckle.AutoSkipFailedReference,
                DualCameraSetup = speckle.DualCameraSetup,
                PlatesolveRoi = speckle.UsePlateSolving,
                SaveCsvToFitsHeader = speckle.SaveCsvToFitsHeader,
                RoiWidth = speckle.RoiSize > 0 ? speckle.RoiSize : 512,
                RoiHeight = speckle.RoiSize > 0 ? speckle.RoiSize : 512,
                DefaultExposures = speckle.Exposures,
                DefaultExposureTime = speckle.ExposureTime,
                ExposureTimeMax = speckle.MaxExposureTime > 0 ? speckle.MaxExposureTime : 1,
                Latitude = profile.AstrometrySettings.Latitude,
                Longitude = profile.AstrometrySettings.Longitude,
                Horizon = profile.AstrometrySettings.Horizon,
                SnapshotDirectory = profile.ImageFileSettings.FilePath,
                Binning = new BinningMode(1, 1),
                ReadAutomaticallyRun = () => optionsProvider.Current.AutomaticallyRun,
                ReadCameraBitDepth = () => cameraMediator.GetInfo()?.BitDepth ?? 16,
                ResolveFilter = name => FilterWheelCatalog.Find(profileService, name)
            };
        }

        private static bool LooksLikeZwo(string cameraName) {
            return !string.IsNullOrWhiteSpace(cameraName)
                && (cameraName.IndexOf("ZWO", StringComparison.OrdinalIgnoreCase) >= 0
                    || cameraName.IndexOf("ASI", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private RoiSeriesMode ResolveVideoExposuresMode(Speckle speckle) {
            var info = cameraMediator.GetInfo();
            var canLiveView = info != null && info.Connected && info.CanShowLiveView;
            var readFrom = ", read from profile " + (profileService.ActiveProfile?.Name ?? "unknown")
                + " with camera " + (info?.Name ?? "none");
            switch (speckle.VideoExposuresMode) {
                case 1:
                    Logger.Info("Video exposures will use single exposures, forced by the plugin options" + readFrom);
                    return RoiSeriesMode.Sequenced;

                case 2:
                    Logger.Info("Video exposures will use video mode, forced by the plugin options" + readFrom
                        + (canLiveView ? "" : ". The driver reports video mode as unavailable, which ZWO drivers do even though they support it, so it is being started anyway"));
                    return RoiSeriesMode.Video;

                default:
                    if (!canLiveView && LooksLikeZwo(info?.Name)) {
                        Logger.Info("Video exposures will use single exposures, chosen automatically. The camera " + info.Name
                            + " does support video mode, but its driver reports otherwise. Set video exposures to Force video mode in the plugin options to stream instead, which is far faster for small regions.");
                        return RoiSeriesMode.Sequenced;
                    }
                    Logger.Info("Video exposures will use " + (canLiveView ? "video mode" : "single exposures")
                        + ", chosen automatically (camera " + (info?.Name ?? "none") + ", video mode available " + canLiveView + ")");
                    return canLiveView ? RoiSeriesMode.Video : RoiSeriesMode.Sequenced;
            }
        }

        public async Task RunAsync(WorkflowRunOptions options, IProgress<ApplicationStatus> progress, CancellationToken ct) {
            if (Interlocked.CompareExchange(ref running, 1, 0) != 0) {
                throw new InvalidOperationException("A run is already in progress");
            }
            runOptions = options ?? BuildRunOptions();
            runCompletion = CreateCompletion(false);
            var runSource = CancellationTokenSource.CreateLinkedTokenSource(ct);
            PublishSource(ref runCts, runSource);
            Session.StopAfterCurrentTarget = false;
            Session.TargetNr = 0;
            Session.TargetsImaged = 0;
            Session.TargetsSkipped = 0;
            Session.TotalFrames = 0;
            Session.SkipReference = false;
            var runWatch = Stopwatch.StartNew();
            Logger.Info("Run starting: " + targets.Count + " targets, dual camera " + runOptions.DualCameraSetup
                + ", platesolve roi " + runOptions.PlatesolveRoi + ", auto run " + (runOptions.ReadAutomaticallyRun?.Invoke() ?? false));
            try {
                var runToken = runSource.Token;
                while (true) {
                    await WaitAtSafeBoundaryAsync(runToken).ConfigureAwait(false);
                    runToken.ThrowIfCancellationRequested();
                    SetPhase(WorkflowPhase.PickingTarget);
                    var target = TakeNextTarget();
                    if (target == null) {
                        SetPhase(WorkflowPhase.ListLoaded);
                        return;
                    }
                    var targetSource = CancellationTokenSource.CreateLinkedTokenSource(runToken);
                    PublishSource(ref targetCts, targetSource);
                    {
                        try {
                            await RunTargetAsync(target, progress, targetSource.Token).ConfigureAwait(false);
                            Session.TargetsImaged++;
                        } catch (OperationCanceledException) when (targetSource.IsCancellationRequested && !runToken.IsCancellationRequested) {
                            Session.TargetsSkipped++;
                            Logger.Info("Target skipped: " + target.Name + " (reason " + (string.IsNullOrEmpty(target.Note2) ? "cancelled" : target.Note2) + ")");
                        } catch (OperationCanceledException) {
                            Logger.Info("Target " + target.Name + " interrupted because the run was stopped");
                            throw;
                        } catch (Exception ex) {
                            Session.TargetsSkipped++;
                            target.ImageTarget = false;
                            target.Note2 = "failed";
                            Logger.Error("Target " + target.Name + " failed unexpectedly; continuing with the next target", ex);
                            Notification.ShowWarning("Target " + target.Name + " failed; skipped");
                        } finally {
                            RetireSource(ref targetCts, targetSource);
                        }
                    }
                    if (Session.StopAfterCurrentTarget) {
                        SetPhase(WorkflowPhase.ListLoaded);
                        return;
                    }
                }
            } catch (OperationCanceledException) {
                Logger.Info("Run stopped by request after " + runWatch.ElapsedMilliseconds + " ms");
                SetPhase(WorkflowPhase.Stopped);
            } finally {
                RetireSource(ref runCts, runSource);
                Volatile.Write(ref running, 0);
                Session.IsHoldRequested = false;
                Session.IsHolding = false;
                pause.Resume();
                runCompletion.TrySetResult(true);
                progress?.Report(new ApplicationStatus { Status = string.Empty });
                Logger.Info("Run ended after " + runWatch.ElapsedMilliseconds + " ms - " + Session.TargetsImaged + " imaged, "
                    + Session.TargetsSkipped + " skipped, " + Session.TotalFrames + " frames");
            }
        }

        public void Confirm() {
            Gate.Confirm();
        }

        public void RetryStep() {
            Gate.Retry();
        }

        public void SkipStep() {
            Gate.Skip();
        }

        public void SkipCurrentTarget() {
            Logger.Info("Skip target requested for " + TargetLabel.Of(Session.CurrentTarget) + " at phase " + Session.Phase);
            CancelCurrentTarget("skipped-by-operator");
        }

        public void SkipReference() {
            SetSkipReference(true);
        }

        public void SetSkipReference(bool skip) {
            if (Session.SkipReference == skip) {
                Logger.Info("Skip reference already " + skip + " for " + TargetLabel.Of(Session.CurrentTarget));
                return;
            }
            if (!skip && Session.ReferenceLegAbandoned) {
                Logger.Info("Cannot re-arm the reference leg for " + TargetLabel.Of(Session.CurrentTarget) + "; it was already abandoned");
                return;
            }
            Logger.Info("Skip reference armed " + skip + " for " + TargetLabel.Of(Session.CurrentTarget) + " at phase " + Session.Phase);
            Session.SkipReference = skip;
            if (skip && Session.ImagingReference) {
                Session.ReferenceLegAbandoned = true;
                Logger.Info("Reference leg for " + TargetLabel.Of(Session.CurrentTarget) + " abandoned while it was running");
                CancelSource(ref legCts);
            }
        }

        public async Task PauseAsync() {
            if (!IsRunning) {
                Logger.Info("Pause requested but no run is in progress");
                return;
            }
            Logger.Info("Pause requested at phase " + Session.Phase + " (target " + TargetLabel.Of(Session.CurrentTarget)
                + ", leg " + LegName(Session.ImagingReference) + ", gate " + (Gate.Pending?.Title ?? "none") + ")");
            var engaged = CreateCompletion(false);
            void OnEngaged(object sender, EventArgs e) {
                engaged.TrySetResult(true);
            }
            pause.Engaged += OnEngaged;
            try {
                pause.RequestPause();
                Session.IsHoldRequested = true;
                await StopPreviewAsync("Pause").ConfigureAwait(false);
                if (Gate.Pending != null) {
                    Logger.Info("Pause armed while gate " + Gate.Pending.Title + " is waiting; capture is stopped and the run stays paused at the next safe boundary");
                    return;
                }
                await Task.WhenAny(engaged.Task, runCompletion.Task).ConfigureAwait(false);
                Logger.Info("Pause is now active at phase " + Session.Phase);
            } finally {
                pause.Engaged -= OnEngaged;
            }
        }

        public void Resume() {
            Logger.Info("Resume requested at phase " + Session.Phase + " (target " + TargetLabel.Of(Session.CurrentTarget)
                + ", leg " + LegName(Session.ImagingReference) + ", gate " + (Gate.Pending?.Title ?? "none") + ")");
            Session.IsHoldRequested = false;
            Session.IsHolding = false;
            pause.Resume();
        }

        public void RequestStop() {
            Logger.Info("Stop requested at phase " + Session.Phase + " (target " + TargetLabel.Of(Session.CurrentTarget) + ")");
            CancelSource(ref runCts);
        }

        public void ImageNow() {
            Logger.Info("Image now requested for " + TargetLabel.Of(Session.CurrentTarget));
            CancelSource(ref windowCts);
        }

        public void FinishVideoExposuresEarly() {
            if (Session.Phase == WorkflowPhase.RunningVideoExposures) {
                Logger.Info("Video exposures ending early after " + Session.FrameCount + " frames");
                CancelSource(ref phaseCts);
            }
        }

        public void OverrideExposureTime(double seconds) {
            if (seconds > 0) {
                Logger.Info("Exposure time overridden to " + seconds + " s");
                Session.CalculatedExposureTime = seconds;
                SetCaptureEntryExposureTime(PendingCaptureEntry(), seconds);
            }
        }

        public IReadOnlyList<string> RefreshAvailableFilters() {
            var names = FilterWheelCatalog.Names(profileService);
            Session.AvailableFilters = names;
            return names;
        }

        public bool AddCaptureEntry(string filter, double exposureTime, int frameCount) {
            var entry = new CaptureEntry(filter, exposureTime, frameCount);
            return EditCapturePlan("add " + entry, () => {
                Session.CaptureEntries.AddEntry(entry);
                return true;
            });
        }

        public bool RemoveCaptureEntry(CaptureEntry entry) {
            return EditCapturePlan("remove " + entry, () => {
                if (entry == null || Session.CaptureEntries.Count <= 1 || !Session.CaptureEntries.Contains(entry)) {
                    return false;
                }
                Session.CaptureEntries.RemoveEntry(entry);
                return true;
            });
        }

        public bool MoveCaptureEntry(CaptureEntry entry, int newIndex) {
            return EditCapturePlan("move " + entry + " to position " + (newIndex + 1), () => {
                var oldIndex = entry == null ? -1 : Session.CaptureEntries.IndexOf(entry);
                if (oldIndex < 0) {
                    return false;
                }
                var target = Math.Clamp(newIndex, 0, Session.CaptureEntries.Count - 1);
                if (target == oldIndex) {
                    return false;
                }
                Session.CaptureEntries.MoveEntry(oldIndex, target);
                return true;
            });
        }

        public bool SetCaptureEntryFilter(CaptureEntry entry, string filter) {
            return EditCaptureEntry(entry, "filter " + filter, () => entry.Filter = filter);
        }

        public bool SetCaptureEntryExposureTime(CaptureEntry entry, double seconds) {
            return EditCaptureEntry(entry, "exposure " + seconds + " s", () => entry.ExposureTime = seconds);
        }

        public bool SetCaptureEntryFrameCount(CaptureEntry entry, int frames) {
            return EditCaptureEntry(entry, "frames " + frames, () => entry.FrameCount = frames);
        }

        public void OverrideReference(ReferenceStar star) {
            if (star == null) {
                return;
            }
            var target = Session.CurrentTarget;
            if (target == null) {
                return;
            }
            target.ReferenceStar = star;
            Session.CurrentReference = star;
        }

        public static int StageRank(WorkflowPhase phase) {
            switch (phase) {
                case WorkflowPhase.PickingTarget:
                case WorkflowPhase.SwitchingToWide:
                case WorkflowPhase.AwaitingSlewConfirmation:
                case WorkflowPhase.Slewing:
                case WorkflowPhase.Centering:
                case WorkflowPhase.SwitchingToScience:
                    return 1;

                case WorkflowPhase.PositioningRoi:
                    return 2;

                case WorkflowPhase.CalibratingExposure:
                    return 3;

                case WorkflowPhase.AwaitingImageConfirmation:
                    return 4;

                default:
                    return 5;
            }
        }

        public void JumpToStage(WorkflowPhase phase, bool toReferenceLeg) {
            var target = Session.CurrentTarget;
            if (!IsRunning || target == null) {
                return;
            }
            if (toReferenceLeg != Session.ImagingReference) {
                JumpToOtherLeg(target, phase, toReferenceLeg);
                return;
            }
            Logger.Info("Operator jumped to " + phase + " on the " + LegName(toReferenceLeg) + " leg of " + TargetLabel.Of(target));
            rewindTo = new PhaseRewind { Phase = phase };
            if (Gate.Pending != null) {
                Gate.Resolve(GateOutcome.Retry);
                return;
            }
            CancelSource(ref phaseCts);
        }

        private void JumpToOtherLeg(SpeckleTarget target, WorkflowPhase phase, bool toReferenceLeg) {
            if (toReferenceLeg && !CanImageReference(target)) {
                Logger.Info("Jump to the reference leg of " + TargetLabel.Of(target) + " refused; there is no reference star to image");
                Notification.ShowWarning("There is no reference star to image for " + TargetLabel.Of(target));
                return;
            }
            if (legCts == null) {
                Logger.Info("Jump to the " + LegName(toReferenceLeg) + " leg of " + TargetLabel.Of(target)
                    + " refused; no leg is running at phase " + Session.Phase);
                Notification.ShowWarning("Wait until " + TargetLabel.Of(target) + " is under way before changing step");
                return;
            }
            Logger.Info("Operator left the " + LegName(Session.ImagingReference) + " leg of " + TargetLabel.Of(target)
                + " for " + phase + " on the " + LegName(toReferenceLeg) + " leg");
            Interlocked.Exchange(ref pendingLegJump, new LegJump { Phase = phase, ToReference = toReferenceLeg });
            if (toReferenceLeg) {
                Session.SkipReference = false;
                Session.ReferenceLegAbandoned = false;
            }
            CancelSource(ref legCts);
        }

        public void QueueNext(SpeckleTarget target) {
            if (target == null) {
                return;
            }
            int position;
            lock (queueSync) {
                if (pinQueue.Contains(target)) {
                    Logger.Info("Queue add ignored - " + TargetLabel.Of(target) + " is already queued");
                    return;
                }
                pinQueue.Add(target);
                position = pinQueue.Count;
            }
            Logger.Info("Queue add: " + TargetLabel.Of(target) + " at position " + position);
            QueueChanged?.Invoke(this, EventArgs.Empty);
        }

        public void RemoveFromQueue(SpeckleTarget target) {
            bool removed;
            lock (queueSync) {
                removed = pinQueue.Remove(target);
            }
            if (removed) {
                Logger.Info("Queue remove: " + TargetLabel.Of(target));
                QueueChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public void MoveInQueue(SpeckleTarget target, int newIndex) {
            int oldPosition;
            int newPosition;
            lock (queueSync) {
                var oldIndex = pinQueue.IndexOf(target);
                if (oldIndex < 0) {
                    return;
                }
                pinQueue.RemoveAt(oldIndex);
                newIndex = Math.Clamp(newIndex, 0, pinQueue.Count);
                pinQueue.Insert(newIndex, target);
                oldPosition = oldIndex + 1;
                newPosition = newIndex + 1;
            }
            Logger.Info("Queue move: " + TargetLabel.Of(target) + " from position " + oldPosition + " to position " + newPosition);
            QueueChanged?.Invoke(this, EventArgs.Empty);
        }

        public async Task SwitchLightPathAsync(LightPath path, IProgress<ApplicationStatus> progress, CancellationToken ct) {
            var watch = Stopwatch.StartNew();
            Logger.Info("Light path switch to " + path + " requested (current " + (Session.CurrentLightPath?.ToString() ?? "unknown") + ")");
            var completion = CreateCompletion(false);
            lightPathSwitchCompletion = completion;
            Session.CurrentLightPath = null;
            try {
                await StopPreviewAsync("Light path switch").ConfigureAwait(false);
                await lightPathService.SwitchAsync(path, progress, ct).ConfigureAwait(false);
                Session.CurrentLightPath = path;
                Logger.Info("Light path switch to " + path + " done in " + watch.ElapsedMilliseconds + " ms");
            } finally {
                lightPathSwitchCompletion = null;
                completion.TrySetResult(true);
            }
        }

        private async Task WaitForLightPathSwitchAsync(CancellationToken ct) {
            var completion = lightPathSwitchCompletion;
            if (completion == null) {
                return;
            }
            Logger.Info("Waiting for the light path switch to finish");
            await completion.Task.WaitAsync(ct).ConfigureAwait(false);
        }

        private TargetPlanDefaults CurrentPlanDefaults() {
            if (runOptions?.PlanDefaults != null) {
                return runOptions.PlanDefaults;
            }
            var speckle = optionsProvider.Current;
            return speckle != null ? TargetPlanDefaults.FromOptions(speckle) : new TargetPlanDefaults();
        }

        private SpeckleTarget TakeNextTarget() {
            var now = runOptions.Clock();
            SpeckleTarget pinned = null;
            var queueChanged = false;
            lock (queueSync) {
                while (pinQueue.Count > 0 && pinned == null) {
                    var candidate = pinQueue[0];
                    pinQueue.RemoveAt(0);
                    queueChanged = true;
                    if (targets.Contains(candidate) && SchedulingRules.HasCyclesLeft(candidate)) {
                        pinned = candidate;
                    }
                }
            }
            if (queueChanged) {
                QueueChanged?.Invoke(this, EventArgs.Empty);
            }
            if (pinned != null) {
                if (pinned.ImageTime > now) {
                    pinned.ImageTime = now;
                }
                Logger.Info("Using queued target " + pinned.Name);
                return pinned;
            }
            return schedulerService.PickNextTarget(targets, runOptions.Scheduling, Session.CurrentTarget?.RA2000, now);
        }

        private void PrepareSessionForTarget(SpeckleTarget target) {
            rewindTo = null;
            Interlocked.Exchange(ref pendingLegJump, null);
            Session.CurrentTarget = target;
            Session.CurrentReference = target.ReferenceStar != null && target.ReferenceStar.RA2000 != 0 ? target.ReferenceStar : null;
            Session.SkipReference = false;
            Session.ReferenceLegAbandoned = false;
            Session.ImagingReference = false;
            Session.CurrentPlan = null;
            Session.TargetNr++;
            Session.FrameCount = 0;
            Session.Fps = 0;
            Session.Orientation = null;
            Session.ArcsecPerPix = null;
            Session.CalculatedExposureTime = 0;
        }

        private async Task RunTargetAsync(SpeckleTarget target, IProgress<ApplicationStatus> progress, CancellationToken targetToken) {
            var targetWatch = Stopwatch.StartNew();
            PrepareSessionForTarget(target);
            Logger.Info("Target started: " + TargetLabel.Of(target) + " (" + Session.TargetNr + "/" + Session.TargetCount
                + ", cycles " + target.Completed_cycles + "/" + target.Cycles + ", getRef " + target.GetRef + ")");

            await WaitForImagingWindowAsync(target, targetToken).ConfigureAwait(false);

            await ResolveReferenceAsync(target, progress, targetToken).ConfigureAwait(false);

            var onReference = false;
            var targetCycleCounted = false;
            var referenceCycleCounted = false;
            while (true) {
                if (!onReference) {
                    if (await RunTargetLegAsync(target, progress, targetToken).ConfigureAwait(false) && !targetCycleCounted) {
                        targetCycleCounted = true;
                        if (target.RegisterTarget) {
                            schedulerService.MarkCycleComplete(target, runOptions.Clock());
                            Logger.Info("Cycle recorded for " + TargetLabel.Of(target) + ": cycles " + target.CyclesDisplay
                                + ", nights " + target.Completed_nights + "/" + target.Nights);
                            await SaveSnapshotSafeAsync().ConfigureAwait(false);
                        }
                    }
                } else if (ShouldRunReferenceLeg(target)) {
                    if (await RunReferenceLegAsync(target, progress, targetToken).ConfigureAwait(false) && !referenceCycleCounted) {
                        referenceCycleCounted = true;
                        target.Completed_ref_cycles++;
                        if (target.RegisterTarget) {
                            await SaveSnapshotSafeAsync().ConfigureAwait(false);
                        }
                    }
                } else {
                    Logger.Info("Reference leg skipped for " + TargetLabel.Of(target) + " (skipRequested " + Session.SkipReference
                        + ", getRef " + target.GetRef + ", reference " + (target.ReferenceStar?.Name ?? "none") + ")");
                }

                var jump = Interlocked.Exchange(ref pendingLegJump, null);
                if (jump != null) {
                    onReference = jump.ToReference;
                    rewindTo = new PhaseRewind { Phase = jump.Phase };
                    Logger.Info("Continuing on the " + LegName(onReference) + " leg of " + TargetLabel.Of(target) + " at " + jump.Phase);
                    continue;
                }
                if (onReference) {
                    break;
                }
                onReference = true;
            }

            Session.ImagingReference = false;
            SetPhase(WorkflowPhase.TargetComplete);
            Logger.Info("Target completed: " + TargetLabel.Of(target) + " in " + targetWatch.ElapsedMilliseconds + " ms, " + Session.FrameCount + " frames in the last leg");
        }

        private async Task<bool> RunTargetLegAsync(SpeckleTarget target, IProgress<ApplicationStatus> progress, CancellationToken targetToken) {
            var plan = planner.BuildPlan(target, false, runOptions.PlanDefaults);
            var legSource = CancellationTokenSource.CreateLinkedTokenSource(targetToken);
            PublishSource(ref legCts, legSource);
            {
                try {
                    await RunLegAsync(target, plan, false, progress, legSource.Token).ConfigureAwait(false);
                    return true;
                } catch (OperationCanceledException) when (legSource.IsCancellationRequested && !targetToken.IsCancellationRequested) {
                    Logger.Info("Target leg for " + TargetLabel.Of(target) + " left by the operator");
                    return false;
                } finally {
                    RetireSource(ref legCts, legSource);
                }
            }
        }

        private async Task<bool> RunReferenceLegAsync(SpeckleTarget target, IProgress<ApplicationStatus> progress, CancellationToken targetToken) {
            var plan = planner.BuildPlan(target, true, runOptions.PlanDefaults);
            var referenceWatch = Stopwatch.StartNew();
            Logger.Info("Reference leg started for " + TargetLabel.Of(target) + " on " + (target.ReferenceStar?.Name ?? "unnamed reference"));
            var legSource = CancellationTokenSource.CreateLinkedTokenSource(targetToken);
            PublishSource(ref legCts, legSource);
            {
                try {
                    await RunLegAsync(target, plan, true, progress, legSource.Token).ConfigureAwait(false);
                    Logger.Info("Reference leg completed for " + TargetLabel.Of(target) + " in " + referenceWatch.ElapsedMilliseconds + " ms");
                    return true;
                } catch (OperationCanceledException) when (legSource.IsCancellationRequested && !targetToken.IsCancellationRequested) {
                    Logger.Info("Reference leg for " + TargetLabel.Of(target) + " left after " + referenceWatch.ElapsedMilliseconds + " ms");
                    return false;
                } catch (OperationCanceledException) {
                    throw;
                } catch (Exception ex) {
                    Logger.Error("Reference leg for " + TargetLabel.Of(target) + " failed; keeping the target and continuing", ex);
                    Notification.ShowWarning("Reference leg for " + target.Name + " failed; reference skipped");
                    return false;
                } finally {
                    RetireSource(ref legCts, legSource);
                }
            }
        }

        private async Task WaitForImagingWindowAsync(SpeckleTarget target, CancellationToken targetToken) {
            var wait = target.ImageTime - runOptions.Clock() - WindowLeadTime;
            if (wait <= TimeSpan.Zero) {
                return;
            }
            Session.WindowOpensAt = target.ImageTime;
            SetPhase(WorkflowPhase.WaitingForWindow);
            var windowSource = CancellationTokenSource.CreateLinkedTokenSource(targetToken);
            PublishSource(ref windowCts, windowSource);
            try {
                await runOptions.Delay(wait, windowSource.Token).ConfigureAwait(false);
            } catch (OperationCanceledException) when (windowSource.IsCancellationRequested && !targetToken.IsCancellationRequested) {
                Logger.Info("Waiting for window skipped by operator");
            } finally {
                RetireSource(ref windowCts, windowSource);
                Session.WindowOpensAt = null;
            }
        }

        private async Task ResolveReferenceAsync(SpeckleTarget target, IProgress<ApplicationStatus> progress, CancellationToken targetToken) {
            if (!ReferenceStarService.NeedsReferenceLookup(target, runOptions.ReferenceQuery) && !ReferenceStarService.HasGaiaNumber(target.RefGaiaNum)) {
                Logger.Info("No reference star is wanted for " + TargetLabel.Of(target) + " (GetRef " + target.GetRef
                    + ", looking one up when the list names none is " + (runOptions.ReferenceQuery.LookupMissingReferenceStars ? "on" : "off") + ").");
                return;
            }
            if (target.ReferenceStar != null && target.ReferenceStar.RA2000 != 0) {
                Session.CurrentReference = target.ReferenceStar;
                return;
            }
            var attempt = 0;
            while (true) {
                try {
                    var result = await referenceStarService.ResolveAsync(target, targets, runOptions.ReferenceQuery, progress, targetToken).ConfigureAwait(false);
                    result.ApplyTo(target);
                    Session.CurrentReference = target.ReferenceStar != null && target.ReferenceStar.RA2000 != 0 ? target.ReferenceStar : null;
                    return;
                } catch (OperationCanceledException) {
                    throw;
                } catch (Exception ex) {
                    Logger.Error("Reference resolution for " + target.Name + " failed", ex);
                    var fullAuto = ComputeFullAuto();
                    if (fullAuto && runOptions.AutoSkipFailedReference) {
                        Session.SkipReference = true;
                        Notification.ShowWarning("Reference resolution for " + target.Name + " failed; reference leg skipped");
                        return;
                    }
                    if (!fullAuto) {
                        SetPhase(WorkflowPhase.Faulted);
                    }
                    var outcome = await WaitAtGateAsync(new GateRequest {
                        Kind = GateKind.Error,
                        Phase = WorkflowPhase.PickingTarget,
                        Title = "Reference resolution failed",
                        Detail = ex.Message,
                        Target = target,
                        IsSkippable = true,
                        Attempt = attempt,
                        Error = ex
                    }, fullAuto, targetToken).ConfigureAwait(false);
                    switch (outcome) {
                        case GateOutcome.Retry:
                        case GateOutcome.Confirm:
                            attempt++;
                            continue;
                        case GateOutcome.Skip:
                            Session.SkipReference = true;
                            return;
                        default:
                            CancelCurrentTarget("skipped-after-error");
                            targetToken.ThrowIfCancellationRequested();
                            throw new OperationCanceledException(targetToken);
                    }
                }
            }
        }

        private bool ShouldRunReferenceLeg(SpeckleTarget target) {
            return !Session.SkipReference && CanImageReference(target);
        }

        private static bool CanImageReference(SpeckleTarget target) {
            return target != null
                && target.ReferenceStar != null
                && target.ReferenceStar.RA2000 != 0;
        }

        private async Task RunLegAsync(SpeckleTarget target, TargetPlan plan, bool isReferenceLeg, IProgress<ApplicationStatus> progress, CancellationToken legToken) {
            Logger.Info("Leg starting: " + LegName(isReferenceLeg) + " for " + TargetLabel.Of(target) + " (title " + plan.Title
                + ", " + CapturePlan.Describe(plan.CaptureEntries) + ")");
            fringeAnalysis?.Reset();
            Session.ImagingReference = isReferenceLeg;
            Session.CurrentPlan = plan;
            Session.SessionTitle = plan.Title;
            Session.FrameCount = 0;
            Session.FrameTotal = plan.NExp > 0 ? plan.NExp : runOptions.DefaultExposures;
            Session.ActiveInputTarget = BuildInputTarget(plan);
            Session.Roi = new ObservableRectangle(0, 0, runOptions.RoiWidth, runOptions.RoiHeight);
            Session.RoiFrameWidth = 0d;
            Session.RoiFrameHeight = 0d;
            appliedFilter = null;
            calibratedFilter = null;
            calibratedExposure = 0;
            LoadCaptureEntries(plan);
            RefreshAvailableFilters();

            var templateIssue = templateValidationService.Inspect(target, plan);
            if (templateIssue != null) {
                Logger.Warning(templateIssue.Message);
            }

            var stagesRun = 0;
            do {
                var requested = StageRank(rewindTo?.Phase ?? WorkflowPhase.AwaitingSlewConfirmation);
                var startAt = Math.Min(requested, stagesRun + 1);
                if (startAt < requested) {
                    Logger.Info("Starting the " + LegName(isReferenceLeg) + " leg of " + TargetLabel.Of(target) + " at stage " + startAt
                        + " rather than stage " + requested + " because the earlier steps have not run for this leg yet");
                }
                rewindTo = null;
                Session.CaptureEntryNumber = 0;

                if (startAt <= 1) {
                    if (runOptions.DualCameraSetup) {
                        await ExecuteStepAsync(WorkflowPhase.SwitchingToWide, false, async ct => {
                            await lightPathService.SwitchAsync(LightPath.Wide, progress, ct).ConfigureAwait(false);
                            Session.CurrentLightPath = LightPath.Wide;
                        }, legToken).ConfigureAwait(false);
                    }

                    await RunSlewGateAsync(target, plan, isReferenceLeg, progress, legToken).ConfigureAwait(false);

                    if (runOptions.DualCameraSetup) {
                        await ExecuteStepAsync(WorkflowPhase.SwitchingToScience, false, async ct => {
                            await lightPathService.SwitchAsync(LightPath.Science, progress, ct).ConfigureAwait(false);
                            Session.CurrentLightPath = LightPath.Science;
                        }, legToken).ConfigureAwait(false);
                    }
                    stagesRun = Math.Max(stagesRun, 1);
                }

                if (rewindTo == null && startAt <= 2) {
                    await RunRoiPositioningAsync(target, plan, progress, legToken).ConfigureAwait(false);
                    stagesRun = Math.Max(stagesRun, 2);
                }
                if (rewindTo == null && startAt <= 3) {
                    await RunCalibrationAsync(progress, legToken).ConfigureAwait(false);
                    stagesRun = Math.Max(stagesRun, 3);
                }
                if (rewindTo == null) {
                    await RunImageGateAsync(target, plan, isReferenceLeg, progress, legToken).ConfigureAwait(false);
                    stagesRun = Math.Max(stagesRun, 4);
                }
                if (rewindTo != null) {
                    continue;
                }

                Session.SpeckleRun = NextSpeckleRun(target, isReferenceLeg);
                try {
                    await RunCaptureEntriesAsync(target, isReferenceLeg, progress, legToken).ConfigureAwait(false);
                } catch (OperationCanceledException) when (rewindTo != null && !legToken.IsCancellationRequested) {
                    Logger.Info("Capture of " + TargetLabel.Of(target) + " was interrupted by the operator");
                }
            } while (rewindTo != null);
        }

        private async Task RunSlewGateAsync(SpeckleTarget target, TargetPlan plan, bool isReferenceLeg, IProgress<ApplicationStatus> progress, CancellationToken legToken) {
            await WaitAtSafeBoundaryAsync(legToken).ConfigureAwait(false);
            await UseSlewFilterAsync(progress, legToken).ConfigureAwait(false);
            var fullAuto = ComputeFullAuto();
            SetPhase(WorkflowPhase.AwaitingSlewConfirmation);
            var request = new GateRequest {
                Kind = GateKind.SlewConfirmation,
                Phase = WorkflowPhase.AwaitingSlewConfirmation,
                Title = isReferenceLeg ? "Slew to reference" : "Slew to target",
                Target = target,
                Reference = isReferenceLeg ? target.ReferenceStar : null,
                Coordinates = plan.Coordinates,
                IsReference = isReferenceLeg,
                IsSkippable = isReferenceLeg
            };
            var outcome = await WaitAtGateWithPreviewAsync(request, fullAuto, RunCenteringPreviewSupervisorAsync, progress, legToken).ConfigureAwait(false);
            HandleConfirmationOutcome(outcome, isReferenceLeg, legToken);
            if (fullAuto) {
                await ExecuteStepAsync(WorkflowPhase.Slewing, false, async ct => {
                    var slewed = await telescopeMediator.SlewToCoordinatesAsync(plan.Coordinates, ct).ConfigureAwait(false);
                    if (!slewed) {
                        throw new Exception("Slew to " + (isReferenceLeg ? "reference" : "target") + " coordinates failed");
                    }
                }, legToken).ConfigureAwait(false);
            }
        }

        private async Task<GateOutcome> WaitAtGateWithPreviewAsync(GateRequest request, bool fullAuto,
                                                                   Func<IProgress<ApplicationStatus>, CancellationToken, Task> runSupervisor,
                                                                   IProgress<ApplicationStatus> progress, CancellationToken legToken) {
            if (fullAuto || cameraMediator.GetInfo()?.Connected != true) {
                return await WaitAtGateAsync(request, fullAuto, legToken).ConfigureAwait(false);
            }
            using (var supervisorCts = CancellationTokenSource.CreateLinkedTokenSource(legToken)) {
                var previewTask = runSupervisor(progress, supervisorCts.Token);
                try {
                    return await WaitAtGateAsync(request, false, legToken).ConfigureAwait(false);
                } finally {
                    supervisorCts.Cancel();
                    await previewTask.ConfigureAwait(false);
                }
            }
        }

        private async Task RunPreviewSupervisorAsync(Func<IProgress<ApplicationStatus>, CancellationToken, Task> runLoop,
                                                     string description, bool announceEachPause,
                                                     IProgress<ApplicationStatus> progress, CancellationToken ct) {
            try {
                while (!ct.IsCancellationRequested) {
                    await WaitAtSafeBoundaryAsync(ct).ConfigureAwait(false);
                    await WaitForLightPathSwitchAsync(ct).ConfigureAwait(false);
                    previewWillResume = false;
                    var previewSource = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    PublishSource(ref previewCts, previewSource);
                    var loopTask = runLoop(progress, previewSource.Token);
                    previewLoopTask = loopTask;
                    try {
                        await loopTask.ConfigureAwait(false);
                    } finally {
                        RetireSource(ref previewCts, previewSource);
                        previewLoopTask = null;
                    }
                    if (!previewWillResume) {
                        return;
                    }
                    if (announceEachPause) {
                        Logger.Info("Centering preview loop is stopped and will start again once the run continues");
                    }
                }
            } catch (OperationCanceledException) {
            } catch (Exception ex) {
                Logger.Error(description + " supervisor failed", ex);
            }
        }

        private Task RunCenteringPreviewSupervisorAsync(IProgress<ApplicationStatus> progress, CancellationToken ct) {
            return RunPreviewSupervisorAsync(RunPreviewLoopSafeAsync, "Preview", true, progress, ct);
        }

        private Task RunRoiPreviewSupervisorAsync(IProgress<ApplicationStatus> progress, CancellationToken ct) {
            return RunPreviewSupervisorAsync(RunRoiPreviewLoopSafeAsync, "Region of interest preview", false, progress, ct);
        }

        private async Task StopPreviewAsync(string reason) {
            if (previewCts == null) {
                return;
            }
            previewWillResume = true;
            Logger.Info(reason + ": cancelling the centering preview loop");
            CancelSource(ref previewCts);
            var task = previewLoopTask;
            if (task != null) {
                try {
                    await task.ConfigureAwait(false);
                } catch (OperationCanceledException) {
                } catch (Exception ex) {
                    Logger.Error("Centering preview loop did not stop cleanly", ex);
                }
            }
            Logger.Info(reason + ": centering preview loop stopped, no capture is running");
        }

        private double CenteringExposureTime() {
            var wanted = optionsProvider.Current?.CenteringExposureTime ?? 0;
            return wanted > 0 ? wanted : runOptions.PreviewExposureTime;
        }

        private async Task RunPreviewLoopSafeAsync(IProgress<ApplicationStatus> progress, CancellationToken ct) {
            var watch = Stopwatch.StartNew();
            try {
                Logger.Info("Centering preview loop starting at " + CenteringExposureTime() + " s per frame");
                await acquisitionService.RunPreviewLoopAsync(new PreviewLoopRequest {
                    GetExposureTime = CenteringExposureTime
                }, progress, ct).ConfigureAwait(false);
            } catch (OperationCanceledException) {
            } catch (Exception ex) {
                Logger.Error("Preview loop failed; the image on screen is no longer live", ex);
                Notification.ShowError("The centring view stopped after " + watch.Elapsed.TotalSeconds.ToString("F0")
                    + " s: " + ex.Message + ". The image on screen is not live.");
            } finally {
                Logger.Info("Centering preview loop ended after " + watch.ElapsedMilliseconds + " ms");
            }
        }

        private async Task RunRoiPositioningAsync(SpeckleTarget target, TargetPlan plan, IProgress<ApplicationStatus> progress, CancellationToken legToken) {
            var result = await ExecuteStepAsync(WorkflowPhase.PositioningRoi, false,
                ct => roiPositioningService.LocateTargetAsync(BuildRoiRequest(target, plan), progress, ct),
                null, legToken).ConfigureAwait(false);
            if (result != null) {
                ApplyRoiResult(target, result);
            }
        }

        private RoiPositionRequest BuildRoiRequest(SpeckleTarget target, TargetPlan plan) {
            return new RoiPositionRequest {
                Target = Session.ActiveInputTarget,
                SpeckleTarget = target,
                Title = plan.Title,
                RoiWidth = runOptions.RoiWidth,
                RoiHeight = runOptions.RoiHeight,
                PlatesolveFirst = runOptions.PlatesolveRoi,
                FallbackExposureTime = RoiFallbackExposureTime,
                CaptureBinning = runOptions.Binning?.X ?? 1
            };
        }

        private void ApplyRoiResult(SpeckleTarget target, RoiPositionResult result) {
            Session.RoiFrameWidth = result.FrameWidth;
            Session.RoiFrameHeight = result.FrameHeight;
            var roi = Session.Roi;
            if (roi != null) {
                if (result.RoiX.HasValue) {
                    roi.X = result.RoiX.Value;
                }
                if (result.RoiY.HasValue) {
                    roi.Y = result.RoiY.Value;
                }
                if (!result.RoiX.HasValue || !result.RoiY.HasValue) {
                    CentreRoiOnSensor(roi);
                }
            }
            if (result.Orientation.HasValue) {
                Session.Orientation = result.Orientation;
                target.Orientation = result.Orientation.Value;
            }
            if (result.ArcsecPerPix.HasValue) {
                Session.ArcsecPerPix = result.ArcsecPerPix;
                target.ArcsecPerPix = result.ArcsecPerPix.Value;
            }
            if (result.Note != null) {
                target.Note2 = result.Note;
            }
        }

        private void CentreRoiOnSensor(ObservableRectangle roi) {
            var camera = cameraMediator.GetInfo();
            if (camera == null || camera.XSize <= 0 || camera.YSize <= 0) {
                Logger.Warning("The region of interest could not be located and the sensor size is unknown; leaving it at "
                    + roi.X + "," + roi.Y);
                return;
            }
            roi.X = Math.Max(0d, Math.Round((camera.XSize - roi.Width) / 2d));
            roi.Y = Math.Max(0d, Math.Round((camera.YSize - roi.Height) / 2d));
            Logger.Warning("The region of interest could not be located from the image, so it is centred on the sensor at "
                + roi.X + "," + roi.Y + " for a " + roi.Width + "x" + roi.Height + " window. Whatever sits at the centre will be imaged.");
            Notification.ShowWarning("The target was not found in the frame. The region of interest is on the sensor centre, so check the preview before imaging.");
        }

        private async Task RunCalibrationAsync(IProgress<ApplicationStatus> progress, CancellationToken legToken) {
            Session.CalculatedExposureTime = await PrepareEntryAsync(PendingCaptureEntry(), true, progress, legToken).ConfigureAwait(false);
        }

        private async Task<double> PrepareEntryAsync(CaptureEntry entry, bool force, IProgress<ApplicationStatus> progress, CancellationToken legToken) {
            var filterName = entry?.Filter ?? string.Empty;
            var requested = entry?.ExposureTime ?? 0;
            var reusable = requested > 0 ? requested : ReusableCalibration(filterName);
            if (!force && reusable > 0 && !NeedsFilterChange(filterName)) {
                return reusable;
            }
            var exposureTime = await ExecuteStepAsync(WorkflowPhase.CalibratingExposure, true, async ct => {
                await ChangeFilterAsync(filterName, progress, ct).ConfigureAwait(false);
                if (reusable > 0) {
                    return reusable;
                }
                return await exposureCalibrationService.FindRoiExposureTimeAsync(new ExposureCalibrationRequest {
                    Roi = Session.Roi,
                    TargetAdu = ExposureCalibrationTargetAdu,
                    StepTime = ExposureCalibrationStepTime,
                    MaxTime = runOptions.ExposureTimeMax,
                    Gain = CameraGain,
                    Offset = CameraOffset,
                    Binning = runOptions.Binning,
                    ImageType = CaptureImageType,
                    BitDepth = runOptions.ReadCameraBitDepth(),
                    OnIteration = (count, time) => Session.CalculatedExposureTime = time
                }, progress, ct).ConfigureAwait(false);
            }, () => requested > 0 ? requested : runOptions.DefaultExposureTime, legToken).ConfigureAwait(false);
            if (requested <= 0) {
                calibratedFilter = filterName;
                calibratedExposure = exposureTime;
            }
            return exposureTime;
        }

        private double ReusableCalibration(string filterName) {
            return calibratedExposure > 0 && string.Equals(calibratedFilter, filterName, StringComparison.OrdinalIgnoreCase)
                ? calibratedExposure
                : 0;
        }

        private bool NeedsFilterChange(string filterName) {
            if (string.IsNullOrWhiteSpace(filterName)) {
                return false;
            }
            if (string.Equals(appliedFilter, filterName, StringComparison.OrdinalIgnoreCase)) {
                return false;
            }
            var selected = filterWheelMediator.GetInfo()?.SelectedFilter?.Name;
            return !string.Equals(selected, filterName, StringComparison.OrdinalIgnoreCase);
        }

        private async Task UseSlewFilterAsync(IProgress<ApplicationStatus> progress, CancellationToken ct) {
            var wanted = optionsProvider.Current?.SlewFilter;
            if (string.IsNullOrWhiteSpace(wanted) || !NeedsFilterChange(wanted)) {
                return;
            }
            try {
                Logger.Info("Changing to the slew filter " + wanted + " before the telescope is pointed");
                await ChangeFilterAsync(wanted, progress, ct).ConfigureAwait(false);
            } catch (OperationCanceledException) {
                throw;
            } catch (Exception ex) {
                Logger.Error("Could not change to the slew filter " + wanted + "; carrying on with the filter that is in", ex);
            }
        }

        private async Task ChangeFilterAsync(string filterName, IProgress<ApplicationStatus> progress, CancellationToken ct) {
            if (!NeedsFilterChange(filterName)) {
                return;
            }
            var filter = runOptions.ResolveFilter?.Invoke(filterName);
            if (filter == null) {
                ReportUnknownFilter(filterName);
                return;
            }
            if (filterWheelMediator.GetInfo()?.Connected != true) {
                Logger.Warning("Filter " + filterName + " was asked for but the filter wheel is not connected, so the frames are taken through"
                    + " whatever filter is in the light path now.");
                return;
            }
            await filterWheelMediator.ChangeFilter(filter, ct, progress).ConfigureAwait(false);
            appliedFilter = filterName;
            Logger.Info("Filter changed to " + filterName);
        }

        private readonly HashSet<string> unknownFiltersReported = new HashSet<string>(StringComparer.Ordinal);

        private void ReportUnknownFilter(string filterName) {
            var wheel = FilterWheelCatalog.Filters(profileService);
            var known = wheel == null || wheel.Count == 0
                ? "the filter wheel has no filters set up in this profile"
                : "this profile has " + string.Join(", ", wheel.Select(entry => entry.Name));
            Logger.Warning("There is no filter named " + filterName + " in the filter wheel, so the frames are taken through whatever filter is in"
                + " the light path now. Names must match exactly, including capitals: " + known + ".");
            if (unknownFiltersReported.Add(filterName)) {
                Notification.ShowWarning("No filter named " + filterName + " in this profile. Frames are being taken through whatever filter is"
                    + " in the light path. Check the spelling in Default filters or in the target list.");
            }
        }

        private async Task RunImageGateAsync(SpeckleTarget target, TargetPlan plan, bool isReferenceLeg, IProgress<ApplicationStatus> progress, CancellationToken legToken) {
            await WaitAtSafeBoundaryAsync(legToken).ConfigureAwait(false);
            var fullAuto = ComputeFullAuto();
            SetPhase(WorkflowPhase.AwaitingImageConfirmation);
            var request = new GateRequest {
                Kind = GateKind.ImageConfirmation,
                Phase = WorkflowPhase.AwaitingImageConfirmation,
                Title = isReferenceLeg ? "Image reference" : "Image target",
                Target = target,
                Reference = isReferenceLeg ? target.ReferenceStar : null,
                Coordinates = plan.Coordinates,
                IsReference = isReferenceLeg,
                IsSkippable = isReferenceLeg
            };
            GateOutcome outcome;
            try {
                outcome = await WaitAtGateWithPreviewAsync(request, fullAuto, RunRoiPreviewSupervisorAsync, progress, legToken).ConfigureAwait(false);
            } finally {
                await PutTriedFilterBackAsync(progress).ConfigureAwait(false);
            }
            HandleConfirmationOutcome(outcome, isReferenceLeg, legToken);
        }

        private async Task RunRoiPreviewLoopSafeAsync(IProgress<ApplicationStatus> progress, CancellationToken ct) {
            var watch = Stopwatch.StartNew();
            Logger.Info("Region of interest preview starting at " + PendingPreviewExposureTime()
                + " s per frame; these frames are shown to the operator and are not saved");
            try {
                await acquisitionService.RunPreviewLoopAsync(new PreviewLoopRequest {
                    GetExposureTime = PendingPreviewExposureTime,
                    EnableSubSample = true,
                    SubSampleRectangle = Session.Roi,
                    Binning = runOptions.Binning,
                    Gain = CameraGain,
                    Offset = CameraOffset
                }, progress, ct).ConfigureAwait(false);
            } catch (OperationCanceledException) {
            } catch (Exception ex) {
                Logger.Error("Region of interest preview loop failed", ex);
            } finally {
                Logger.Info("Region of interest preview ended after " + watch.ElapsedMilliseconds + " ms with nothing saved");
            }
        }

        public async Task TryCaptureEntryAsync(CaptureEntry entry, CancellationToken ct) {
            if (entry == null || Gate.Pending?.Kind != GateKind.ImageConfirmation) {
                return;
            }
            if (!filterWasTried) {
                var inTheWheel = filterWheelMediator.GetInfo()?.SelectedFilter?.Name;
                filterBeforeTry = string.IsNullOrWhiteSpace(inTheWheel) ? appliedFilter : inTheWheel;
                filterWasTried = true;
            }
            Logger.Info("Operator is trying " + entry + " in the preview; the capture still starts on "
                + DescribeFilter(PendingCaptureEntry()?.Filter));
            triedEntry = entry;
            try {
                await ChangeFilterAsync(entry.Filter, null, ct).ConfigureAwait(false);
            } catch (OperationCanceledException) {
                throw;
            } catch (Exception ex) {
                Logger.Error("Could not change to " + DescribeFilter(entry.Filter) + " for the preview", ex);
                Notification.ShowError("Could not change to " + DescribeFilter(entry.Filter) + ": " + ex.Message);
            }
        }

        private async Task PutTriedFilterBackAsync(IProgress<ApplicationStatus> progress) {
            if (!filterWasTried) {
                return;
            }
            var wanted = string.IsNullOrWhiteSpace(filterBeforeTry) ? PendingCaptureEntry()?.Filter : filterBeforeTry;
            filterWasTried = false;
            filterBeforeTry = null;
            triedEntry = null;
            if (string.IsNullOrWhiteSpace(wanted)) {
                return;
            }
            try {
                appliedFilter = null;
                Logger.Info("Putting the filter wheel back to " + wanted + " after the operator tried other filters");
                await ChangeFilterAsync(wanted, progress, CancellationToken.None).ConfigureAwait(false);
            } catch (Exception ex) {
                Logger.Error("Could not put the filter wheel back to " + wanted, ex);
                Notification.ShowError("The filter wheel could not be put back to " + wanted + ": " + ex.Message);
            }
        }

        private static string DescribeFilter(string filter) {
            return string.IsNullOrWhiteSpace(filter) ? "no filter" : filter;
        }

        private double PendingPreviewExposureTime() {
            if (triedEntry != null && triedEntry.ExposureTime > 0) {
                return triedEntry.ExposureTime * ExposureTimeMultiplier;
            }
            var entry = PendingCaptureEntry();
            if (entry != null && entry.ExposureTime > 0) {
                return entry.ExposureTime * ExposureTimeMultiplier;
            }
            if (Session.CalculatedExposureTime > 0) {
                return Session.CalculatedExposureTime * ExposureTimeMultiplier;
            }
            return runOptions.DefaultExposureTime;
        }

        private int NextSpeckleRun(SpeckleTarget target, bool isReferenceLeg) {
            var key = (target?.TargetId ?? 0) + (isReferenceLeg ? "r" : "t");
            speckleRuns.TryGetValue(key, out var previous);
            var run = previous + 1;
            speckleRuns[key] = run;
            if (run > 1) {
                Logger.Info("Capture of " + TargetLabel.Of(target) + " is starting again; frames will be written to run " + run);
            }
            return run;
        }

        private async Task RunCaptureEntriesAsync(SpeckleTarget target, bool isReferenceLeg, IProgress<ApplicationStatus> progress, CancellationToken legToken) {
            var headers = isReferenceLeg
                ? target.ReferenceStar?.GenericHeaders()
                : target.GenericHeaders(runOptions.SaveCsvToFitsHeader);
            var index = 0;
            try {
                while (true) {
                    await WaitAtSafeBoundaryAsync(legToken).ConfigureAwait(false);
                    await WaitForLightPathSwitchAsync(legToken).ConfigureAwait(false);
                    legToken.ThrowIfCancellationRequested();
                    if (runOptions.DualCameraSetup && Session.CurrentLightPath != LightPath.Science) {
                        Logger.Warning("The light path is not on the science camera, so it is being put back before capturing");
                        await ExecuteStepAsync(WorkflowPhase.SwitchingToScience, false, async ct => {
                            await lightPathService.SwitchAsync(LightPath.Science, progress, ct).ConfigureAwait(false);
                            Session.CurrentLightPath = LightPath.Science;
                        }, legToken).ConfigureAwait(false);
                    }
                    CaptureEntry entry;
                    int total;
                    lock (captureSync) {
                        total = Session.CaptureEntries.Count;
                        if (index >= total) {
                            break;
                        }
                        entry = Session.CaptureEntries[index];
                        capturePlanLocked = true;
                    }
                    Session.CapturePlanLocked = true;
                    Session.CaptureEntryCount = total;
                    Session.FrameCount = 0;
                    Session.CaptureEntryNumber = index + 1;
                    Session.ActiveCaptureEntry = entry;
                    try {
                        await RunCaptureEntryAsync(entry, index + 1, total, headers, progress, legToken).ConfigureAwait(false);
                    } finally {
                        SetCapturePlanLocked(false);
                    }
                    if (rewindTo != null) {
                        Logger.Info("The operator jumped back to " + rewindTo.Phase + ", so the remaining capture entries are not run");
                        return;
                    }
                    index++;
                }
            } finally {
                Session.ActiveCaptureEntry = null;
                Session.ActiveFilter = null;
                Session.ActiveExposureTime = 0;
            }
        }

        private async Task RunCaptureEntryAsync(CaptureEntry entry, int number, int total, List<IGenericMetaDataHeader> headers, IProgress<ApplicationStatus> progress, CancellationToken legToken) {
            var resolved = await PrepareEntryAsync(entry, false, progress, legToken).ConfigureAwait(false);
            Session.CalculatedExposureTime = resolved;
            var totalCount = entry.FrameCount > 0 ? entry.FrameCount : runOptions.DefaultExposures;
            var exposureTime = (resolved > 0 ? resolved : runOptions.DefaultExposureTime) * ExposureTimeMultiplier;
            Session.ActiveExposureTime = exposureTime;
            Session.ActiveFilter = filterWheelMediator.GetInfo()?.SelectedFilter?.Name ?? entry.Filter;
            var framesBeforeEntry = Session.TotalFrames;
            Session.FrameCount = 0;
            Session.FrameTotal = totalCount;
            Logger.Info("Capture entry " + number + " of " + total + " starting (" + entry
                + ", exposure used " + exposureTime + " s, frames " + totalCount + ")");
            var videoWatch = new Stopwatch();
            var request = new RoiSeriesRequest {
                Mode = runOptions.ReadVideoExposuresMode?.Invoke() ?? runOptions.VideoExposuresMode,
                ExposureTime = exposureTime,
                TotalExposureCount = totalCount,
                Gain = CameraGain,
                Offset = CameraOffset,
                Binning = runOptions.Binning,
                ImageType = CaptureImageType,
                EnableSubSample = true,
                SubSampleRectangle = Session.Roi,
                Target = Session.ActiveInputTarget,
                Title = Session.SessionTitle,
                SpeckleRun = Session.SpeckleRun,
                GenericHeaders = headers,
                Pause = pause,
                IsReference = Session.ImagingReference,
                ArcsecPerPixel = Session.ArcsecPerPix ?? 0,
                ScaleSource = Session.ArcsecPerPix.HasValue ? "plate solve" : null,
                OnFrame = count => {
                    Session.FrameCount = count;
                    Session.TotalFrames = framesBeforeEntry + count;
                    var elapsed = videoWatch.Elapsed.TotalSeconds;
                    var fps = elapsed > 0 ? count / elapsed : 0;
                    Session.Fps = fps;
                    VideoExposuresProgress?.Invoke(this, new RoiSeriesProgress { FrameNumber = count, Total = totalCount, Fps = fps });
                }
            };
            var attemptsMade = 0;
            var result = await ExecuteStepAsync<ExposureRunResult>(WorkflowPhase.RunningVideoExposures, false, ct => {
                acquisitionService.EnsureFramesCanBeSaved();
                if (attemptsMade > 0) {
                    request.SpeckleRun = NextSpeckleRun(Session.CurrentTarget, Session.ImagingReference);
                    Session.SpeckleRun = request.SpeckleRun;
                    Logger.Info("Retrying the video exposures under speckle run " + request.SpeckleRun
                        + " so the frames already written are not mixed with the retry");
                }
                attemptsMade++;
                videoWatch.Restart();
                return acquisitionService.RunRoiSeriesAsync(request, progress, ct);
            }, () => null, legToken).ConfigureAwait(false);
            if (result != null) {
                Session.Fps = result.Fps;
                Session.FrameCount = result.FramesCaptured;
                Session.TotalFrames = framesBeforeEntry + result.FramesCaptured;
                if (result.PendingSaves != null) {
                    try {
                        await result.PendingSaves.ConfigureAwait(false);
                    } catch (OperationCanceledException) {
                        throw;
                    } catch (Exception ex) {
                        Logger.Error("One or more frames failed to save for " + Session.CurrentTarget?.Name, ex);
                        Notification.ShowWarning("Some frames failed to save for " + Session.CurrentTarget?.Name);
                    }
                }
            } else {
                Session.TotalFrames = framesBeforeEntry + Session.FrameCount;
            }
            Logger.Info("Capture entry " + number + " of " + total + " finished in " + videoWatch.ElapsedMilliseconds
                + " ms (" + Session.FrameCount + " frames, " + Session.Fps + " fps)");
            if (Session.FrameCount < totalCount) {
                Logger.Warning("Capture entry " + number + " of " + total + " for " + TargetLabel.Of(Session.CurrentTarget)
                    + " stopped after " + Session.FrameCount + " of " + totalCount
                    + " frames, and the cycle is still counted as complete in the target list");
            }
        }

        private void LoadCaptureEntries(TargetPlan plan) {
            SetCapturePlanLocked(true);
            var entries = Session.CaptureEntries;
            var wanted = CapturePlan.Clone(plan?.CaptureEntries);
            if (wanted.Count == 0) {
                wanted.Add(new CaptureEntry());
            }
            entries.ResetTo(wanted);
            Session.ActiveCaptureEntry = null;
            Session.CaptureEntryNumber = 0;
            Session.CaptureEntryCount = entries.Count;
            SetCapturePlanLocked(false);
            CapturePlanChanged?.Invoke(this, EventArgs.Empty);
        }

        private void SetCapturePlanLocked(bool locked) {
            lock (captureSync) {
                capturePlanLocked = locked;
            }
            Session.CapturePlanLocked = locked;
        }

        private CaptureEntry PendingCaptureEntry() {
            lock (captureSync) {
                var entries = Session.CaptureEntries;
                return entries.Count == 0 ? null : entries[Math.Clamp(Session.CaptureEntryNumber, 0, entries.Count - 1)];
            }
        }

        private bool EditCaptureEntry(CaptureEntry entry, string description, Action apply) {
            if (entry == null) {
                return false;
            }
            return EditCapturePlan("set " + description + " on " + entry, () => {
                apply();
                return true;
            });
        }

        private bool EditCapturePlan(string description, Func<bool> edit) {
            lock (captureSync) {
                if (capturePlanLocked) {
                    Logger.Info("Capture plan edit ignored while a capture series is running: " + description);
                    return false;
                }
            }
            if (!edit()) {
                Logger.Info("Capture plan edit had no effect: " + description);
                return false;
            }
            Session.CaptureEntryCount = Session.CaptureEntries.Count;
            Logger.Info("Capture plan edit: " + description + " -> " + CapturePlan.Describe(Session.CaptureEntries));
            CapturePlanChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }

        private void HandleConfirmationOutcome(GateOutcome outcome, bool isReferenceLeg, CancellationToken legToken) {
            switch (outcome) {
                case GateOutcome.Confirm:
                case GateOutcome.Retry:
                    return;
                case GateOutcome.Skip:
                    if (isReferenceLeg) {
                        SkipReference();
                        legToken.ThrowIfCancellationRequested();
                        throw new OperationCanceledException(legToken);
                    }
                    return;
                default:
                    CancelCurrentTarget("skipped-by-operator");
                    legToken.ThrowIfCancellationRequested();
                    throw new OperationCanceledException(legToken);
            }
        }

        private Task ExecuteStepAsync(WorkflowPhase phase, bool skippable, Func<CancellationToken, Task> work, CancellationToken legToken) {
            return ExecuteStepAsync<object>(phase, skippable, async ct => {
                await work(ct).ConfigureAwait(false);
                return null;
            }, null, legToken);
        }

        private async Task<T> ExecuteStepAsync<T>(WorkflowPhase phase, bool skippable, Func<CancellationToken, Task<T>> work, Func<T> skipResult, CancellationToken legToken) {
            var attempt = 0;
            while (true) {
                await WaitAtSafeBoundaryAsync(legToken).ConfigureAwait(false);
                SetPhase(phase);
                var watch = Stopwatch.StartNew();
                Logger.Info("Step " + phase + " starting (target " + TargetLabel.Of(Session.CurrentTarget)
                    + ", leg " + LegName(Session.ImagingReference) + ", attempt " + attempt + ")");
                var phaseSource = CancellationTokenSource.CreateLinkedTokenSource(legToken);
                PublishSource(ref phaseCts, phaseSource);
                {
                    try {
                        var result = await work(phaseSource.Token).ConfigureAwait(false);
                        Logger.Info("Step " + phase + " finished in " + watch.ElapsedMilliseconds + " ms");
                        return result;
                    } catch (OperationCanceledException) when (phaseSource.IsCancellationRequested && !legToken.IsCancellationRequested) {
                        Logger.Info("Step " + phase + " ended early by operator after " + watch.ElapsedMilliseconds + " ms");
                        return skipResult != null ? skipResult() : default;
                    } catch (OperationCanceledException) {
                        Logger.Info("Step " + phase + " cancelled after " + watch.ElapsedMilliseconds + " ms");
                        throw;
                    } catch (Exception ex) {
                        Logger.Error("Step " + phase + " failed after " + watch.ElapsedMilliseconds + " ms", ex);
                        var fullAuto = ComputeFullAuto();
                        if (!fullAuto) {
                            SetPhase(WorkflowPhase.Faulted);
                        }
                        var outcome = await WaitAtGateAsync(new GateRequest {
                            Kind = GateKind.Error,
                            Phase = phase,
                            Title = FailureTitleFor(phase),
                            Detail = ex.Message,
                            Target = Session.CurrentTarget,
                            Reference = Session.ImagingReference ? Session.CurrentTarget?.ReferenceStar : null,
                            IsReference = Session.ImagingReference,
                            IsSkippable = skippable,
                            Attempt = attempt,
                            Error = ex
                        }, fullAuto, legToken).ConfigureAwait(false);
                        switch (outcome) {
                            case GateOutcome.Retry:
                            case GateOutcome.Confirm:
                                attempt++;
                                continue;
                            case GateOutcome.Skip when skippable:
                                return skipResult != null ? skipResult() : default;
                            default:
                                CancelCurrentTarget("skipped-after-error");
                                legToken.ThrowIfCancellationRequested();
                                throw new OperationCanceledException(legToken);
                        }
                    } finally {
                        RetireSource(ref phaseCts, phaseSource);
                    }
                }
            }
        }

        private async Task WaitAtSafeBoundaryAsync(CancellationToken ct) {
            if (!pause.IsPauseRequested) {
                return;
            }
            await pause.WaitWhilePausedAsync(ct).ConfigureAwait(false);
        }

        private bool ComputeFullAuto() {
            if (runOptions?.ReadAutomaticallyRun?.Invoke() != true) {
                return false;
            }
            var info = telescopeMediator.GetInfo();
            if (info == null || !info.Connected || !info.CanSlew || info.AtPark) {
                return false;
            }
            return !string.Equals(info.DeviceId, ManualTelescope.DeviceId, StringComparison.Ordinal);
        }

        private InputTarget BuildInputTarget(TargetPlan plan) {
            if (runOptions.InputTargetFactory != null) {
                return runOptions.InputTargetFactory(plan);
            }
            var input = new InputTarget(Angle.ByDegree(runOptions.Latitude), Angle.ByDegree(runOptions.Longitude), runOptions.Horizon) {
                TargetName = plan.TargetName
            };
            if (plan.Coordinates != null) {
                input.InputCoordinates = new InputCoordinates { Coordinates = plan.Coordinates };
            }
            return input;
        }

        private Task SaveSnapshotAsync() {
            var directory = runOptions.SnapshotDirectory;
            if (string.IsNullOrWhiteSpace(directory)) {
                return Task.CompletedTask;
            }
            return targetListService.SaveSnapshotAsync(targets, directory);
        }

        private async Task SaveSnapshotSafeAsync() {
            try {
                await SaveSnapshotAsync().ConfigureAwait(false);
            } catch (OperationCanceledException) {
                throw;
            } catch (Exception ex) {
                Logger.Error("Failed to write the target list snapshot", ex);
            }
        }

        private void CancelCurrentTarget(string note) {
            CancellationTokenSource source;
            lock (cancellationSync) {
                source = targetCts;
            }
            if (source == null) {
                Logger.Info("Skip requested between targets, so there is nothing to skip");
                return;
            }
            var target = Session.CurrentTarget;
            if (target != null) {
                if (!string.IsNullOrEmpty(note)) {
                    target.Note2 = note;
                }
                target.ImageTarget = false;
            }
            CancelSource(ref targetCts);
        }

        private void SetPhase(WorkflowPhase phase) {
            var previous = Session.Phase;
            Logger.Info("Phase " + previous + " -> " + phase + " (target " + TargetLabel.Of(Session.CurrentTarget)
                + ", leg " + LegName(Session.ImagingReference) + ")");
            Session.Phase = phase;
            PhaseChanged?.Invoke(this, new WorkflowPhaseChangedEventArgs {
                Phase = phase,
                IsReferenceLeg = Session.ImagingReference,
                Plan = Session.CurrentPlan
            });
        }

        private async Task<GateOutcome> WaitAtGateAsync(GateRequest request, bool fullAuto, CancellationToken ct) {
            Logger.Info("Gate armed: " + request.Kind + " - " + request.Title + " (target " + TargetLabel.Of(request.Target)
                + ", leg " + LegName(request.IsReference) + ", skippable " + request.IsSkippable
                + ", attempt " + request.Attempt + ", fullAuto " + fullAuto + ")");
            var watch = Stopwatch.StartNew();
            try {
                var outcome = await Gate.WaitAsync(request, fullAuto, ct).ConfigureAwait(false);
                Logger.Info("Gate resolved: " + request.Kind + " - " + request.Title + " -> " + outcome + " after " + watch.ElapsedMilliseconds + " ms");
                return outcome;
            } catch (OperationCanceledException) {
                Logger.Info("Gate cancelled: " + request.Kind + " - " + request.Title + " after " + watch.ElapsedMilliseconds + " ms");
                throw;
            }
        }

        private static string FailureTitleFor(WorkflowPhase phase) {
            switch (phase) {
                case WorkflowPhase.PickingTarget:
                    return "Could not pick the next target";
                case WorkflowPhase.SwitchingToWide:
                    return "Could not switch to the wide field camera";
                case WorkflowPhase.SwitchingToScience:
                    return "Could not switch to the science camera";
                case WorkflowPhase.Slewing:
                case WorkflowPhase.AwaitingSlewConfirmation:
                    return "Could not point the telescope";
                case WorkflowPhase.Centering:
                    return "Could not centre the target";
                case WorkflowPhase.PositioningRoi:
                    return "Could not place the region of interest";
                case WorkflowPhase.CalibratingExposure:
                    return "Could not work out the exposure time";
                case WorkflowPhase.RunningVideoExposures:
                    return "The video exposures stopped early";
                default:
                    return "Something went wrong";
            }
        }

        private static string LegName(bool isReference) {
            return isReference ? "reference" : "target";
        }

        private void PublishSource(ref CancellationTokenSource field, CancellationTokenSource source) {
            lock (cancellationSync) {
                field = source;
            }
        }

        private void RetireSource(ref CancellationTokenSource field, CancellationTokenSource source) {
            lock (cancellationSync) {
                if (ReferenceEquals(field, source)) {
                    field = null;
                }
                source.Dispose();
            }
        }

        private void CancelSource(ref CancellationTokenSource field) {
            CancellationTokenSource source;
            lock (cancellationSync) {
                source = field;
            }
            try {
                source?.Cancel();
            } catch (ObjectDisposedException) {
            }
        }

        private static TaskCompletionSource<bool> CreateCompletion(bool completed) {
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (completed) {
                tcs.TrySetResult(true);
            }
            return tcs;
        }

        private sealed class PhaseRewind {
            public WorkflowPhase Phase { get; init; }
        }

        private sealed class LegJump {
            public WorkflowPhase Phase { get; init; }
            public bool ToReference { get; init; }
        }
    }
}
