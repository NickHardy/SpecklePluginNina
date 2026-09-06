using NINA.Astrometry;
using NINA.Core.Model;
using NINA.Core.Model.Equipment;
using NINA.Equipment.Model;
using NINA.Plugin.Speckle.Services;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Plugin.Speckle.Workflow {

    public class WorkflowRunOptions {
        public SchedulingOptions Scheduling { get; init; } = new SchedulingOptions();
        public ReferenceStarQueryOptions ReferenceQuery { get; init; } = new ReferenceStarQueryOptions();
        public TargetPlanDefaults PlanDefaults { get; init; } = new TargetPlanDefaults();
        public bool AutoSkipFailedReference { get; init; } = true;
        public bool DualCameraSetup { get; init; }
        public bool SaveCsvToFitsHeader { get; init; }
        public double RoiWidth { get; init; } = 512;
        public double RoiHeight { get; init; } = 512;
        public bool PlatesolveRoi { get; init; } = true;
        public double ExposureTimeMax { get; init; } = 1;
        public BinningMode Binning { get; init; }
        public RoiSeriesMode VideoExposuresMode { get; init; } = RoiSeriesMode.Sequenced;
        public Func<RoiSeriesMode> ReadVideoExposuresMode { get; init; }
        public int DefaultExposures { get; init; } = 1000;
        public double DefaultExposureTime { get; init; } = 1;
        public double PreviewExposureTime { get; init; } = 1;
        public double Latitude { get; init; }
        public double Longitude { get; init; }
        public CustomHorizon Horizon { get; init; }
        public string SnapshotDirectory { get; init; }
        public Func<bool> ReadAutomaticallyRun { get; init; } = () => false;
        public Func<TargetPlan, InputTarget> InputTargetFactory { get; init; }
        public Func<string, FilterInfo> ResolveFilter { get; init; }
        public Func<int> ReadCameraBitDepth { get; init; } = () => 16;
        public Func<DateTime> Clock { get; init; } = () => DateTime.Now;
        public Func<TimeSpan, CancellationToken, Task> Delay { get; init; } = Task.Delay;
    }
}
