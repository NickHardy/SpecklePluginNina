using NINA.Plugin.Speckle.Imaging;
using System;
using System.Windows.Media.Imaging;

namespace NINA.Plugin.Speckle.Services {

    public sealed class FringeRunContext {
        public string Label { get; init; }
        public bool IsReference { get; init; }
        public double RoiX { get; init; }
        public double RoiY { get; init; }
        public double RoiWidth { get; init; }
        public double RoiHeight { get; init; }
        public double ArcsecPerPixel { get; init; }
        public string ScaleSource { get; init; }
        public double KSpaceRadiusPx { get; init; }
        public double Binning { get; init; } = 1.0;
        public int TotalExposureCount { get; init; }
    }

    public sealed record FringeAnalysisResult {
        public BitmapSource Image { get; init; }
        public string Label { get; init; }
        public FringePaneMode Mode { get; init; }
        public int Size { get; init; }
        public int FramesAnalysed { get; init; }
        public int FramesCaptured { get; init; }
        public int FramesDropped { get; init; }
        public bool IsFinal { get; init; }
        public bool Detected { get; init; }
        public double? PeriodPx { get; init; }
        public double? PositionAngleDeg { get; init; }
        public double? SeparationArcsec { get; init; }
        public double? Visibility { get; init; }
        public double? BrightnessRatio { get; init; }
        public double? MagnitudeDifference { get; init; }
        public double? DetectionSnr { get; init; }
        public double? ArcsecPerPixel { get; init; }
        public double? DiffractionLimitArcsec { get; init; }
        public double? KSpaceRadiusPx { get; init; }
        public string ScaleSource { get; init; }
        public string MetricsLine { get; init; }
        public string ScaleLine { get; init; }
    }

    public sealed class FringeOptions {
        public bool Enabled { get; init; } = true;
        public int FftSize { get; init; }
        public int AnalyseEveryNthFrame { get; init; } = 1;
        public int RefreshMs { get; init; } = 300;
        public bool SubtractFrameMean { get; init; } = true;
        public FringeApodizationMode Apodization { get; init; } = FringeApodizationMode.Tukey;
        public bool RemoveNoiseBias { get; init; } = true;
        public double KSpaceRadiusPx { get; init; }
        public double WavelengthNm { get; init; } = 620.0;
        public double ApertureMillimetres { get; init; }
        public bool RadialFlatten { get; init; } = true;
        public FringeStretchMode Stretch { get; init; } = FringeStretchMode.Asinh;
        public int MaskDcRadiusPx { get; init; } = 1;

        public static readonly FringeOptions Defaults = new FringeOptions();

        public static FringeOptions From(Speckle speckle) {
            if (speckle == null) {
                return Defaults;
            }
            return new FringeOptions {
                Enabled = speckle.AnalyseFringes,
                FftSize = speckle.FringeFftSize,
                AnalyseEveryNthFrame = Math.Max(1, speckle.FringeAnalyseEveryNthFrame),
                RefreshMs = Math.Clamp(speckle.FringeRefreshMs, 50, 5000),
                SubtractFrameMean = speckle.FringeSubtractFrameMean,
                Apodization = (FringeApodizationMode)speckle.FringeApodization,
                RemoveNoiseBias = speckle.FringeRemoveNoiseBias,
                KSpaceRadiusPx = speckle.FringeKSpaceRadiusPx,
                WavelengthNm = speckle.FringeWavelengthNm > 0 ? speckle.FringeWavelengthNm : 620.0,
                ApertureMillimetres = speckle.ApertureD,
                RadialFlatten = speckle.FringeRadialFlatten,
                Stretch = (FringeStretchMode)speckle.FringeStretch,
                MaskDcRadiusPx = Math.Clamp(speckle.FringeMaskDcRadiusPx, 0, 16)
            };
        }
    }
}
