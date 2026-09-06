using NINA.Astrometry;
using NINA.Core.Model;
using NINA.Core.Utility;
using NINA.Plugin.Speckle.Model;
using NINA.Plugin.Speckle.Services;
using System;
using System.Collections.Generic;

namespace NINA.Plugin.Speckle.Workflow {

    public class SpeckleWorkflowSession : BaseINPC {
        private WorkflowPhase phase = WorkflowPhase.Idle;
        private SpeckleTarget currentTarget;
        private ReferenceStar currentReference;
        private bool imagingReference;
        private TargetPlan currentPlan;
        private CaptureEntry activeCaptureEntry;
        private int captureEntryNumber;
        private int captureEntryCount;
        private bool capturePlanLocked;
        private IReadOnlyList<string> availableFilters = new List<string>();
        private InputTarget activeInputTarget;
        private ObservableRectangle roi;
        private double roiFrameWidth;
        private double roiFrameHeight;
        private double? orientation;
        private double? arcsecPerPix;
        private int speckleRun;
        private string activeFilter;
        private double activeExposureTime;
        private double calculatedExposureTime;
        private int targetNr;
        private int targetCount;
        private int frameCount;
        private int frameTotal;
        private double fps;
        private bool skipReference;
        private bool referenceLegAbandoned;
        private bool stopAfterCurrentTarget;
        private bool isHoldRequested;
        private bool isHolding;
        private LightPath? currentLightPath;
        private GateRequest pendingGate;
        private DateTime? windowOpensAt;
        private string sessionTitle;
        private int targetsImaged;
        private int targetsSkipped;
        private long totalFrames;

        public WorkflowPhase Phase {
            get => phase;
            set { phase = value; RaisePropertyChanged(); }
        }

        public SpeckleTarget CurrentTarget {
            get => currentTarget;
            set { currentTarget = value; RaisePropertyChanged(); }
        }

        public ReferenceStar CurrentReference {
            get => currentReference;
            set { currentReference = value; RaisePropertyChanged(); }
        }

        public bool ImagingReference {
            get => imagingReference;
            set { imagingReference = value; RaisePropertyChanged(); }
        }

        public TargetPlan CurrentPlan {
            get => currentPlan;
            set { currentPlan = value; RaisePropertyChanged(); }
        }

        public CaptureEntryCollection CaptureEntries { get; } = new CaptureEntryCollection();

        public CaptureEntry ActiveCaptureEntry {
            get => activeCaptureEntry;
            set { activeCaptureEntry = value; RaisePropertyChanged(); }
        }

        public int CaptureEntryNumber {
            get => captureEntryNumber;
            set { captureEntryNumber = value; RaisePropertyChanged(); }
        }

        public int CaptureEntryCount {
            get => captureEntryCount;
            set { captureEntryCount = value; RaisePropertyChanged(); }
        }

        public bool CapturePlanLocked {
            get => capturePlanLocked;
            set { capturePlanLocked = value; RaisePropertyChanged(); }
        }

        public IReadOnlyList<string> AvailableFilters {
            get => availableFilters;
            set { availableFilters = value ?? new List<string>(); RaisePropertyChanged(); }
        }

        public InputTarget ActiveInputTarget {
            get => activeInputTarget;
            set { activeInputTarget = value; RaisePropertyChanged(); }
        }

        public ObservableRectangle Roi {
            get => roi;
            set { roi = value; RaisePropertyChanged(); }
        }

        public double RoiFrameWidth {
            get => roiFrameWidth;
            set { roiFrameWidth = value; RaisePropertyChanged(); }
        }

        public double RoiFrameHeight {
            get => roiFrameHeight;
            set { roiFrameHeight = value; RaisePropertyChanged(); }
        }

        public double? Orientation {
            get => orientation;
            set { orientation = value; RaisePropertyChanged(); }
        }

        public double? ArcsecPerPix {
            get => arcsecPerPix;
            set { arcsecPerPix = value; RaisePropertyChanged(); }
        }

        public int SpeckleRun {
            get => speckleRun;
            set { speckleRun = value; RaisePropertyChanged(); }
        }

        public string ActiveFilter {
            get => activeFilter;
            set { activeFilter = value; RaisePropertyChanged(); }
        }

        public double ActiveExposureTime {
            get => activeExposureTime;
            set { activeExposureTime = value; RaisePropertyChanged(); }
        }

        public double CalculatedExposureTime {
            get => calculatedExposureTime;
            set { calculatedExposureTime = value; RaisePropertyChanged(); }
        }

        public int TargetNr {
            get => targetNr;
            set { targetNr = value; RaisePropertyChanged(); }
        }

        public int TargetCount {
            get => targetCount;
            set { targetCount = value; RaisePropertyChanged(); }
        }

        public int FrameCount {
            get => frameCount;
            set { frameCount = value; RaisePropertyChanged(); }
        }

        public int FrameTotal {
            get => frameTotal;
            set { frameTotal = value; RaisePropertyChanged(); }
        }

        public double Fps {
            get => fps;
            set { fps = value; RaisePropertyChanged(); }
        }

        public bool SkipReference {
            get => skipReference;
            set { skipReference = value; RaisePropertyChanged(); }
        }

        public bool ReferenceLegAbandoned {
            get => referenceLegAbandoned;
            set { referenceLegAbandoned = value; RaisePropertyChanged(); }
        }

        public bool StopAfterCurrentTarget {
            get => stopAfterCurrentTarget;
            set { stopAfterCurrentTarget = value; RaisePropertyChanged(); }
        }

        public bool IsHoldRequested {
            get => isHoldRequested;
            set { isHoldRequested = value; RaisePropertyChanged(); }
        }

        public bool IsHolding {
            get => isHolding;
            set { isHolding = value; RaisePropertyChanged(); }
        }

        public LightPath? CurrentLightPath {
            get => currentLightPath;
            set { currentLightPath = value; RaisePropertyChanged(); }
        }

        public GateRequest PendingGate {
            get => pendingGate;
            set { pendingGate = value; RaisePropertyChanged(); }
        }

        public DateTime? WindowOpensAt {
            get => windowOpensAt;
            set { windowOpensAt = value; RaisePropertyChanged(); }
        }

        public string SessionTitle {
            get => sessionTitle;
            set { sessionTitle = value; RaisePropertyChanged(); }
        }

        public int TargetsImaged {
            get => targetsImaged;
            set { targetsImaged = value; RaisePropertyChanged(); }
        }

        public int TargetsSkipped {
            get => targetsSkipped;
            set { targetsSkipped = value; RaisePropertyChanged(); }
        }

        public long TotalFrames {
            get => totalFrames;
            set { totalFrames = value; RaisePropertyChanged(); }
        }
    }
}
