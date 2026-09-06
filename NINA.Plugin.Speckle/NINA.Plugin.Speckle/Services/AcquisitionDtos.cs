using NINA.Astrometry;
using NINA.Core.Model;
using NINA.Core.Model.Equipment;
using NINA.Core.Utility;
using NINA.Image.ImageData;
using NINA.Plugin.Speckle.Model;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace NINA.Plugin.Speckle.Services {

    public class RoiPositionRequest {
        public int CaptureBinning { get; init; } = 1;
        public InputTarget Target { get; init; }
        public SpeckleTarget SpeckleTarget { get; init; }
        public string Title { get; init; }
        public double RoiWidth { get; init; }
        public double RoiHeight { get; init; }
        public bool PlatesolveFirst { get; init; }
        public bool ImageFlippedX { get; init; }
        public bool ImageFlippedY { get; init; }
        public double FallbackExposureTime { get; init; }
    }

    public class RoiPositionResult {
        public double? RoiX { get; init; }
        public double? RoiY { get; init; }
        public double? Orientation { get; init; }
        public double? ArcsecPerPix { get; init; }
        public bool PlatesolveSucceeded { get; init; }
        public string Note { get; init; }
        public double FrameWidth { get; init; }
        public double FrameHeight { get; init; }
    }

    public class ExposureCalibrationRequest {
        public ObservableRectangle Roi { get; init; }
        public double TargetAdu { get; init; }
        public double StepTime { get; init; }
        public double MaxTime { get; init; }
        public int Gain { get; init; }
        public int Offset { get; init; }
        public BinningMode Binning { get; init; }
        public string ImageType { get; init; }
        public int BitDepth { get; init; }
        public Action<int, double> OnIteration { get; init; }
    }

    public class ExposureEstimateRequest {
        public SpeckleTarget Target { get; init; }
        public bool IsRef { get; init; }
        public Telescope Telescope { get; init; }
        public Camera Camera { get; init; }
        public Barlow Barlow { get; init; }
        public double IntendedSNR { get; init; }
    }

    public enum RoiSeriesMode {
        Sequenced,
        Video
    }

    public class RoiSeriesRequest {
        public RoiSeriesMode Mode { get; init; }
        public double ExposureTime { get; init; }
        public int TotalExposureCount { get; init; }
        public int Gain { get; init; }
        public int Offset { get; init; }
        public BinningMode Binning { get; init; }
        public string ImageType { get; init; }
        public bool EnableSubSample { get; init; }
        public ObservableRectangle SubSampleRectangle { get; init; }
        public InputTarget Target { get; init; }
        public string Title { get; init; }
        public int SpeckleRun { get; set; }
        public List<IGenericMetaDataHeader> GenericHeaders { get; init; }
        public PauseGate Pause { get; init; }
        public Action<int> OnFrame { get; init; }
        public bool IsReference { get; init; }
        public double ArcsecPerPixel { get; init; }
        public string ScaleSource { get; init; }
    }

    public class ExposureRunResult {
        public int FramesCaptured { get; init; }
        public double Fps { get; init; }
        public Task PendingSaves { get; init; }
    }

    public class SingleExposureRequest {
        public double ExposureTime { get; init; }
        public int Gain { get; init; }
        public int Offset { get; init; }
        public BinningMode Binning { get; init; }
        public string ImageType { get; init; }
        public int ExposureCount { get; init; }
        public bool EnableSubSample { get; init; }
        public ObservableRectangle SubSampleRectangle { get; init; }
        public InputTarget Target { get; init; }
        public string Title { get; init; }
        public int SpeckleRun { get; init; }
        public List<IGenericMetaDataHeader> GenericHeaders { get; init; }
    }

    public class PreviewLoopRequest {
        public Func<double> GetExposureTime { get; init; }
        public IProgress<ApplicationStatus> LoopProgress { get; init; }
        public bool EnableSubSample { get; init; }
        public ObservableRectangle SubSampleRectangle { get; init; }
        public BinningMode Binning { get; init; }
        public int Gain { get; init; }
        public int Offset { get; init; }
    }

    public enum ClusterCenteringMode {
        Center,
        SynchLoop
    }

    public class ClusterSelection {
        public List<SimbadStarCluster> Candidates { get; init; }
        public SimbadStarCluster Chosen { get; init; }
    }

    public class ClusterCenteringRequest {
        public ClusterCenteringMode Mode { get; init; }
        public Coordinates TargetCoordinates { get; init; }
        public double SearchRadius { get; init; }
        public bool SlewBackToTarget { get; init; }
        public bool Zenith { get; init; }
        public bool Platesolve { get; init; }
        public Action<ClusterSelection> ClusterSelected { get; init; }
    }
}
