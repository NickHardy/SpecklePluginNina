using CsvHelper;
using Microsoft.Win32;
using NINA.Astrometry;
using NINA.Core.Model;
using NINA.Core.Model.Equipment;
using NINA.Core.Utility;
using NINA.Core.Utility.Notification;
using NINA.Equipment.Equipment.MyCamera;
using NINA.Equipment.Equipment.MyFocuser;
using NINA.Equipment.Equipment.MyTelescope;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Equipment.Interfaces.ViewModel;
using NINA.Equipment.Model;
using NINA.Image.Interfaces;
using NINA.Plugin.Speckle.Dockables.Kepler;
using NINA.Plugin.Speckle.Imaging;
using NINA.Plugin.Speckle.ManualMount;
using NINA.Plugin.Speckle.Model;
using NINA.Plugin.Speckle.Services;
using NINA.Plugin.Speckle.Workflow;
using NINA.Profile.Interfaces;
using NINA.Sequencer.Interfaces.Mediator;
using NINA.WPF.Base.Interfaces.Mediator;
using NINA.WPF.Base.ViewModel;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Media;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using System.Windows.Media.Imaging;
using PointCommand = GalaSoft.MvvmLight.Command.RelayCommand<System.Windows.Point>;
using RelayCommand = GalaSoft.MvvmLight.Command.RelayCommand;
using StringCommand = GalaSoft.MvvmLight.Command.RelayCommand<string>;
using StarCommand = GalaSoft.MvvmLight.Command.RelayCommand<NINA.Plugin.Speckle.Model.ReferenceStar>;
using StepCommand = GalaSoft.MvvmLight.Command.RelayCommand<NINA.Plugin.Speckle.Dockables.ChainStepVM>;
using TargetCommand = GalaSoft.MvvmLight.Command.RelayCommand<NINA.Plugin.Speckle.Model.SpeckleTarget>;
using EntryCommand = GalaSoft.MvvmLight.Command.RelayCommand<NINA.Plugin.Speckle.Services.CaptureEntry>;

namespace NINA.Plugin.Speckle.Dockables {

    [Export(typeof(IDockableVM))]
    public class SpeckleDockableVM : DockableVM, ICameraConsumer, ITelescopeConsumer, IFocuserConsumer {
        private readonly ICameraMediator cameraMediator;
        private readonly ITelescopeMediator telescopeMediator;
        private readonly IFilterWheelMediator filterWheelMediator;
        private readonly IFocuserMediator focuserMediator;
        private readonly IImagingMediator imagingMediator;
        private readonly IApplicationStatusMediator applicationStatusMediator;
        private readonly ISequenceMediator sequenceMediator;
        private readonly ISpeckleWorkflowCoordinator coordinator;
        private readonly ISpeckleOptionsProvider optionsProvider;
        private readonly IFringeAnalysisService fringeAnalysis;
        private static readonly TimeSpan LightPathSwitchTimeout = TimeSpan.FromMinutes(6);
        private static readonly TimeSpan StopConfirmWindow = TimeSpan.FromSeconds(5);
        private readonly CameraCoolingService cooling;
        private readonly ITargetListService targetListService;
        private readonly IProgress<ApplicationStatus> progress;

        private CameraInfo cameraInfo;
        private TelescopeInfo telescopeInfo;
        private CancellationTokenSource runCts;
        private CancellationTokenSource focusCts;
        private Task focusTask;
        private bool isFocusing;
        private ApplicationStatus status;
        private BitmapSource preparedImage;
        private int zoomFitToken;
        private bool isBenchmarking;
        private bool gateCardTookRoom;
        private bool isRunning;
        private bool isDrawerOpen;
        private bool isMoreOpen;
        private bool isKeplerOpen;
        private bool stopArmed;
        private DispatcherTimer stopArmTimer;
        private bool isLoadingList;
        private bool isConnectingEquipment;
        private bool cameraReleasedForHold;
        private bool isSwitchingLightPath;
        private string loadingListName;
        private bool runEnded;
        private double slewDeltaArcmin = double.NaN;
        private long imagePreparedCount;
        private ushort[] rawPixels;
        private int rawWidth;
        private int rawHeight;
        private string pixelReadout;
        private FringeAnalysisResult fringeResult;
        private ICollectionView targetsView;
        private string targetSearch = string.Empty;
        private bool addStarOpen;
        private string newStarName = string.Empty;
        private string newStarRa = string.Empty;
        private string newStarDec = string.Empty;
        private int focuserPosition;
        private bool focuserConnected;
        private bool gateCardCollapsed;
        private CancellationTokenSource demoCts;
        private bool fringeDemoActive;
        private const int StatusLineRefreshMs = 200;
        private readonly Stopwatch statusClock = Stopwatch.StartNew();
        private bool showCompass;
        private readonly Dictionary<SpeckleTarget, string[]> templateSnapshot = new Dictionary<SpeckleTarget, string[]>();

        [ImportingConstructor]
        public SpeckleDockableVM(
            IProfileService profileService,
            ICameraMediator cameraMediator,
            ITelescopeMediator telescopeMediator,
            IFilterWheelMediator filterWheelMediator,
            IFocuserMediator focuserMediator,
            IImagingMediator imagingMediator,
            IApplicationStatusMediator applicationStatusMediator,
            ISequenceMediator sequenceMediator,
            ISpeckleWorkflowCoordinator coordinator,
            ISpeckleOptionsProvider optionsProvider,
            IDomeMediator domeMediator,
            IFringeAnalysisService fringeAnalysis,
            ITargetListService targetListService) : base(profileService) {
            this.cameraMediator = cameraMediator;
            this.telescopeMediator = telescopeMediator;
            this.filterWheelMediator = filterWheelMediator;
            this.focuserMediator = focuserMediator;
            this.imagingMediator = imagingMediator;
            this.applicationStatusMediator = applicationStatusMediator;
            this.sequenceMediator = sequenceMediator;
            this.coordinator = coordinator;
            this.optionsProvider = optionsProvider;
            this.fringeAnalysis = fringeAnalysis;
            this.targetListService = targetListService;

            Title = "Speckle";
            ImageGeometry = SpeckleIcons.PanelIcon;

            progress = new Progress<ApplicationStatus>(status => Status = status);

            Targets = new AsyncObservableCollection<SpeckleTarget>();
            UpNext = new AsyncObservableCollection<SpeckleTarget>();
            LoadedLists = new AsyncObservableCollection<LoadedListVM>();
            Templates = new ObservableCollection<string>();
            TargetSteps = new ObservableCollection<ChainStepVM>();
            ReferenceSteps = new ObservableCollection<ChainStepVM>();
            BuildChains();

            Kepler = new KeplerSkyVM(profileService, coordinator, optionsProvider, telescopeMediator, domeMediator, ImageNext);

            LoadListCommand = new RelayCommand(() => { LogClick("Load list"); _ = LoadListAsync(null); }, () => ListToolsEnabled);
            RemoveListCommand = new StringCommand(RemoveList, path => ListToolsEnabled);
            AddStarCommand = new RelayCommand(AddStar, () => CanAddStar);
            PickBrightStarCommand = new StarCommand(PickBrightStar);
            ExportCsvCommand = new RelayCommand(ExportCsv, () => !IsLoadingList && Targets.Count > 0);
            PrimaryActionCommand = new RelayCommand(PrimaryAction, CanPrimaryAction);
            PauseResumeCommand = new RelayCommand(PauseResume, () => IsRunning);
            StopRunCommand = new RelayCommand(StopRun, () => StopRunVisible);
            SkipTargetCommand = new RelayCommand(SkipTarget, () => IsRunning && Session.CurrentTarget != null);
            SkipReferenceCommand = new RelayCommand(ToggleSkipReference, () => CanToggleSkipReference);
            SkipStepCommand = new RelayCommand(SkipStep, () => CanSkipStep);
            JumpToStageCommand = new StepCommand(JumpToStage, step => IsRunning && step != null);
            SlewWithMountCommand = new RelayCommand(() => { LogClick("Slew with mount"); _ = SlewWithMountAsync(); }, CanSlewWithMount);
            SwitchWidePathCommand = new RelayCommand(() => { LogClick("Light path wide"); _ = SwitchLightPathAsync(LightPath.Wide); }, () => CanSwitchLightPath);
            SwitchSciencePathCommand = new RelayCommand(() => { LogClick("Light path science"); _ = SwitchLightPathAsync(LightPath.Science); }, () => CanSwitchLightPath);
            RemoveFromQueueCommand = new TargetCommand(RemoveFromQueue);
            ToggleDoneCommand = new TargetCommand(ToggleDone);
            QueueNextCommand = new TargetCommand(ImageNext);
            PixelReadoutStartCommand = new PointCommand(SamplePixel);
            PixelReadoutMoveCommand = new PointCommand(SamplePixel);
            PixelReadoutEndCommand = new RelayCommand(() => PixelReadout = null);
            AddCaptureEntryCommand = new RelayCommand(AddCaptureEntry, () => CapturePlanEditable);
            RemoveCaptureEntryCommand = new EntryCommand(RemoveCaptureEntry, entry => CanRemoveCaptureEntry);
            TryCaptureEntryCommand = new EntryCommand(TryCaptureEntry, entry => CapturePlanEditable);
            BenchmarkRequest.Requested += OnBenchmarkRequested;
            PreparedFrameRelay.Published += OnPreparedFramePublished;
            ResetFringesCommand = new RelayCommand(ResetFringes);
            SetFringeModeCommand = new StringCommand(SetFringeMode);
            FringeDemoCommand = new RelayCommand(ToggleFringeDemo);

            if (fringeAnalysis != null) {
                fringeAnalysis.PaneMode = (FringePaneMode)optionsProvider.Current.FringePaneMode;
                fringeResult = fringeAnalysis.Latest;
                fringeAnalysis.Updated += OnFringeUpdated;
            }
            FringeDemoRequest.Requested += OnFringeDemoRequested;

            coordinator.PhaseChanged += OnPhaseChanged;
            coordinator.PendingChanged += OnPendingChanged;
            coordinator.VideoExposuresProgress += OnVideoExposuresProgress;
            coordinator.TargetsChanged += OnTargetsChanged;
            coordinator.QueueChanged += OnQueueChanged;
            Session.PropertyChanged += OnSessionPropertyChanged;
            imagingMediator.ImagePrepared += OnImagePrepared;

            cameraMediator.RegisterConsumer(this);
            telescopeMediator.RegisterConsumer(this);
            focuserMediator.RegisterConsumer(this);
            cooling = new CameraCoolingService(profileService, cameraMediator);
            cooling.Start();
            Logger.Info("Speckle panel created and subscribed to ImagePrepared");
        }

        public override bool IsTool => true;

        public override string ContentId => "NINA.Plugin.Speckle.SpeckleDockableVM";

        public SpeckleWorkflowSession Session => coordinator.Session;

        public AsyncObservableCollection<SpeckleTarget> Targets { get; }

        public AsyncObservableCollection<SpeckleTarget> UpNext { get; }

        public AsyncObservableCollection<LoadedListVM> LoadedLists { get; }

        public ICollectionView TargetsView {
            get {
                if (targetsView == null) {
                    targetsView = CollectionViewSource.GetDefaultView(Targets);
                    targetsView.Filter = MatchesSearch;
                }
                return targetsView;
            }
        }

        public string TargetSearch {
            get => targetSearch;
            set {
                var wanted = value ?? string.Empty;
                if (string.Equals(targetSearch, wanted, StringComparison.Ordinal)) {
                    return;
                }
                targetSearch = wanted;
                targetsView?.Refresh();
                Logger.Info("UI: plan search '" + targetSearch + "' matches " + Targets.Count(MatchesSearch) + " of " + Targets.Count + " targets");
                RaisePropertyChanged();
                RaisePropertyChanged(nameof(TargetSearchEmpty));
            }
        }

        public bool TargetSearchEmpty => string.IsNullOrEmpty(targetSearch);

        private bool MatchesSearch(SpeckleTarget target) {
            return MatchesSearch((object)target);
        }

        private bool MatchesSearch(object item) {
            if (string.IsNullOrWhiteSpace(targetSearch)) {
                return true;
            }
            var target = item as SpeckleTarget;
            if (target == null) {
                return false;
            }
            var wanted = targetSearch.Trim();
            return Contains(target.Name1, wanted) || Contains(target.Name2, wanted);
        }

        private static bool Contains(string text, string wanted) {
            return !string.IsNullOrEmpty(text) && text.IndexOf(wanted, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public ObservableCollection<string> Templates { get; }

        public ObservableCollection<ChainStepVM> TargetSteps { get; }

        public ObservableCollection<ChainStepVM> ReferenceSteps { get; }

        public KeplerSkyVM Kepler { get; }

        public ICommand LoadListCommand { get; }

        public ICommand RemoveListCommand { get; }

        public ICommand AddStarCommand { get; }

        public ICommand PickBrightStarCommand { get; }
        public ICommand ExportCsvCommand { get; }
        public ICommand PrimaryActionCommand { get; }
        public ICommand PauseResumeCommand { get; }
        public ICommand StopRunCommand { get; }
        public ICommand SkipTargetCommand { get; }
        public ICommand SkipReferenceCommand { get; }
        public ICommand SkipStepCommand { get; }
        public ICommand JumpToStageCommand { get; }
        public ICommand SlewWithMountCommand { get; }
        public ICommand SwitchWidePathCommand { get; }
        public ICommand SwitchSciencePathCommand { get; }
        public ICommand RemoveFromQueueCommand { get; }
        public ICommand ToggleDoneCommand { get; }

        public ICommand QueueNextCommand { get; }
        public ICommand PixelReadoutStartCommand { get; }
        public ICommand PixelReadoutMoveCommand { get; }
        public ICommand PixelReadoutEndCommand { get; }
        public ICommand AddCaptureEntryCommand { get; }
        public ICommand RemoveCaptureEntryCommand { get; }

        public ICommand TryCaptureEntryCommand { get; }

        public ICommand ResetFringesCommand { get; }
        public ICommand SetFringeModeCommand { get; }
        public ICommand FringeDemoCommand { get; }

        public ApplicationStatus Status {
            get => status;
            set {
                status = value;
                status.Source = Title;
                RaisePropertyChanged();
                applicationStatusMediator.StatusUpdate(status);
            }
        }

        public BitmapSource PreparedImage {
            get => preparedImage;
            private set {
                var sizeChanged = preparedImage == null
                    || value == null
                    || preparedImage.PixelWidth != value.PixelWidth
                    || preparedImage.PixelHeight != value.PixelHeight;
                preparedImage = value;
                RaisePropertyChanged();
                RaiseImageRegionState();
                if (sizeChanged && value != null) {
                    FitImageInView("the frame is now " + value.PixelWidth + " x " + value.PixelHeight);
                }
            }
        }

        public int ZoomFitToken {
            get => zoomFitToken;
            private set { zoomFitToken = value; RaisePropertyChanged(); }
        }

        private void NoteRoomForImage() {
            var visible = GateCardVisible;
            if (visible == gateCardTookRoom) {
                return;
            }
            gateCardTookRoom = visible;
            FitImageInView(visible
                ? "the confirmation card took room from the image"
                : "the confirmation card gave the room back to the image");
        }

        private void FitImageInView(string why) {
            Logger.Info("Zooming the image back out to fit because " + why);
            UiThread.Post(() => ZoomFitToken++);
        }

        public bool IsRunning {
            get => isRunning;
            private set { isRunning = value; RaisePropertyChanged(); RaiseImageRegionState(); }
        }

        public string PixelReadout {
            get => pixelReadout;
            private set {
                pixelReadout = value;
                RaisePropertyChanged();
                RaisePropertyChanged(nameof(PixelReadoutVisible));
            }
        }

        public bool PixelReadoutVisible => MainRegionShowsImage && !string.IsNullOrEmpty(pixelReadout);

        public bool ShowCrosshair {
            get => optionsProvider.Current.ShowCrosshair;
            set {
                var speckle = optionsProvider.Current;
                if (speckle == null || speckle.ShowCrosshair == value) {
                    return;
                }
                LogClick("Crosshair " + (value ? "on" : "off"));
                speckle.ShowCrosshair = value;
                RaisePropertyChanged();
                RaisePropertyChanged(nameof(CrosshairVisible));
            }
        }

        public bool CrosshairVisible => ShowCrosshair && PreparedImage != null;

        public double CrosshairSize => Math.Clamp(ShortestImageSide / 8d, 40d, 400d);

        public double CrosshairThickness => Math.Clamp(ShortestImageSide / 600d, 1d, 8d);

        public double CrosshairLeft => CrosshairCenter.X - CrosshairSize / 2d;

        public double CrosshairTop => CrosshairCenter.Y - CrosshairSize / 2d;

        public bool IsDrawerOpen {
            get => isDrawerOpen;
            set {
                if (isDrawerOpen != value) {
                    Logger.Info("UI: Plan panel " + (value ? "opened" : "closed"));
                    LogTemplateEdits();
                }
                isDrawerOpen = value;
                RaisePropertyChanged();
                RaisePropertyChanged(nameof(GateCardVisible));
                RaiseImageRegionState();
            }
        }

        public bool IsKeplerOpen {
            get => isKeplerOpen;
            set {
                if (isKeplerOpen == value) {
                    return;
                }
                if (value && !optionsProvider.Current.ShowKeplerSky) {
                    return;
                }
                Logger.Info("UI: Kepler sky view " + (value ? "opened" : "closed"));
                isKeplerOpen = value;
                if (value) {
                    CloseFringeDemo();
                    Kepler.Start();
                } else {
                    Kepler.Stop();
                }
                RaisePropertyChanged();
                RaisePropertyChanged(nameof(KeplerVisible));
                RaiseImageRegionState();
            }
        }

        public bool KeplerVisible => isKeplerOpen;

        public bool KeplerButtonVisible => optionsProvider.Current.ShowKeplerSky
            && (!IsRunning || IsDrawerOpen || isKeplerOpen);

        public bool IsMoreOpen {
            get => isMoreOpen;
            set {
                if (isMoreOpen != value) {
                    Logger.Info("UI: More drawer " + (value ? "opened" : "closed"));
                }
                isMoreOpen = value;
                RaisePropertyChanged();
            }
        }

        public bool IsLoadingList {
            get => isLoadingList;
            private set {
                isLoadingList = value;
                RaisePropertyChanged();
                RaisePropertyChanged(nameof(ListToolsEnabled));
                RaisePropertyChanged(nameof(NoTargetsMessageVisible));
                RaisePropertyChanged(nameof(QueueEmpty));
                RaisePropertyChanged(nameof(LoadingListStatus));
            }
        }

        public bool ListToolsEnabled => !IsLoadingList;

        public bool NoTargetsMessageVisible => Targets.Count == 0 && !IsLoadingList;

        public string LoadListLabel => Targets.Count == 0 ? "Load list" : "Add list";

        public AsyncObservableCollection<ReferenceStar> BrightStars { get; } = new AsyncObservableCollection<ReferenceStar>();

        public string BrightStarsLabel {
            get {
                var speckle = optionsProvider.Current;
                if (speckle == null) {
                    return "bright stars up now";
                }
                if (speckle.DomePositionLock) {
                    return "bright stars in the dome slit";
                }
                return speckle.DomeSlitSouth ? "bright stars near the meridian" : "bright stars due north";
            }
        }

        public bool AddStarOpen {
            get => addStarOpen;
            set {
                if (addStarOpen == value) {
                    return;
                }
                addStarOpen = value;
                Logger.Info("UI: add star panel " + (value ? "opened" : "closed"));
                if (value) {
                    RaisePropertyChanged(nameof(BrightStarsLabel));
                    _ = LoadBrightStarsAsync();
                }
                RaisePropertyChanged();
            }
        }

        public string NewStarName {
            get => newStarName;
            set { newStarName = value; RaisePropertyChanged(); RequeryCommands(); }
        }

        public string NewStarRa {
            get => newStarRa;
            set { newStarRa = value; RaisePropertyChanged(); RequeryCommands(); }
        }

        public string NewStarDec {
            get => newStarDec;
            set { newStarDec = value; RaisePropertyChanged(); RequeryCommands(); }
        }

        public bool CanAddStar => TryReadAngle(newStarRa, true, out _) && TryReadAngle(newStarDec, false, out _);

        private async Task LoadBrightStarsAsync() {
            try {
                BrightStars.Clear();
                var stars = await coordinator.FindBrightStarsInSlitAsync(CancellationToken.None).ConfigureAwait(true);
                foreach (var star in stars) {
                    BrightStars.Add(star);
                }
            } catch (Exception ex) {
                Logger.Error("Could not read the bright star list", ex);
                Notification.ShowError("Could not read the bright star list: " + ex.Message);
            }
        }

        private void PickBrightStar(ReferenceStar star) {
            if (star == null) {
                return;
            }
            Logger.Info("UI: bright star " + star.Name2 + " picked");
            NewStarName = star.Name2;
            NewStarRa = CoordinateFormat.RaHours(star.RA2000 / 15d);
            NewStarDec = CoordinateFormat.DecDegrees(star.Dec2000);
        }

        private void AddStar() {
            if (!TryReadAngle(newStarRa, true, out var ra) || !TryReadAngle(newStarDec, false, out var dec)) {
                Notification.ShowError("Could not read those coordinates. Use 18 36 56 and +38 47 01, or decimal degrees.");
                return;
            }
            LogClick("Add star " + newStarName);
            var added = coordinator.AddTargetByHand(newStarName, ra, dec);
            coordinator.QueueNext(added);
            coordinator.MoveInQueue(added, 0);
            NewStarName = string.Empty;
            NewStarRa = string.Empty;
            NewStarDec = string.Empty;
            AddStarOpen = false;
            UiThread.Post(() => { RaiseAll(); RequeryCommands(); });
        }

        private static bool TryReadAngle(string text, bool isRightAscension, out double degrees) {
            degrees = 0;
            if (string.IsNullOrWhiteSpace(text)) {
                return false;
            }
            var cleaned = text.Trim().Replace(':', ' ');
            if (cleaned.Contains(" ")) {
                try {
                    degrees = isRightAscension ? AstroUtil.HMSToDegrees(cleaned) : AstroUtil.DMSToDegrees(cleaned);
                    return true;
                } catch (Exception) {
                    return false;
                }
            }
            if (!double.TryParse(cleaned, NumberStyles.Float, CultureInfo.InvariantCulture, out var single)) {
                return false;
            }
            if (isRightAscension) {
                if (single < 0d || single > 24d) {
                    return false;
                }
                degrees = single * 15d;
                return true;
            }
            degrees = single;
            return single >= -90d && single <= 90d;
        }

        public string LoadListTooltip {
            get {
                var lists = coordinator.LoadedLists;
                if (lists.Count == 0) {
                    return "Load one or more target lists from CSV files";
                }
                return "Add more target lists to the " + lists.Count + (lists.Count == 1 ? " list" : " lists")
                    + " already loaded: " + string.Join(", ", lists.Select(Path.GetFileName));
            }
        }

        public bool QueueEmpty => UpNext.Count == 0;

        public string LoadingListStatus => string.IsNullOrEmpty(loadingListName)
            ? "Loading target list"
            : "Loading " + loadingListName;

        public bool AutomaticallyRun {
            get => optionsProvider.Current.AutomaticallyRun;
            set {
                if (optionsProvider.Current.AutomaticallyRun != value) {
                    Logger.Info("UI: Auto run set to " + value);
                }
                optionsProvider.Current.AutomaticallyRun = value;
                RaiseAll();
                RequeryCommands();
            }
        }

        private IReadOnlyList<string> RecentTargetLists {
            get {
                var raw = optionsProvider.Current.RecentTargetLists;
                if (string.IsNullOrWhiteSpace(raw)) {
                    return Array.Empty<string>();
                }
                return raw.Split('|', StringSplitOptions.RemoveEmptyEntries);
            }
        }

        public bool TwoCameraSetupVisible => optionsProvider.Current.DualCameraSetup;

        public bool IsWidePath => Session.CurrentLightPath == LightPath.Wide;

        public bool IsSciencePath => Session.CurrentLightPath == LightPath.Science;

        public bool IsSwitchingLightPath {
            get => isSwitchingLightPath;
            private set {
                isSwitchingLightPath = value;
                RaisePropertyChanged();
                RaisePropertyChanged(nameof(CanSwitchLightPath));
                RaisePropertyChanged(nameof(LightPathTooltip));
                RequeryCommands();
            }
        }

        public bool CanSwitchLightPath => TwoCameraSetupVisible && !isSwitchingLightPath
            && (!IsRunning || Session.IsHolding || coordinator.Gate.Pending != null);

        public string LightPathTooltip {
            get {
                if (isSwitchingLightPath) {
                    return "The flip mirror is moving";
                }
                if (!CanSwitchLightPath) {
                    return "Pause the run, or wait for the next confirmation, before moving the flip mirror";
                }
                return "Choose which camera the light path points at";
            }
        }

        public double MirrorAngle => -45d * MirrorTravel;

        public double WidePathOpacity => 0.2d + 0.8d * (1d - MirrorTravel);

        public double SciencePathOpacity => 0.2d + 0.8d * MirrorTravel;

        private double MirrorTravel {
            get {
                var options = optionsProvider.Current;
                var wideStop = options.WideMirrorPosition;
                var scienceStop = options.ScienceMirrorPosition;
                var travel = scienceStop - wideStop;
                var assumed = Session.CurrentLightPath == LightPath.Science ? 1d : 0d;
                if (Math.Abs(travel) < 1) {
                    return assumed;
                }
                if (!focuserConnected) {
                    return assumed;
                }
                return Math.Clamp((focuserPosition - wideStop) / (double)travel, 0d, 1d);
            }
        }

        public bool IsFocusing {
            get => isFocusing;
            set {
                if (isFocusing == value) {
                    return;
                }
                Logger.Info("UI: Focus toggled to " + (value ? "start" : "stop"));
                if (value) {
                    StartFocusPreview();
                } else {
                    StopFocusPreview();
                }
            }
        }

        public bool CanFocus =>
            cameraInfo != null
            && cameraInfo.Connected
            && !isConnectingEquipment
            && !IsLoadingList
            && Session.Phase != WorkflowPhase.RunningVideoExposures
            && (!IsRunning || Session.IsHoldRequested);

        public string FocusLabel => IsFocusing ? "Stop" : "Focus";

        public string FocusTooltip => IsFocusing
            ? "Stop the live preview"
            : "Show a continuous full frame preview so the telescope can be focused by hand";

        public string CurrentLightPathLabel {
            get {
                switch (Session.CurrentLightPath) {
                    case LightPath.Wide: return "wide";
                    case LightPath.Science: return "science";
                    default: return "";
                }
            }
        }

        public bool MainRegionShowsImage => PreparedImage != null && !PlanningPanelVisible && !isKeplerOpen;

        public bool ImageRegionVisible => !PlanningPanelVisible && !isKeplerOpen;

        public bool PointingOverlayVisible => IsSlewGate && !isKeplerOpen;

        public bool PointingPulseVisible => IsSlewGate && !PlanningPanelVisible && !isKeplerOpen;

        public bool ShowCompass {
            get => showCompass;
            set {
                if (showCompass == value) {
                    return;
                }
                showCompass = value;
                Logger.Info("UI: compass overlay " + (value ? "shown" : "hidden"));
                RaisePropertyChanged();
                RaisePropertyChanged(nameof(CompassVisible));
            }
        }

        public bool CompassVisible => showCompass && !PlanningPanelVisible && !isKeplerOpen;

        public string CompassLabel => IsSciencePath ? "Science camera" : "Wide camera";

        public bool CompassMirrored {
            get {
                var speckle = optionsProvider.Current;
                if (speckle == null) {
                    return false;
                }
                return IsSciencePath ? speckle.ScienceCompassMirrored : speckle.WideCompassMirrored;
            }
            set {
                var speckle = optionsProvider.Current;
                if (speckle == null) {
                    return;
                }
                if (IsSciencePath) {
                    speckle.ScienceCompassMirrored = value;
                } else {
                    speckle.WideCompassMirrored = value;
                }
                Logger.Info("UI: compass for the " + (IsSciencePath ? "science" : "wide") + " camera is "
                    + (value ? "mirrored, east on the right" : "not mirrored, east on the left"));
                RaisePropertyChanged();
                RaisePropertyChanged(nameof(CompassLeftLabel));
                RaisePropertyChanged(nameof(CompassRightLabel));
            }
        }

        public string CompassLeftLabel => CompassMirrored ? "W" : "E";

        public string CompassRightLabel => CompassMirrored ? "E" : "W";

        public double CompassAngle {
            get {
                var speckle = optionsProvider.Current;
                if (speckle == null) {
                    return 0d;
                }
                return IsSciencePath ? speckle.ScienceCompassAngle : speckle.WideCompassAngle;
            }
            set {
                var speckle = optionsProvider.Current;
                if (speckle == null) {
                    return;
                }
                if (IsSciencePath) {
                    speckle.ScienceCompassAngle = value;
                } else {
                    speckle.WideCompassAngle = value;
                }
                Logger.Info("UI: compass angle for the " + (IsSciencePath ? "science" : "wide") + " camera set to " + value);
                RaisePropertyChanged();
            }
        }

        public bool PreviewBadgeVisible => IsRunning
            && coordinator.Gate.Pending?.Kind != GateKind.Error
            && coordinator.Gate.Pending != null
            && Session.Phase != WorkflowPhase.RunningVideoExposures
            && !PlanningPanelVisible
            && !isKeplerOpen
            && PreparedImage != null;

        public string PointingOverlayRa => GateRaNowText;

        public string PointingOverlayDec => GateDecNowText;

        public BitmapSource FringeImage => fringeResult?.Image;

        public bool FringePaneEnabled {
            get => optionsProvider.Current.FringePaneVisible;
            set {
                optionsProvider.Current.FringePaneVisible = value;
                Logger.Info("UI: Fringe pane " + (value ? "shown" : "hidden"));
                RaisePropertyChanged();
                RaiseFringeState();
            }
        }

        public bool FringeVisible => FringePaneEnabled && FringeImage != null && !PlanningPanelVisible && !isKeplerOpen;

        public bool FringeSideBySide => (FringePaneOrientation)optionsProvider.Current.FringePaneOrientation == FringePaneOrientation.SideBySide;

        public bool FringeModeAverage => CurrentFringeMode == FringePaneMode.Average;

        public bool FringeModeAutocorrelation => CurrentFringeMode == FringePaneMode.Autocorrelation;

        private FringePaneMode CurrentFringeMode => fringeAnalysis?.PaneMode ?? FringePaneMode.Average;

        public bool StartVisible => !IsRunning && Targets.Count > 0 && (Session.Phase == WorkflowPhase.ListLoaded || Session.Phase == WorkflowPhase.Stopped);

        public bool PlanningPanelVisible => ((!IsRunning && !IsFocusing && !IsFringeDemoRunning) || IsDrawerOpen) && !isKeplerOpen;

        public bool ImagePlaceholderVisible => (IsRunning || IsFocusing) && PreparedImage == null && !FringeVisible && !IsDrawerOpen && !isKeplerOpen;

        public bool ChainVisible => IsRunning && RunChromeVisible;

        public bool RunChromeVisible => !isKeplerOpen && !(IsRunning && IsDrawerOpen);

        public bool StatusLineVisible => !isKeplerOpen;

        public bool ReferenceLegVisible => IsRunning && Session.CurrentTarget != null
            && (Session.CurrentTarget.GetRef > 0 || Session.CurrentReference != null || ReferenceStarService.HasGaiaNumber(Session.CurrentTarget.RefGaiaNum));

        public string TargetSectionLabel {
            get {
                var name = Session.CurrentTarget?.Name;
                return string.IsNullOrWhiteSpace(name) ? "Target" : name;
            }
        }

        public string ReferenceSectionLabel {
            get {
                var name = Session.CurrentReference?.Name;
                return string.IsNullOrWhiteSpace(name) ? "Reference star" : name;
            }
        }

        public bool CanToggleSkipReference => ReferenceLegVisible && !Session.ReferenceLegAbandoned;

        public string SkipReferenceTooltip {
            get {
                if (Session.ReferenceLegAbandoned) {
                    return "The reference leg of this target was already abandoned";
                }
                return Session.SkipReference
                    ? "Image the reference star for this target after all - the target itself is not affected"
                    : "Skip only the reference star for this target and keep the target";
            }
        }

        public SpeckleTarget NextUpTarget {
            get {
                if (UpNext.Count > 0) {
                    return UpNext[0];
                }
                var current = Session.CurrentTarget;
                return Targets
                    .Where(target => !target.IsComplete && !ReferenceEquals(target, current))
                    .OrderBy(target => target.ImageTime)
                    .FirstOrDefault();
            }
        }

        public bool AutoRunMountWarning {
            get {
                if (!AutomaticallyRun) {
                    return false;
                }
                return telescopeInfo == null || !telescopeInfo.Connected || !telescopeInfo.CanSlew || telescopeInfo.AtPark;
            }
        }

        public string AutoRunMountWarningText => "\"Run automatically\" is switched on in the plugin options, but the mount cannot slew. The panel will stop and ask you to point the telescope for every target.";

        public string ModeBadge {
            get {
                var badge = IsFullAuto() ? "FULL-AUTO" : "SEMI-AUTONOMOUS";
                if (TwoCameraSetupVisible) {
                    badge += " TWO CAMERAS";
                }
                return badge;
            }
        }

        private string CaptureSettingsLine() {
            var line = string.Empty;
            if (Session.CaptureEntryCount > 1) {
                line += " - filter " + Session.CaptureEntryNumber + " of " + Session.CaptureEntryCount;
            }
            var filter = Session.ActiveFilter;
            if (!string.IsNullOrWhiteSpace(filter)) {
                line += " - " + filter;
            }
            if (Session.ActiveExposureTime > 0) {
                line += (string.IsNullOrWhiteSpace(filter) ? " - " : " at ")
                    + Session.ActiveExposureTime.ToString("0.####", CultureInfo.InvariantCulture) + " s";
            }
            return line;
        }

        public string StatusLine {
            get {
                if (isBenchmarking) {
                    return "Benchmarking the camera";
                }
                if (isConnectingEquipment) {
                    return "Connecting the equipment";
                }
                if (!IsRunning && Targets.Count > 0 && !cameraMediator.IsFreeToCapture(this)) {
                    return "Another part of NINA is using the camera";
                }
                var pending = coordinator.Gate.Pending;
                if (Session.IsHoldRequested) {
                    if (Session.IsHolding) {
                        return "Paused - press Resume to carry on";
                    }
                    return pending != null
                        ? "Pausing - confirm this step first, then the run pauses"
                        : "Pausing - finishing the current step";
                }
                switch (Session.Phase) {
                    case WorkflowPhase.Idle:
                        return Targets.Count == 0 ? string.Empty : "Ready";
                    case WorkflowPhase.ListLoaded:
                        return runEnded
                            ? "Nothing to observe right now - " + Session.TargetsImaged + " imaged, " + Session.TargetsSkipped + " skipped"
                            : "Ready - " + Targets.Count(target => !target.IsComplete) + " remaining";
                    case WorkflowPhase.PickingTarget:
                        return "Selecting next target";
                    case WorkflowPhase.WaitingForWindow:
                        return Session.WindowOpensAt.HasValue
                            ? "Next window " + Session.WindowOpensAt.Value.ToString("HH:mm", CultureInfo.InvariantCulture)
                            : "Waiting for window";
                    case WorkflowPhase.SwitchingToWide:
                        return "Switching to wide path";
                    case WorkflowPhase.AwaitingSlewConfirmation:
                        return pending != null ? "Waiting for the telescope to be pointed" : "Slewing";
                    case WorkflowPhase.Slewing:
                        return "Slewing";
                    case WorkflowPhase.Centering:
                        return "Centering";
                    case WorkflowPhase.SwitchingToScience:
                        return "Switching to science path";
                    case WorkflowPhase.PositioningRoi:
                        return "Positioning ROI";
                    case WorkflowPhase.CalibratingExposure:
                        return "Calibrating exposure";
                    case WorkflowPhase.AwaitingImageConfirmation:
                        return "Waiting for confirmation";
                    case WorkflowPhase.RunningVideoExposures:
                        return "Capturing - " + Session.FrameCount + " / " + Session.FrameTotal + CaptureSettingsLine();
                    case WorkflowPhase.TargetComplete:
                        return "Target complete";
                    case WorkflowPhase.Faulted:
                        return pending?.Title ?? "Step failed";
                    case WorkflowPhase.Stopped:
                        return "Stopped - " + Session.TargetsImaged + " imaged";
                    default:
                        return "";
                }
            }
        }

        public bool IsActionNeeded => coordinator.Gate.Pending != null || Session.Phase == WorkflowPhase.WaitingForWindow;

        public bool ProgressBarVisible => IsRunning && !IsActionNeeded && !Session.IsHolding;

        public bool ProgressIndeterminate => Session.Phase != WorkflowPhase.RunningVideoExposures;

        public bool PrimaryActionVisible => !isKeplerOpen && (IsRunning ? IsActionNeeded : StartVisible);

        public string PrimaryActionLabel {
            get {
                if (!IsRunning) {
                    return "Start";
                }
                if (Session.Phase == WorkflowPhase.WaitingForWindow) {
                    return "Image now";
                }
                var pending = coordinator.Gate.Pending;
                if (pending == null) {
                    return "";
                }
                if (pending.Kind == GateKind.Error) {
                    return "Retry this step";
                }
                if (pending.Kind == GateKind.SlewConfirmation) {
                    return pending.IsReference ? "Telescope is on reference" : "Telescope is on target";
                }
                return pending.IsReference ? "Image reference" : "Image this target";
            }
        }

        public string PrimaryActionTooltip {
            get {
                if (!IsRunning) {
                    return "Connect the equipment and start the run";
                }
                if (Session.Phase == WorkflowPhase.WaitingForWindow) {
                    return "Image this target now instead of waiting for its window";
                }
                var pending = coordinator.Gate.Pending;
                if (pending == null) {
                    return null;
                }
                if (pending.Kind == GateKind.Error) {
                    return "Run the failed step again";
                }
                if (pending.Kind == GateKind.SlewConfirmation) {
                    return "Confirm the telescope points at these coordinates and continue";
                }
                return "Start the video exposures for these coordinates";
            }
        }

        public bool GateCardVisible => coordinator.Gate.Pending != null && IsRunning && !IsDrawerOpen && !IsSlewGate;

        public bool GateCardCollapsed {
            get => gateCardCollapsed;
            set {
                gateCardCollapsed = value;
                RaisePropertyChanged();
                RaisePropertyChanged(nameof(GateCardExpanded));
                RaisePropertyChanged(nameof(GateCardCollapseLabel));
            }
        }

        public bool GateCardExpanded => !gateCardCollapsed;

        public string GateCardCollapseLabel => gateCardCollapsed ? "Show" : "Hide";

        public bool IsErrorGate => coordinator.Gate.Pending?.Kind == GateKind.Error;

        public bool IsSlewGate => coordinator.Gate.Pending?.Kind == GateKind.SlewConfirmation;

        public bool IsImageGate => coordinator.Gate.Pending?.Kind == GateKind.ImageConfirmation;

        public CaptureEntryCollection CaptureEntries => Session.CaptureEntries;

        public IReadOnlyList<string> AvailableFilters => Session.AvailableFilters;

        public bool CapturePlanVisible => IsImageGate;

        public bool CapturePlanEditable => IsImageGate && !Session.CapturePlanLocked;

        public bool CanRemoveCaptureEntry => CapturePlanEditable && Session.CaptureEntries.Count > 1;

        public bool HasFilters => Session.AvailableFilters != null && Session.AvailableFilters.Count > 0;

        public bool ExposureStepperEditable => CapturePlanEditable || !IsImageGate;

        public string ExposureStepperLabel => IsImageGate ? "Exposure" : "Centring exposure";

        public double ExposureStepperStep => IsImageGate ? 0.005 : 0.25;

        public string ExposureStepperTooltip => IsImageGate
            ? "Seconds per frame for the video exposures on this target"
            : "Seconds per frame for the centring view. It is kept for the next night.";

        public double ExposureStepperTime {
            get => IsImageGate ? Session.CalculatedExposureTime : optionsProvider.Current.CenteringExposureTime;
            set {
                if (IsImageGate) {
                    coordinator.OverrideExposureTime(value);
                } else {
                    var speckle = optionsProvider.Current;
                    if (speckle != null) {
                        speckle.CenteringExposureTime = value;
                    }
                }
                RaisePropertyChanged();
            }
        }

        public string GateTitle {
            get {
                var pending = coordinator.Gate.Pending;
                if (pending == null) {
                    return "";
                }
                var star = GateStarName;
                if (pending.Kind == GateKind.Error || string.IsNullOrWhiteSpace(star)) {
                    return pending.Title;
                }
                return pending.Kind == GateKind.SlewConfirmation ? "Slew to " + star : "Image " + star;
            }
        }

        public string GateStarName {
            get {
                var pending = coordinator.Gate.Pending;
                if (pending == null) {
                    return "";
                }
                if (pending.IsReference) {
                    return pending.Reference?.Name ?? pending.Target?.Name ?? "";
                }
                return pending.Target?.Name ?? "";
            }
        }

        public string GateRaNowText => CoordinateFormat.RaHours(GateCoordinatesNow?.RA ?? double.NaN);

        public string GateDecNowText => CoordinateFormat.DecDegrees(GateCoordinatesNow?.Dec ?? double.NaN);

        private Coordinates GateCoordinatesNow {
            get {
                var coordinates = coordinator.Gate.Pending?.Coordinates;
                if (coordinates == null) {
                    return null;
                }
                try {
                    return coordinates.Transform(Epoch.JNOW);
                } catch (Exception ex) {
                    Logger.Error("Could not convert the gate coordinates to JNOW", ex);
                    return null;
                }
            }
        }

        public string GateDetail => coordinator.Gate.Pending?.Detail ?? "";

        public string GateDeltaLine {
            get {
                var pending = coordinator.Gate.Pending;
                if (pending == null) {
                    return "";
                }
                if (pending.Kind == GateKind.SlewConfirmation) {
                    if (double.IsNaN(slewDeltaArcmin)) {
                        return "";
                    }
                    var line = "delta " + FormatSeparation(slewDeltaArcmin);
                    return SlewLooksOnTarget ? line + " (on target)" : line;
                }
                return "";
            }
        }

        public bool GateDeltaVisible => !IsErrorGate && !string.IsNullOrEmpty(GateDeltaLine);

        public bool SlewLooksOnTarget => !double.IsNaN(slewDeltaArcmin) && slewDeltaArcmin <= optionsProvider.Current.ManualSlewToleranceArcmin;



        public bool ErrorGateVisible => IsRunning && coordinator.Gate.Pending?.Kind == GateKind.Error;

        public bool CanSkipStep {
            get {
                var pending = coordinator.Gate.Pending;
                return pending != null && pending.Kind == GateKind.Error && pending.IsSkippable;
            }
        }

        public string PauseLabel => Session.IsHoldRequested ? "Resume" : "Pause";

        public string PauseTooltip => Session.IsHoldRequested
            ? "Resume the run"
            : "Pause the run - capture stops now and continues where it left off";

        public bool StopRunVisible => IsRunning;

        public bool StopConfirmPending => stopArmed;

        public string StopRunLabel => stopArmed ? "Confirm stop" : "Stop";

        public bool DangerZoneVisible => IsRunning;

        public string StopRunTooltip => stopArmed
            ? "Press again to end the run. The current target is abandoned and nothing further is imaged."
            : "End the run. Asks for a second press first.";

        public void UpdateDeviceInfo(CameraInfo deviceInfo) {
            cameraInfo = deviceInfo;
            EnsureFocusAllowed();
            RaiseFocusState();
            RequeryCommands();
        }

        public void UpdateDeviceInfo(FocuserInfo deviceInfo) {
            if (deviceInfo == null) {
                return;
            }
            var connected = deviceInfo.Connected;
            var position = deviceInfo.Position;
            if (connected == focuserConnected && position == focuserPosition) {
                return;
            }
            focuserConnected = connected;
            focuserPosition = position;
            UiThread.Post(() => {
                RaisePropertyChanged(nameof(MirrorAngle));
                RaisePropertyChanged(nameof(WidePathOpacity));
                RaisePropertyChanged(nameof(SciencePathOpacity));
            });
        }

        public void UpdateEndAutoFocusRun(AutoFocusInfo info) {
        }

        public void UpdateUserFocused(FocuserInfo info) {
        }

        public void AutoFocusRunStarting() {
        }

        public void NewAutoFocusPoint(OxyPlot.DataPoint dataPoint) {
        }

        public void UpdateDeviceInfo(TelescopeInfo deviceInfo) {
            telescopeInfo = deviceInfo;
            RecomputeSlewDelta();
            UiThread.Post(() => { RaiseAll(); RequeryCommands(); });
        }

        public void RefreshAfterReload() {
            Logger.Info("UI: panel shown again; image " + (PreparedImage == null ? "not available" : "restored")
                + ", phase " + Session.Phase);
            UiThread.Post(() => { UpdateChain(); RaiseAll(); RequeryCommands(); });
        }

        public void Dispose() {
            Logger.Info("Speckle panel disposing; unsubscribing from ImagePrepared");
            StopFocusPreview();
            coordinator.PhaseChanged -= OnPhaseChanged;
            coordinator.PendingChanged -= OnPendingChanged;
            coordinator.VideoExposuresProgress -= OnVideoExposuresProgress;
            coordinator.TargetsChanged -= OnTargetsChanged;
            coordinator.QueueChanged -= OnQueueChanged;
            Session.PropertyChanged -= OnSessionPropertyChanged;
            imagingMediator.ImagePrepared -= OnImagePrepared;
            if (fringeAnalysis != null) {
                fringeAnalysis.Updated -= OnFringeUpdated;
            }
            FringeDemoRequest.Requested -= OnFringeDemoRequested;
            BenchmarkRequest.Requested -= OnBenchmarkRequested;
            PreparedFrameRelay.Published -= OnPreparedFramePublished;
            cameraMediator.RemoveConsumer(this);
            telescopeMediator.RemoveConsumer(this);
            focuserMediator.RemoveConsumer(this);
            Kepler.Dispose();
            cooling.Dispose();
            StopAndDisposeSource(ref runCts);
            StopAndDisposeSource(ref focusCts);
            StopAndDisposeSource(ref demoCts);
            SetRawPixels(null);
        }

        private static void StopAndDisposeSource(ref CancellationTokenSource source) {
            var taken = Interlocked.Exchange(ref source, null);
            if (taken == null) {
                return;
            }
            try {
                taken.Cancel();
            } catch (ObjectDisposedException) {
            }
            taken.Dispose();
        }

        private bool CanStart() {
            return !IsRunning && !IsLoadingList && !isConnectingEquipment && !isBenchmarking && Targets.Count > 0 && cameraMediator.IsFreeToCapture(this);
        }

        private bool CanSlewWithMount() {
            return IsSlewGate && telescopeInfo != null && telescopeInfo.Connected && telescopeInfo.CanSlew && !telescopeInfo.AtPark;
        }

        private bool IsFullAuto() {
            return AutomaticallyRun && HasUsableMountPosition && telescopeInfo.CanSlew;
        }

        private void Start() {
            if (!CanStart()) {
                Logger.Info("UI: Start ignored - running " + IsRunning + ", loading " + IsLoadingList + ", targets " + Targets.Count + ", camera free " + cameraMediator.IsFreeToCapture(this));
                return;
            }
            Logger.Info("UI: Start clicked - " + Targets.Count + " targets, mode " + ModeBadge);
            CloseFringeDemo();
            LogTemplateEdits();
            _ = StartAsync();
        }

        private async Task StartAsync() {
            await StopFocusPreviewAsync().ConfigureAwait(true);
            isConnectingEquipment = true;
            UiThread.Post(() => RaisePropertyChanged(nameof(StatusLine)));
            RequeryCommands();
            bool ready;
            try {
                ready = await ConnectEquipmentAsync().ConfigureAwait(true);
            } catch (Exception ex) {
                Logger.Error(ex);
                Notification.ShowError("Cannot start: " + ex.Message);
                ready = false;
            } finally {
                isConnectingEquipment = false;
                progress.Report(new ApplicationStatus { Status = string.Empty });
                UiThread.Post(() => RaisePropertyChanged(nameof(StatusLine)));
                RequeryCommands();
            }
            if (!ready || !CanStart()) {
                return;
            }
            await RunAsync().ConfigureAwait(true);
        }

        private async Task<bool> ConnectEquipmentAsync() {
            var cameraConnected = await ConnectDeviceAsync("camera", cameraMediator.GetInfo()?.Connected == true, cameraMediator.Connect).ConfigureAwait(true);
            var telescopeConnected = await ConnectDeviceAsync("telescope", telescopeMediator.GetInfo()?.Connected == true, telescopeMediator.Connect).ConfigureAwait(true);
            await ConnectDeviceAsync("filter wheel", filterWheelMediator.GetInfo()?.Connected == true, filterWheelMediator.Connect).ConfigureAwait(true);
            await ConnectDeviceAsync("focuser", focuserMediator.GetInfo()?.Connected == true, focuserMediator.Connect).ConfigureAwait(true);
            if (!cameraConnected) {
                Notification.ShowError("Cannot start: the camera did not connect");
                return false;
            }
            if (!telescopeConnected) {
                Notification.ShowWarning("The telescope did not connect - the run continues with operator slews");
            }
            return true;
        }

        private async Task<bool> ConnectDeviceAsync(string label, bool alreadyConnected, Func<Task<bool>> connect) {
            if (alreadyConnected) {
                return true;
            }
            progress.Report(new ApplicationStatus { Status = "Connecting " + label });
            try {
                return await Task.Run(connect).ConfigureAwait(true);
            } catch (Exception ex) {
                Logger.Error(ex);
                return false;
            }
        }

        private async Task RunAsync() {
            cameraMediator.RegisterCaptureBlock(this);
            runCts?.Dispose();
            runCts = new CancellationTokenSource();
            runEnded = false;
            IsRunning = true;
            IsDrawerOpen = false;
            IsMoreOpen = false;
            BuildChains();
            RaiseAll();
            RequeryCommands();
            try {
                await Task.Run(() => coordinator.RunAsync(null, progress, runCts.Token)).ConfigureAwait(true);
            } catch (OperationCanceledException) {
            } catch (Exception ex) {
                Logger.Error(ex);
                Notification.ShowError(ex.Message);
            } finally {
                Logger.Info("Run finished - " + Session.TargetsImaged + " imaged, " + Session.TargetsSkipped + " skipped, " + Session.TotalFrames + " frames");
                if (!cameraReleasedForHold) {
                    cameraMediator.ReleaseCaptureBlock(this);
                }
                cameraReleasedForHold = false;
                IsRunning = false;
                IsDrawerOpen = true;
                runEnded = true;
                RecordSnapshotToRecent();
                progress.Report(new ApplicationStatus { Status = string.Empty });
                UiThread.Post(() => { UpdateChain(); RaiseAll(); RequeryCommands(); });
            }
        }

        private async Task LoadListAsync(string path) {
            if (!ListToolsEnabled) {
                return;
            }
            var paths = new List<string>();
            if (string.IsNullOrEmpty(path)) {
                var dialog = new OpenFileDialog {
                    DefaultExt = ".csv",
                    Filter = "CSV files (*.csv)|*.csv",
                    Multiselect = true
                };
                if (dialog.ShowDialog() != true) {
                    return;
                }
                paths.AddRange(dialog.FileNames);
            } else {
                paths.Add(path);
            }
            try {
                foreach (var csvPath in paths) {
                    if (!File.Exists(csvPath)) {
                        Notification.ShowError("Target list not found: " + csvPath);
                        continue;
                    }
                    Logger.Info("UI: Loading target list from " + csvPath);
                    SetLoadingList(Path.GetFileName(csvPath));
                    progress.Report(new ApplicationStatus { Status = LoadingListStatus });
                    await Task.Run(() => coordinator.LoadListAsync(csvPath, CancellationToken.None)).ConfigureAwait(true);
                    runEnded = false;
                    AddRecent(csvPath);
                    Logger.Info("UI: Target list loaded from " + csvPath + " - " + coordinator.Targets.Count + " targets in total");
                }
                LoadTemplates();
            } catch (Exception ex) {
                Logger.Error(ex);
                Notification.ShowError(ex.Message);
            } finally {
                SetLoadingList(null);
                progress.Report(new ApplicationStatus { Status = string.Empty });
                UiThread.Post(() => { RaiseAll(); RequeryCommands(); });
            }
        }

        private void SetLoadingList(string name) {
            UiThread.Post(() => {
                loadingListName = name;
                IsLoadingList = name != null;
                RequeryCommands();
            });
        }

        private void RemoveList(string path) {
            if (!ListToolsEnabled || string.IsNullOrEmpty(path)) {
                return;
            }
            var name = Path.GetFileName(path);
            if (!ConfirmListChange("Remove " + name + "?", path)) {
                Logger.Info("UI: Remove list " + path + " cancelled by the operator");
                return;
            }
            Logger.Info("UI: Remove list clicked - " + path);
            try {
                coordinator.RemoveList(path);
                runEnded = false;
                RaiseAll();
                RequeryCommands();
            } catch (Exception ex) {
                Logger.Error(ex);
                Notification.ShowError(ex.Message);
            }
        }

        private bool ConfirmListChange(string question, string path) {
            var imaging = Session.CurrentTarget;
            var stopsImaging = IsRunning
                && imaging != null
                && (path == null || string.Equals(imaging.SourceList, path, StringComparison.OrdinalIgnoreCase));
            if (!stopsImaging && Targets.Count == 0) {
                return true;
            }
            var detail = stopsImaging
                ? " " + imaging.Name + " is being imaged now and will stop."
                : " " + Targets.Count + (Targets.Count == 1 ? " target" : " targets") + " and the progress made tonight go with it.";
            var answer = MessageBox.Show(question + detail, "Speckle", MessageBoxButton.OKCancel, MessageBoxImage.Question);
            return answer == MessageBoxResult.OK;
        }

        private void ExportCsv() {
            if (Targets.Count == 0) {
                return;
            }
            Logger.Info("UI: Export CSV clicked - " + Targets.Count + " targets");
            LogTemplateEdits();
            try {
                var dialog = new SaveFileDialog {
                    DefaultExt = ".csv",
                    Filter = "CSV files (*.csv)|*.csv",
                    FileName = "TargetList-" + DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".csv"
                };
                if (dialog.ShowDialog() != true) {
                    return;
                }
                targetListService.WriteTargets(Targets.ToList(), dialog.FileName);
                Logger.Info("UI: Target list exported to " + dialog.FileName);
                AddRecent(dialog.FileName);
            } catch (Exception ex) {
                Logger.Error(ex);
                Notification.ShowError(ex.Message);
            }
        }

        private bool CanPrimaryAction() {
            return IsRunning ? IsActionNeeded : CanStart();
        }

        private void PrimaryAction() {
            var label = PrimaryActionLabel;
            if (!IsRunning) {
                Logger.Info("UI: Primary action clicked - resolved to start (label " + label + ", phase " + Session.Phase + ")");
                Start();
                return;
            }
            if (Session.Phase == WorkflowPhase.WaitingForWindow) {
                Logger.Info("UI: Primary action clicked - resolved to image now (label " + label + ", phase " + Session.Phase + ")");
                coordinator.ImageNow();
                return;
            }
            var pending = coordinator.Gate.Pending;
            if (pending == null) {
                Logger.Info("UI: Primary action clicked with no pending gate (phase " + Session.Phase + ")");
                return;
            }
            if (pending.Kind == GateKind.Error) {
                Logger.Info("UI: Primary action clicked - resolved to retry step (label " + label + ", phase " + Session.Phase + ", gate " + pending.Title + ")");
                coordinator.RetryStep();
            } else {
                Logger.Info("UI: Primary action clicked - resolved to confirm (label " + label + ", phase " + Session.Phase + ", gate " + pending.Title + ")");
                coordinator.Confirm();
            }
        }

        private void StopRun() {
            if (!stopArmed) {
                Logger.Info("UI: Stop clicked once, waiting for confirmation (phase " + Session.Phase
                    + ", target " + (Session.CurrentTarget?.Name ?? "none") + ")");
                ArmStop();
                return;
            }
            DisarmStop();
            Logger.Info("UI: Stop confirmed - ending the run immediately (phase " + Session.Phase + ", target " + (Session.CurrentTarget?.Name ?? "none") + ")");
            coordinator.RequestStop();
        }

        private void ArmStop() {
            stopArmed = true;
            RaiseStopState();
            stopArmTimer?.Stop();
            stopArmTimer = new DispatcherTimer { Interval = StopConfirmWindow };
            stopArmTimer.Tick += (sender, args) => {
                Logger.Info("UI: Stop confirmation expired without a second click");
                DisarmStop();
            };
            stopArmTimer.Start();
        }

        private void DisarmStop() {
            stopArmTimer?.Stop();
            stopArmTimer = null;
            if (!stopArmed) {
                return;
            }
            stopArmed = false;
            RaiseStopState();
        }

        private void RaiseStopState() {
            RaisePropertyChanged(nameof(StopRunLabel));
            RaisePropertyChanged(nameof(StopRunTooltip));
            RaisePropertyChanged(nameof(StopConfirmPending));
        }

        private void SkipTarget() {
            Logger.Info("UI: Skip target clicked - " + (Session.CurrentTarget?.Name ?? "none"));
            coordinator.SkipCurrentTarget();
        }

        private void ToggleSkipReference() {
            var skip = !Session.SkipReference;
            Logger.Info("UI: Skip reference clicked - armed " + skip + " for target " + (Session.CurrentTarget?.Name ?? "none"));
            coordinator.SetSkipReference(skip);
        }

        private void SkipStep() {
            Logger.Info("UI: Skip step clicked - gate " + (coordinator.Gate.Pending?.Title ?? "none") + ", phase " + Session.Phase);
            coordinator.SkipStep();
        }

        private static void LogClick(string what) {
            Logger.Info("UI: " + what + " clicked");
        }

        private void StartFocusPreview() {
            if (!CanFocus || (focusTask != null && !focusTask.IsCompleted)) {
                Logger.Info("Focus preview not started - canFocus " + CanFocus + ", already running " + (focusTask != null && !focusTask.IsCompleted));
                return;
            }
            Logger.Info("Focus preview starting");
            focusCts?.Dispose();
            focusCts = new CancellationTokenSource();
            isFocusing = true;
            IsDrawerOpen = false;
            UiThread.Post(() => { RaiseAll(); RequeryCommands(); });
            var token = focusCts.Token;
            focusTask = Task.Run(() => RunFocusPreviewAsync(token));
        }

        private void StopFocusPreview() {
            _ = StopFocusPreviewAsync();
        }

        private async Task StopFocusPreviewAsync() {
            var task = focusTask;
            var wasFocusing = isFocusing;
            isFocusing = false;
            focusCts?.Cancel();
            if (task != null) {
                try {
                    await task.ConfigureAwait(true);
                } catch (Exception ex) {
                    Logger.Error(ex);
                }
            }
            focusTask = null;
            if (wasFocusing) {
                Logger.Info("Focus preview stopped");
            }
        }

        private void EnsureFocusAllowed() {
            if (isFocusing && !CanFocus) {
                Logger.Info("Focus preview stopped because it is no longer allowed (phase " + Session.Phase + ")");
                StopFocusPreview();
            }
        }

        private void RaiseFocusState() {
            UiThread.Post(() => {
                RaisePropertyChanged(nameof(IsFocusing));
                RaisePropertyChanged(nameof(CanFocus));
                RaisePropertyChanged(nameof(FocusLabel));
                RaisePropertyChanged(nameof(FocusTooltip));
            });
        }

        private async Task RunFocusPreviewAsync(CancellationToken ct) {
            try {
                var exposureTime = optionsProvider.Current.ExposureTime;
                var capture = new CaptureSequence {
                    ExposureTime = exposureTime > 0 ? exposureTime : 1d,
                    ImageType = CaptureSequence.ImageTypes.SNAPSHOT,
                    Binning = new BinningMode(1, 1),
                    Gain = -1,
                    Offset = -1,
                    TotalExposureCount = 1,
                    ProgressExposureCount = 1,
                    EnableSubSample = false
                };
                var parameters = new PrepareImageParameters(true, false);
                progress.Report(new ApplicationStatus { Status = "Live preview" });
                if (cameraMediator.GetInfo()?.CanShowLiveView == true) {
                    await RunLiveFramesAsync(capture, parameters, ct).ConfigureAwait(false);
                } else {
                    await RunSnapshotFramesAsync(capture, parameters, ct).ConfigureAwait(false);
                }
            } catch (OperationCanceledException) {
            } catch (Exception ex) {
                Logger.Error(ex);
                Notification.ShowError("Live preview stopped: " + ex.Message);
            } finally {
                isFocusing = false;
                progress.Report(new ApplicationStatus { Status = string.Empty });
                UiThread.Post(() => { RaiseAll(); RequeryCommands(); });
            }
        }

        private async Task RunLiveFramesAsync(CaptureSequence capture, PrepareImageParameters parameters, CancellationToken ct) {
            Logger.Info("Focus preview using live view at " + capture.ExposureTime + " s");
            var frames = 0;
            await foreach (var exposureData in cameraMediator.LiveView(capture, ct).ConfigureAwait(false)) {
                ct.ThrowIfCancellationRequested();
                if (exposureData == null) {
                    continue;
                }
                var rendered = await imagingMediator.PrepareImage(exposureData, parameters, ct).ConfigureAwait(false);
                Logger.Debug("Focus preview live frame " + (++frames));
                ShowFrame(rendered?.Image);
            }
        }

        private async Task RunSnapshotFramesAsync(CaptureSequence capture, PrepareImageParameters parameters, CancellationToken ct) {
            Logger.Info("Focus preview using snapshots at " + capture.ExposureTime + " s");
            var frames = 0;
            while (!ct.IsCancellationRequested) {
                var rendered = await imagingMediator.CaptureAndPrepareImage(capture, parameters, ct, progress).ConfigureAwait(false);
                Logger.Debug("Focus preview snapshot frame " + (++frames));
                ShowFrame(rendered?.Image);
            }
        }

        private void PauseResume() {
            if (Session.IsHoldRequested) {
                Logger.Info("UI: Resume clicked (phase " + Session.Phase + ", target " + (Session.CurrentTarget?.Name ?? "none") + ")");
                StopFocusPreview();
                if (cameraReleasedForHold) {
                    cameraMediator.RegisterCaptureBlock(this);
                    cameraReleasedForHold = false;
                }
                coordinator.Resume();
            } else {
                Logger.Info("UI: Pause clicked (phase " + Session.Phase + ", target " + (Session.CurrentTarget?.Name ?? "none") + ", gate " + (coordinator.Gate.Pending?.Title ?? "none") + ")");
                StopFocusPreview();
                _ = PauseAndReleaseCameraAsync();
            }
        }

        private async Task PauseAndReleaseCameraAsync() {
            try {
                await coordinator.PauseAsync().ConfigureAwait(true);
            } catch (Exception ex) {
                Logger.Error("Pause failed", ex);
            }
            await StopFocusPreviewAsync().ConfigureAwait(true);
            if (!cameraReleasedForHold) {
                cameraMediator.ReleaseCaptureBlock(this);
                cameraReleasedForHold = true;
                Logger.Info("Pause: camera capture block released");
            }
            UiThread.Post(() => { RaiseAll(); RequeryCommands(); });
        }

        private void JumpToStage(ChainStepVM step) {
            if (step == null || !IsRunning) {
                return;
            }
            var question = DescribeJumpRisk(step);
            if (question != null) {
                var answer = MessageBox.Show(question, "Speckle", MessageBoxButton.OKCancel, MessageBoxImage.Question);
                if (answer != MessageBoxResult.OK) {
                    Logger.Info("UI: jump to " + step.Label + " cancelled by the operator");
                    return;
                }
            }
            LogClick("Jump to " + step.Label + (step.IsReference ? " on the reference" : ""));
            coordinator.JumpToStage(step.Phase, step.IsReference);
        }

        private string DescribeJumpRisk(ChainStepVM step) {
            var interrupted = Session.Phase == WorkflowPhase.RunningVideoExposures
                ? " The frames already saved are kept, and imaging again writes to a new folder."
                : string.Empty;
            if (step.IsReference && !Session.ImagingReference) {
                return "Go to the reference star now? The rest of " + TargetSectionLabel + " will not be imaged." + interrupted;
            }
            if (!step.IsReference && Session.ImagingReference) {
                return "Go back to " + TargetSectionLabel + "? The reference star is imaged again afterwards." + interrupted;
            }
            if (interrupted.Length > 0) {
                return "Interrupt the frames being captured?" + interrupted;
            }
            return null;
        }

        private async Task SlewWithMountAsync() {
            var pending = coordinator.Gate.Pending;
            if (pending?.Coordinates == null) {
                Logger.Info("Slew with mount ignored - no pending coordinates");
                return;
            }
            try {
                Logger.Info("Slew with mount starting to " + CoordinateFormat.RaHours(pending.Coordinates.RA) + " " + CoordinateFormat.DecDegrees(pending.Coordinates.Dec));
                var watch = Stopwatch.StartNew();
                await telescopeMediator.SlewToCoordinatesAsync(pending.Coordinates, CancellationToken.None).ConfigureAwait(true);
                Logger.Info("Slew with mount finished in " + watch.ElapsedMilliseconds + " ms");
            } catch (Exception ex) {
                Logger.Error(ex);
                Notification.ShowError(ex.Message);
            }
        }

        private async Task SwitchLightPathAsync(LightPath path) {
            if (isSwitchingLightPath) {
                Logger.Info("Light path switch to " + path + " ignored - a switch is already in progress");
                return;
            }
            var resumeFocus = isFocusing;
            var watch = Stopwatch.StartNew();
            IsSwitchingLightPath = true;
            Logger.Info("Light path switch to " + path + " starting (current " + CurrentLightPathLabel + ")");
            using (var cts = new CancellationTokenSource()) {
                cts.CancelAfter(LightPathSwitchTimeout);
                try {
                    await StopFocusPreviewAsync().ConfigureAwait(true);
                    await coordinator.SwitchLightPathAsync(path, progress, cts.Token).ConfigureAwait(true);
                    Logger.Info("Light path switch to " + path + " finished in " + watch.ElapsedMilliseconds + " ms");
                } catch (OperationCanceledException) {
                    Logger.Warning("Light path switch to " + path + " timed out after " + watch.ElapsedMilliseconds + " ms");
                    Notification.ShowWarning("Light path switch to " + path + " timed out after "
                        + LightPathSwitchTimeout.TotalMinutes.ToString(CultureInfo.InvariantCulture) + " minutes");
                } catch (Exception ex) {
                    Logger.Error("Light path switch to " + path + " failed after " + watch.ElapsedMilliseconds + " ms", ex);
                    Notification.ShowError(ex.Message);
                } finally {
                    IsSwitchingLightPath = false;
                    progress.Report(new ApplicationStatus { Status = string.Empty });
                    UiThread.Post(() => { RaiseAll(); RequeryCommands(); });
                }
            }
            if (resumeFocus) {
                UiThread.Post(StartFocusPreview);
            }
        }

        private void RemoveFromQueue(SpeckleTarget target) {
            if (target == null) {
                return;
            }
            Logger.Info("UI: Queue remove - " + target.Name);
            coordinator.RemoveFromQueue(target);
        }

        private void ImageNext(SpeckleTarget target) {
            if (target == null) {
                return;
            }
            if (UpNext.Contains(target)) {
                Logger.Info("UI: Image next clicked again - " + target.Name + " taken out of the queue");
                coordinator.RemoveFromQueue(target);
                return;
            }
            Logger.Info("UI: Image next clicked - " + target.Name + " moved to position 1");
            coordinator.QueueNext(target);
            coordinator.MoveInQueue(target, 0);
        }

        public void DropInQueue(SpeckleTarget moved, SpeckleTarget droppedOn) {
            if (moved == null || ReferenceEquals(moved, droppedOn)) {
                return;
            }
            var wasQueued = UpNext.Contains(moved);
            if (!wasQueued) {
                coordinator.QueueNext(moved);
            }
            var index = droppedOn == null ? UpNext.Count - 1 : UpNext.IndexOf(droppedOn);
            if (index < 0) {
                index = UpNext.Count - 1;
            }
            coordinator.MoveInQueue(moved, index);
            Logger.Info("UI: Queue drag - " + moved.Name + (wasQueued ? " moved to" : " added at") + " position " + (index + 1));
        }

        private void ToggleDone(SpeckleTarget target) {
            if (target == null) {
                return;
            }
            var was = target.CyclesDisplay;
            target.Completed_cycles = target.IsComplete ? 0 : target.Cycles;
            Logger.Info("UI: " + target.Name + " marked " + (target.IsComplete ? "done" : "not done")
                + " by the operator - cycles " + was + " to " + target.CyclesDisplay);
            RebuildTargets();
            UiThread.Post(() => { RaiseAll(); RequeryCommands(); });
        }

        private void OnPhaseChanged(object sender, WorkflowPhaseChangedEventArgs e) {
            EnsureFocusAllowed();
            Kepler.Refresh();
            UiThread.Post(() => {
                if (!IsRunning) {
                    DisarmStop();
                }
                LogTemplateEdits();
                UpdateChain();
                RaiseAll();
                RequeryCommands();
            });
        }

        private void OnPendingChanged(object sender, EventArgs e) {
            var pending = coordinator.Gate.Pending;
            RecomputeSlewDelta();
            Logger.Info(pending == null
                ? "UI gate cleared"
                : "UI gate armed: " + pending.Kind + " - " + pending.Title + " (skippable " + pending.IsSkippable + ")");
            if (pending != null && pending.Kind == GateKind.SlewConfirmation && optionsProvider.Current.PlaySlewSound) {
                PlaySlewSound();
            }
            if (pending != null) {
                GateCardCollapsed = false;
                IsDrawerOpen = false;
                IsKeplerOpen = false;
            }
            if (pending != null && pending.Kind == GateKind.ImageConfirmation) {
                coordinator.RefreshAvailableFilters();
            }
            UiThread.Post(RaiseCapturePlanState);
            UiThread.Post(() => {
                UpdateChain();
                RaiseAll();
                RequeryCommands();
                NoteRoomForImage();
            });
        }

        private void OnVideoExposuresProgress(object sender, RoiSeriesProgress e) {
            if (statusClock.IsRunning && statusClock.ElapsedMilliseconds < StatusLineRefreshMs && e.FrameNumber < e.Total) {
                return;
            }
            statusClock.Restart();
            RaisePropertyChanged(nameof(StatusLine));
        }

        private void OnTargetsChanged(object sender, EventArgs e) {
            RebuildTargets();
            Kepler.Refresh();
            UiThread.Post(() => { RaiseAll(); RequeryCommands(); });
        }

        private void OnQueueChanged(object sender, EventArgs e) {
            RebuildQueue();
            Kepler.Refresh();
            UiThread.Post(RaiseAll);
        }

        private void OnSessionPropertyChanged(object sender, PropertyChangedEventArgs e) {
            if (e.PropertyName == nameof(SpeckleWorkflowSession.FrameCount) || e.PropertyName == nameof(SpeckleWorkflowSession.Fps)) {
                RaisePropertyChanged(nameof(StatusLine));
                return;
            }
            if (e.PropertyName == nameof(SpeckleWorkflowSession.CurrentTarget)) {
                Kepler.Refresh();
            }
            if (e.PropertyName == nameof(SpeckleWorkflowSession.CurrentLightPath) && Session.CurrentLightPath == LightPath.Wide) {
                FitImageInView("the light path is back on the wide camera");
            }
            EnsureFocusAllowed();
            UiThread.Post(() => { RaiseAll(); RequeryCommands(); });
        }

        private void OnPreparedFramePublished(object sender, IRenderedImage renderedImage) {
            var image = renderedImage?.Image;
            if (image == null) {
                return;
            }
            SetRawPixels(renderedImage);
            ShowFrame(image);
        }

        private void OnImagePrepared(object sender, ImagePreparedEventArgs e) {
            if (PreparedFrameRelay.CaptureLoopOwnsDisplay) {
                return;
            }
            var count = Interlocked.Increment(ref imagePreparedCount);
            var rendered = e?.RenderedImage;
            var image = rendered?.Image;
            Logger.Info("ImagePrepared " + count + ": args " + (e == null ? "null" : "ok")
                + ", rendered " + (rendered == null ? "null" : "ok")
                + ", image " + PanelBitmaps.DescribeImage(image)
                + ", autoStretch " + (e?.Parameters?.AutoStretch?.ToString() ?? "profile")
                + ", detectStars " + (e?.Parameters?.DetectStars?.ToString() ?? "profile")
                + ", isRunning " + IsRunning + ", isFocusing " + isFocusing
                + ", showsImage " + MainRegionShowsImage + ", planningPanel " + PlanningPanelVisible
                + ", drawer " + IsDrawerOpen);
            if (image == null) {
                Logger.Warning("ImagePrepared carried no bitmap; nothing to display");
                return;
            }
            SetRawPixels(rendered);
            _ = DisplayRenderedImageAsync(rendered, e?.Parameters);
        }

        private async Task DisplayRenderedImageAsync(IRenderedImage rendered, PrepareImageParameters parameters) {
            var image = rendered.Image;
            try {
                if (ShouldStretchForDisplay(parameters)) {
                    var settings = profileService.ActiveProfile.ImageSettings;
                    var stretched = await rendered.Stretch(settings.AutoStretchFactor, settings.BlackClipping, false).ConfigureAwait(false);
                    if (stretched?.Image != null) {
                        image = stretched.Image;
                        Logger.Debug("ImagePrepared stretched for display with factor " + settings.AutoStretchFactor + " and black clipping " + settings.BlackClipping);
                    }
                }
            } catch (Exception ex) {
                Logger.Error("Could not stretch the prepared image for the panel; showing it unstretched", ex);
            }
            ShowFrame(image);
        }

        private bool ShouldStretchForDisplay(PrepareImageParameters parameters) {
            if (parameters == null) {
                return profileService.ActiveProfile.ImageSettings.AutoStretch;
            }
            if (parameters.DetectStars == true) {
                return true;
            }
            return parameters.AutoStretch ?? profileService.ActiveProfile.ImageSettings.AutoStretch;
        }

        private void ShowFrame(BitmapSource image) {
            if (image == null) {
                Logger.Debug("ShowFrame skipped a null image");
                return;
            }
            var displayable = PanelBitmaps.MakeDisplayable(image);
            if (displayable == null) {
                return;
            }
            UiThread.Post(() => {
                PreparedImage = displayable;
                Logger.Debug("Panel image updated: " + PanelBitmaps.DescribeImage(displayable) + ", showsImage " + MainRegionShowsImage + ", planningPanel " + PlanningPanelVisible);
            });
        }

        private void AddCaptureEntry() {
            var filters = coordinator.RefreshAvailableFilters();
            var last = Session.CaptureEntries.LastOrDefault();
            var filter = last?.Filter;
            if (string.IsNullOrWhiteSpace(filter)) {
                filter = filters.FirstOrDefault() ?? string.Empty;
            }
            if (!coordinator.AddCaptureEntry(filter, last?.ExposureTime ?? 0, last?.FrameCount ?? 0)) {
                return;
            }
            LogClick("Add capture entry");
            RaiseCapturePlanState();
        }

        private void OnBenchmarkRequested(object sender, EventArgs e) {
            _ = RunBenchmarkAsync();
        }

        private async Task RunBenchmarkAsync() {
            if (isBenchmarking) {
                return;
            }
            if (IsRunning) {
                Notification.ShowWarning("Stop the run before benchmarking the camera");
                return;
            }
            isBenchmarking = true;
            UiThread.Post(() => RaisePropertyChanged(nameof(StatusLine)));
            RequeryCommands();
            var benchmark = new CameraBenchmarkService(cameraMediator, imagingMediator, profileService, fringeAnalysis);
            using (var cts = new CancellationTokenSource()) {
                try {
                    LogClick("Camera benchmark");
                    var reportPath = await benchmark.RunAsync(progress, cts.Token).ConfigureAwait(true);
                    Logger.Info("Camera benchmark finished, report at " + reportPath);
                } catch (Exception ex) {
                    Logger.Error("Camera benchmark failed", ex);
                    Notification.ShowError("Camera benchmark failed: " + ex.Message);
                } finally {
                    isBenchmarking = false;
                    progress.Report(new ApplicationStatus { Status = string.Empty });
                    UiThread.Post(() => { RaisePropertyChanged(nameof(StatusLine)); RequeryCommands(); });
                }
            }
        }

        private void TryCaptureEntry(CaptureEntry entry) {
            if (entry == null) {
                return;
            }
            LogClick("Try " + entry);
            _ = coordinator.TryCaptureEntryAsync(entry, CancellationToken.None);
        }

        private void RemoveCaptureEntry(CaptureEntry entry) {
            if (entry == null || !coordinator.RemoveCaptureEntry(entry)) {
                return;
            }
            LogClick("Remove capture entry");
            RaiseCapturePlanState();
        }

        private void RaiseCapturePlanState() {
            RaisePropertyChanged(nameof(CapturePlanVisible));
            RaisePropertyChanged(nameof(CapturePlanEditable));
            RaisePropertyChanged(nameof(CanRemoveCaptureEntry));
            RaisePropertyChanged(nameof(AvailableFilters));
            RaisePropertyChanged(nameof(HasFilters));
            RequeryCommands();
        }

        private void OnFringeUpdated(object sender, FringeAnalysisResult result) {
            UiThread.Post(() => {
                fringeResult = result;
                RaiseFringeState();
            });
        }

        private void RaiseFringeState() {
            RaisePropertyChanged(nameof(FringeImage));
            RaisePropertyChanged(nameof(FringeVisible));
            RaisePropertyChanged(nameof(FringeModeAverage));
            RaisePropertyChanged(nameof(FringeModeAutocorrelation));
        }

        public bool IsFringeDemoRunning => fringeDemoActive;

        private void OnFringeDemoRequested(object sender, EventArgs e) {
            UiThread.Post(() => {
                if (!fringeDemoActive) {
                    ToggleFringeDemo();
                }
            });
        }

        private void ToggleFringeDemo() {
            if (fringeDemoActive) {
                LogClick("Close fringe demo");
                CloseFringeDemo();
                return;
            }
            LogClick("Fringe demo");
            demoCts?.Dispose();
            demoCts = new CancellationTokenSource();
            fringeDemoActive = true;
            IsDrawerOpen = false;
            FringePaneEnabled = true;
            _ = RunFringeDemoAsync(demoCts.Token);
            RaiseDemoState();
        }

        private async Task RunFringeDemoAsync(CancellationToken ct) {
            var frames = FringeDemoData.Load();
            if (frames.Length == 0 || fringeAnalysis == null) {
                Notification.ShowWarning("The fringe demo data could not be loaded");
                fringeDemoActive = false;
                UiThread.Post(RaiseDemoState);
                return;
            }
            fringeAnalysis.Reset();
            var size = FringeDemoData.Size;
            fringeAnalysis.BeginRun(new FringeRunContext {
                Label = FringeDemoData.Label,
                RoiWidth = size,
                RoiHeight = size,
                ArcsecPerPixel = FringeDemoData.ArcsecPerPixel,
                ScaleSource = FringeDemoData.ScaleSource,
                KSpaceRadiusPx = FringeDemoData.KSpaceRadiusPx,
                TotalExposureCount = frames.Length
            });
            try {
                for (var i = 0; i < frames.Length; i++) {
                    ct.ThrowIfCancellationRequested();
                    ShowFrame(PanelBitmaps.MakeGrayBitmap(frames[i], size, size));
                    fringeAnalysis.Push(frames[i], size, size, null);
                    progress.Report(new ApplicationStatus { Status = FringeDemoData.Label + " - frame " + (i + 1) + " of " + frames.Length });
                    await Task.Delay(250, ct).ConfigureAwait(false);
                }
            } catch (OperationCanceledException) {
            } catch (Exception ex) {
                Logger.Error("Fringe demo failed", ex);
            } finally {
                fringeAnalysis.EndRun();
                progress.Report(new ApplicationStatus { Status = string.Empty });
                UiThread.Post(RaiseDemoState);
            }
            Logger.Info("Fringe demo finished");
        }

        private void RaiseDemoState() {
            RaisePropertyChanged(nameof(IsFringeDemoRunning));
            RaiseImageRegionState();
        }

        private void CloseFringeDemo() {
            if (!fringeDemoActive) {
                return;
            }
            demoCts?.Cancel();
            fringeDemoActive = false;
            fringeAnalysis?.Reset();
            PreparedImage = null;
            RaiseDemoState();
        }

        private void ResetFringes() {
            LogClick("Reset fringes");
            fringeAnalysis?.Reset();
        }

        private void SetFringeMode(string mode) {
            if (fringeAnalysis == null || !Enum.TryParse<FringePaneMode>(mode, true, out var parsed)) {
                return;
            }
            LogClick("Fringe mode " + parsed);
            fringeAnalysis.PaneMode = parsed;
            optionsProvider.Current.FringePaneMode = (int)parsed;
            RaiseFringeState();
        }

        private void RaiseImageRegionState() {
            RaisePropertyChanged(nameof(MainRegionShowsImage));
            RaisePropertyChanged(nameof(PointingOverlayVisible));
            RaisePropertyChanged(nameof(PointingPulseVisible));
            RaisePropertyChanged(nameof(PointingOverlayRa));
            RaisePropertyChanged(nameof(PointingOverlayDec));
            RaisePropertyChanged(nameof(FringeVisible));
            RaisePropertyChanged(nameof(PlanningPanelVisible));
            RaisePropertyChanged(nameof(ImagePlaceholderVisible));
            RaisePropertyChanged(nameof(PixelReadoutVisible));
            RaisePropertyChanged(nameof(CrosshairVisible));
            RaisePropertyChanged(nameof(CrosshairSize));
            RaisePropertyChanged(nameof(CrosshairThickness));
            RaisePropertyChanged(nameof(CrosshairLeft));
            RaisePropertyChanged(nameof(CrosshairTop));
            RaisePropertyChanged(nameof(KeplerVisible));
            RaisePropertyChanged(nameof(RunChromeVisible));
            RaisePropertyChanged(nameof(StatusLineVisible));
            RaisePropertyChanged(nameof(ChainVisible));
            RaisePropertyChanged(nameof(PrimaryActionVisible));
        }

        private double ShortestImageSide {
            get {
                var image = PreparedImage;
                return image == null ? 0d : Math.Min(image.PixelWidth, image.PixelHeight);
            }
        }

        private Point CrosshairCenter {
            get {
                var image = PreparedImage;
                if (image == null) {
                    return new Point(0d, 0d);
                }
                var roi = Session.Roi;
                var matchesRoiFrame = Math.Abs(Session.RoiFrameWidth - image.PixelWidth) < 1d
                    && Math.Abs(Session.RoiFrameHeight - image.PixelHeight) < 1d;
                if (matchesRoiFrame
                    && roi != null
                    && roi.Width > 0d
                    && roi.Height > 0d
                    && (roi.X > 0d || roi.Y > 0d)
                    && roi.X + roi.Width <= image.PixelWidth
                    && roi.Y + roi.Height <= image.PixelHeight) {
                    return new Point(roi.X + roi.Width / 2d, roi.Y + roi.Height / 2d);
                }
                return new Point(image.PixelWidth / 2d, image.PixelHeight / 2d);
            }
        }

        private void SamplePixel(Point position) {
            var image = PreparedImage;
            var pixels = rawPixels;
            if (image == null || pixels == null || rawWidth <= 0 || rawHeight <= 0) {
                return;
            }
            if (image.PixelWidth != rawWidth || image.PixelHeight != rawHeight) {
                return;
            }
            var pixelX = (int)position.X;
            var pixelY = (int)position.Y;
            if (pixelX < 0 || pixelY < 0 || pixelX >= rawWidth || pixelY >= rawHeight) {
                return;
            }
            var index = pixelX + pixelY * rawWidth;
            if (index < 0 || index >= pixels.Length) {
                return;
            }
            PixelReadout = "x " + pixelX.ToString(CultureInfo.InvariantCulture)
                + "  y " + pixelY.ToString(CultureInfo.InvariantCulture)
                + "  ADU " + pixels[index].ToString(CultureInfo.InvariantCulture);
        }

        private void SetRawPixels(IRenderedImage rendered) {
            try {
                var data = rendered?.RawImageData;
                var properties = data?.Properties;
                var array = data?.Data?.FlatArray;
                if (array == null || properties == null || properties.Width <= 0 || properties.Height <= 0) {
                    rawPixels = null;
                    rawWidth = 0;
                    rawHeight = 0;
                    return;
                }
                rawPixels = array;
                rawWidth = properties.Width;
                rawHeight = properties.Height;
            } catch (Exception ex) {
                Logger.Error("Could not keep the raw pixel data for the readout", ex);
                rawPixels = null;
                rawWidth = 0;
                rawHeight = 0;
            }
        }

        private void RebuildTargets() {
            var snapshot = coordinator.Targets.ToList();
            var lists = snapshot
                .Where(target => !string.IsNullOrEmpty(target.SourceList))
                .GroupBy(target => target.SourceList, StringComparer.OrdinalIgnoreCase)
                .Select(group => new LoadedListVM(group.Key, group.Count()))
                .ToList();
            for (var index = 0; index < snapshot.Count; index++) {
                snapshot[index].PlanNumber = index + 1;
            }
            UiThread.Post(() => {
                Targets.Clear();
                foreach (var target in snapshot) {
                    Targets.Add(target);
                }
                LoadedLists.Clear();
                foreach (var list in lists) {
                    LoadedLists.Add(list);
                }
                ResetTemplateSnapshot();
                RaisePropertyChanged(nameof(NoTargetsMessageVisible));
                RaisePropertyChanged(nameof(QueueEmpty));
                RaisePropertyChanged(nameof(LoadListLabel));
                RaisePropertyChanged(nameof(LoadListTooltip));
            });
        }

        private void ResetTemplateSnapshot() {
            templateSnapshot.Clear();
            foreach (var target in Targets) {
                templateSnapshot[target] = new[] { target.Template ?? string.Empty, target.TemplateRef ?? string.Empty };
            }
        }

        private void LogTemplateEdits() {
            foreach (var target in Targets.ToList()) {
                var template = target.Template ?? string.Empty;
                var templateRef = target.TemplateRef ?? string.Empty;
                if (!templateSnapshot.TryGetValue(target, out var previous)) {
                    templateSnapshot[target] = new[] { template, templateRef };
                    continue;
                }
                if (!string.Equals(previous[0], template, StringComparison.Ordinal)) {
                    Logger.Info("UI: Template changed for " + target.Name + " - old '" + previous[0] + "' new '" + template + "'");
                }
                if (!string.Equals(previous[1], templateRef, StringComparison.Ordinal)) {
                    Logger.Info("UI: Reference template changed for " + target.Name + " - old '" + previous[1] + "' new '" + templateRef + "'");
                }
                templateSnapshot[target] = new[] { template, templateRef };
            }
        }

        private void RebuildQueue() {
            var snapshot = coordinator.PinQueue.ToList();
            UiThread.Post(() => {
                UpNext.Clear();
                foreach (var target in snapshot) {
                    UpNext.Add(target);
                }
            });
        }

        private void LoadTemplates() {
            List<string> wanted;
            try {
                if (!sequenceMediator.Initialized) {
                    return;
                }
                wanted = sequenceMediator.GetDeepSkyObjectContainerTemplates()
                    .Select(target => target.Name)
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(name => name, StringComparer.Ordinal)
                    .ToList();
            } catch (Exception ex) {
                Logger.Error(ex);
                return;
            }
            UiThread.Post(() => {
                foreach (var stale in Templates.Where(name => !wanted.Contains(name)).ToList()) {
                    Templates.Remove(stale);
                }
                for (var i = 0; i < wanted.Count; i++) {
                    if (i >= Templates.Count) {
                        Templates.Add(wanted[i]);
                    } else if (!string.Equals(Templates[i], wanted[i], StringComparison.Ordinal)) {
                        Templates.Insert(i, wanted[i]);
                    }
                }
            });
        }

        private void RecomputeSlewDelta() {
            var pending = coordinator.Gate.Pending;
            slewDeltaArcmin = pending != null && pending.Kind == GateKind.SlewConfirmation && pending.Coordinates != null && HasUsableMountPosition
                ? SkySeparation.Arcmin(telescopeInfo.Coordinates, pending.Coordinates)
                : double.NaN;
        }

        private bool HasUsableMountPosition {
            get {
                if (telescopeInfo == null || !telescopeInfo.Connected || telescopeInfo.AtPark) {
                    return false;
                }
                if (string.Equals(telescopeInfo.DeviceId, ManualTelescope.DeviceId, StringComparison.Ordinal)) {
                    return false;
                }
                var coordinates = telescopeInfo.Coordinates;
                if (coordinates == null || double.IsNaN(coordinates.RADegrees) || double.IsNaN(coordinates.Dec)) {
                    return false;
                }
                return coordinates.RADegrees != 0d || coordinates.Dec != 0d;
            }
        }

        private static string FormatSeparation(double arcmin) {
            return arcmin >= 60d
                ? (arcmin / 60d).ToString("F1", CultureInfo.InvariantCulture) + " deg"
                : arcmin.ToString("F1", CultureInfo.InvariantCulture) + " arcmin";
        }

        private void RecordSnapshotToRecent() {
            if (Targets.Count == 0) {
                return;
            }
            var directory = profileService.ActiveProfile?.ImageFileSettings?.FilePath;
            if (string.IsNullOrWhiteSpace(directory)) {
                return;
            }
            var path = Path.Combine(directory, "TargetList-" + DateTime.Now.AddHours(-12).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".csv");
            if (File.Exists(path)) {
                AddRecent(path);
            }
        }

        private void AddRecent(string path) {
            if (string.IsNullOrWhiteSpace(path)) {
                return;
            }
            var list = new List<string>();
            list.Add(path);
            foreach (var existing in RecentTargetLists) {
                if (!string.Equals(existing, path, StringComparison.OrdinalIgnoreCase) && list.Count < 5) {
                    list.Add(existing);
                }
            }
            optionsProvider.Current.RecentTargetLists = string.Join("|", list);
        }

        private void PlaySlewSound() {
            _ = Task.Run(() => {
                try {
                    using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("SpeckleSlew.wav")) {
                        if (stream == null) {
                            return;
                        }
                        using (var player = new SoundPlayer(stream)) {
                            player.PlaySync();
                        }
                    }
                } catch (Exception ex) {
                    Logger.Error(ex);
                }
            });
        }

        private void RequeryCommands() {
            UiThread.Post(() => {
                (LoadListCommand as RelayCommand)?.RaiseCanExecuteChanged();
                (ExportCsvCommand as RelayCommand)?.RaiseCanExecuteChanged();
                (PrimaryActionCommand as RelayCommand)?.RaiseCanExecuteChanged();
                (PauseResumeCommand as RelayCommand)?.RaiseCanExecuteChanged();
                (StopRunCommand as RelayCommand)?.RaiseCanExecuteChanged();
                (SkipTargetCommand as RelayCommand)?.RaiseCanExecuteChanged();
                (SkipReferenceCommand as RelayCommand)?.RaiseCanExecuteChanged();
                (SkipStepCommand as RelayCommand)?.RaiseCanExecuteChanged();
                (JumpToStageCommand as StepCommand)?.RaiseCanExecuteChanged();
                (SlewWithMountCommand as RelayCommand)?.RaiseCanExecuteChanged();
                (SwitchWidePathCommand as RelayCommand)?.RaiseCanExecuteChanged();
                (SwitchSciencePathCommand as RelayCommand)?.RaiseCanExecuteChanged();
                (ToggleDoneCommand as TargetCommand)?.RaiseCanExecuteChanged();
                (QueueNextCommand as TargetCommand)?.RaiseCanExecuteChanged();
                (AddCaptureEntryCommand as RelayCommand)?.RaiseCanExecuteChanged();
                (RemoveCaptureEntryCommand as EntryCommand)?.RaiseCanExecuteChanged();
            });
        }

        private void BuildChains() {
            ChainStateBuilder.Build(TargetSteps, ReferenceSteps);
        }

        private void UpdateChain() {
            var pending = coordinator.Gate.Pending;
            var phase = Session.Phase == WorkflowPhase.Faulted && pending != null ? pending.Phase : Session.Phase;
            var faulted = Session.Phase == WorkflowPhase.Faulted || pending?.Kind == GateKind.Error;
            ChainStateBuilder.Update(TargetSteps, ReferenceSteps, phase, Session.ImagingReference, IsRunning, faulted, ReferenceLegVisible);
        }

        private void RaiseAll() {
            RaisePropertyChanged(string.Empty);
        }
    }

}
