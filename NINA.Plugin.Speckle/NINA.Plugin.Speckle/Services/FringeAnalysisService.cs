using NINA.Core.Utility;
using NINA.Plugin.Speckle.Imaging;
using NINA.Profile.Interfaces;
using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Windows.Media.Imaging;

namespace NINA.Plugin.Speckle.Services {

    [Export(typeof(IFringeAnalysisService))]
    [PartCreationPolicy(CreationPolicy.Shared)]
    public sealed class FringeAnalysisService : IFringeAnalysisService, IDisposable {
        public const int MinFftSize = 64;
        public const int MaxFftSize = 1024;
        public const double DetectionSnrThreshold = 5.0;
        public const int DetectionMinFrames = 50;
        public const double DetectionMinOffsetPx = 2.0;
        public const double DetectionMinSeparationFactor = 1.5;
        public const double DetectionMaxSeparationArcsec = 3.0;
        public const double LowFrequencyMaskRadius = 3.0;

        private const int QueueCapacity = 12;

        private enum ItemKind {
            Frame,
            Begin,
            End,
            Reset,
            Render
        }

        private sealed class WorkItem {
            public ItemKind Kind { get; init; }
            public ushort[] Buffer { get; init; }
            public int Size { get; init; }
            public FringeRunContext Context { get; init; }
            public FringeOptions Options { get; init; }
        }

        private readonly ISpeckleOptionsProvider optionsProvider;
        private readonly IProfileService profileService;
        private readonly object lifecycle = new object();

        private BlockingCollection<WorkItem> queue;
        private Thread worker;
        private CancellationTokenSource cts;
        private int framesWaiting;

        private volatile FringeOptions pushOptions = FringeOptions.Defaults;
        private volatile bool accepting;
        private int pushFrameIndex;
        private int droppedFrames;
        private int capturedFrames;
        private int dropReported;
        private FringePaneMode paneMode = FringePaneMode.Average;

        private FringeRunContext context;
        private FringeOptions options = FringeOptions.Defaults;
        private PowerSpectrumAccumulator accumulator;
        private RadialGrid grid;
        private double[] window;
        private double[] psdAverage;
        private double[] display;
        private double[] work;
        private double[] scratch;
        private double[] frameScratch;
        private double[] lineScratch;
        private byte[] gray;
        private double[] referencePsd;
        private int runSize;
        private bool sizeMismatchLogged;
        private bool dirty;
        private double arcsecPerPixel;
        private string scaleSource = "unknown";
        private readonly Stopwatch renderClock = new Stopwatch();

        [ImportingConstructor]
        public FringeAnalysisService(ISpeckleOptionsProvider optionsProvider, IProfileService profileService) {
            this.optionsProvider = optionsProvider;
            this.profileService = profileService;
        }

        public event EventHandler<FringeAnalysisResult> Updated;

        public bool IsEnabled => CurrentOptions().Enabled;

        public FringeAnalysisResult Latest { get; private set; }

        public string CachedReferenceLabel { get; private set; }

        public FringePaneMode PaneMode {
            get => paneMode;
            set {
                if (paneMode == value) {
                    return;
                }
                paneMode = value;
                Enqueue(new WorkItem { Kind = ItemKind.Render });
            }
        }

        public void BeginRun(FringeRunContext runContext) {
            var current = FringeOptions.From(optionsProvider?.Current);
            if (!current.Enabled) {
                accepting = false;
                return;
            }
            pushOptions = current;
            Interlocked.Exchange(ref pushFrameIndex, 0);
            Interlocked.Exchange(ref droppedFrames, 0);
            Interlocked.Exchange(ref capturedFrames, 0);
            Interlocked.Exchange(ref dropReported, 0);
            EnsureWorker();
            accepting = true;
            Enqueue(new WorkItem { Kind = ItemKind.Begin, Context = runContext, Options = current });
        }

        public void Push(ushort[] pixels, int width, int height, ObservableRectangle roi) {
            if (!accepting || pixels == null || width <= 0 || height <= 0) {
                return;
            }
            var localQueue = queue;
            if (localQueue == null) {
                return;
            }
            Interlocked.Increment(ref capturedFrames);
            var current = pushOptions;
            var every = current.AnalyseEveryNthFrame;
            var index = Interlocked.Increment(ref pushFrameIndex);
            if (every > 1 && index % every != 0) {
                return;
            }
            var size = FrameCrop.ChooseSize(width, height, current.FftSize, MinFftSize, MaxFftSize);
            if (size <= 0) {
                return;
            }
            if (Volatile.Read(ref framesWaiting) >= QueueCapacity) {
                Interlocked.Increment(ref droppedFrames);
                if (Interlocked.Exchange(ref dropReported, 1) == 0) {
                    Logger.Warning("Fringe analysis cannot keep up with the camera; analysis frames are being dropped. Capture and saving are unaffected.");
                }
                return;
            }
            var buffer = ArrayPool<ushort>.Shared.Rent(size * size);
            try {
                FrameCrop.Crop(pixels, width, height,
                               roi?.X ?? 0, roi?.Y ?? 0, roi?.Width ?? 0, roi?.Height ?? 0,
                               size, buffer);
            } catch (Exception ex) {
                ArrayPool<ushort>.Shared.Return(buffer);
                Logger.Error(ex);
                return;
            }
            var item = new WorkItem { Kind = ItemKind.Frame, Buffer = buffer, Size = size };
            var added = false;
            if (Volatile.Read(ref framesWaiting) < QueueCapacity) {
                Interlocked.Increment(ref framesWaiting);
                try {
                    added = localQueue.TryAdd(item);
                } catch (Exception) {
                    added = false;
                }
                if (!added) {
                    Interlocked.Decrement(ref framesWaiting);
                }
            }
            if (!added) {
                ArrayPool<ushort>.Shared.Return(buffer);
                Interlocked.Increment(ref droppedFrames);
                if (Interlocked.Exchange(ref dropReported, 1) == 0) {
                    Logger.Warning("Fringe analysis cannot keep up with the camera; analysis frames are being dropped. Capture and saving are unaffected.");
                }
            }
        }

        public void EndRun() {
            if (!accepting) {
                return;
            }
            accepting = false;
            Enqueue(new WorkItem { Kind = ItemKind.End });
        }

        public void Reset() {
            Enqueue(new WorkItem { Kind = ItemKind.Reset });
        }

        public void Dispose() {
            lock (lifecycle) {
                accepting = false;
                try {
                    cts?.Cancel();
                } catch (Exception) {
                }
                try {
                    queue?.CompleteAdding();
                } catch (Exception) {
                }
                if (worker != null && worker.IsAlive) {
                    worker.Join(2000);
                }
                worker = null;
                try {
                    queue?.Dispose();
                } catch (Exception) {
                }
                queue = null;
                Interlocked.Exchange(ref framesWaiting, 0);
                cts?.Dispose();
                cts = null;
            }
        }

        private FringeOptions CurrentOptions() {
            try {
                return FringeOptions.From(optionsProvider?.Current);
            } catch (Exception) {
                return FringeOptions.Defaults;
            }
        }

        private void EnsureWorker() {
            lock (lifecycle) {
                if (worker != null && worker.IsAlive) {
                    return;
                }
                cts?.Dispose();
                cts = new CancellationTokenSource();
                queue = new BlockingCollection<WorkItem>();
                Interlocked.Exchange(ref framesWaiting, 0);
                var token = cts.Token;
                worker = new Thread(() => WorkerLoop(token)) {
                    Name = "SpeckleFringeWorker",
                    IsBackground = true,
                    Priority = ThreadPriority.BelowNormal
                };
                worker.Start();
            }
        }

        private void Enqueue(WorkItem item) {
            var localQueue = queue;
            if (localQueue == null) {
                return;
            }
            try {
                localQueue.Add(item);
            } catch (Exception) {
            }
        }

        private void WorkerLoop(CancellationToken token) {
            var localQueue = queue;
            while (!token.IsCancellationRequested) {
                WorkItem item = null;
                try {
                    if (!localQueue.TryTake(out item, 100, token)) {
                        Process(null);
                        continue;
                    }
                } catch (OperationCanceledException) {
                    break;
                } catch (ObjectDisposedException) {
                    break;
                } catch (InvalidOperationException) {
                    break;
                }
                if (item.Kind == ItemKind.Frame) {
                    Interlocked.Decrement(ref framesWaiting);
                }
                try {
                    Process(item);
                } catch (Exception ex) {
                    Logger.Error(ex);
                    ReturnBuffer(item);
                }
            }
            DrainQueue(localQueue);
        }

        private void Process(WorkItem item) {
            if (item == null) {
                MaybeRender();
                return;
            }
            switch (item.Kind) {
                case ItemKind.Begin:
                    StartLeg(item);
                    break;

                case ItemKind.Frame:
                    ProcessFrame(item);
                    break;

                case ItemKind.End:
                    FinishLeg();
                    break;

                case ItemKind.Reset:
                    accumulator?.Reset();
                    dirty = false;
                    Publish(BuildResult(null, true));
                    break;

                case ItemKind.Render:
                    dirty = true;
                    MaybeRender(true);
                    break;
            }
        }

        private void StartLeg(WorkItem item) {
            context = item.Context;
            options = item.Options ?? FringeOptions.Defaults;
            runSize = 0;
            sizeMismatchLogged = false;
            dirty = false;
            accumulator?.Reset();
            window = null;
            ResolveScale();
            renderClock.Restart();
        }

        private void ProcessFrame(WorkItem item) {
            try {
                if (runSize == 0) {
                    runSize = item.Size;
                    Allocate(item.Size);
                } else if (item.Size != runSize) {
                    if (!sizeMismatchLogged) {
                        sizeMismatchLogged = true;
                        Logger.Warning("Fringe analysis dropped a frame of size " + item.Size + " because the run started at size " + runSize);
                    }
                    return;
                }
                LineBias.Widen(item.Buffer, frameScratch, runSize * runSize);
                LineBias.Remove(frameScratch, runSize, lineScratch);
                accumulator.AddFrame(frameScratch, options.SubtractFrameMean, window);
                dirty = true;
            } finally {
                ReturnBuffer(item);
            }
            MaybeRender();
        }

        private void FinishLeg() {
            if (accumulator != null && accumulator.FrameCount > 0) {
                var result = Render(true);
                if (context != null && context.IsReference && psdAverage != null) {
                    referencePsd = (double[])psdAverage.Clone();
                    CachedReferenceLabel = context.Label;
                }
                if (result != null) {
                    Publish(result);
                }
            } else {
                Logger.Warning("Fringe analysis finished with no frames analysed for " + (context?.Label ?? "unknown"));
                Publish(BuildResult(null, true));
            }
            var dropped = Volatile.Read(ref droppedFrames);
            var captured = Volatile.Read(ref capturedFrames);
            var analysed = accumulator?.FrameCount ?? 0;
            Logger.Info("Fringe analysis finished for " + (context?.Label ?? "unknown")
                        + ": analysed " + analysed + " of " + captured + " frames, dropped " + dropped);
            dirty = false;
        }

        private void Allocate(int size) {
            if (grid == null || grid.Size != size) {
                grid = new RadialGrid(size);
            }
            if (accumulator == null || accumulator.Size != size) {
                accumulator = new PowerSpectrumAccumulator(size);
                var length = size * size;
                psdAverage = new double[length];
                display = new double[length];
                work = new double[length];
                scratch = new double[length];
                frameScratch = new double[length];
                lineScratch = new double[size];
                gray = new byte[length];
            } else {
                accumulator.Reset();
            }
            window = options.Apodization switch {
                FringeApodizationMode.Hann => Apodization.Hann(size),
                FringeApodizationMode.Tukey => Apodization.Tukey(size, Apodization.DefaultTukeyAlpha),
                _ => null
            };
        }

        private void MaybeRender(bool force = false) {
            if (!dirty || accumulator == null || accumulator.FrameCount == 0) {
                return;
            }
            if (!force && renderClock.IsRunning && renderClock.ElapsedMilliseconds < options.RefreshMs) {
                return;
            }
            var result = Render(false);
            if (result != null) {
                Publish(result);
            }
            renderClock.Restart();
            dirty = false;
        }

        private FringeAnalysisResult Render(bool final) {
            if (accumulator == null || accumulator.FrameCount == 0) {
                return null;
            }
            var size = accumulator.Size;
            var kRadius = ResolveKSpaceRadius(size);

            accumulator.CopyMeanPsd(psdAverage);
            Fft.FftShift(psdAverage, size);
            var averagePedestal = 0.0;
            if (options.RemoveNoiseBias && kRadius >= 5.0) {
                averagePedestal = PowerSpectrum.RemovePhotonBias(psdAverage, grid, kRadius);
            }

            var exclusionRadius = FringeMetrics.ExclusionRadius(size, kRadius);
            var detection = FringeMetrics.DetectOffset(psdAverage, grid, accumulator.Transform,
                                                       exclusionRadius,
                                                       DetectionMaxRadius(size, exclusionRadius),
                                                       LowFrequencyMaskRadius, work, scratch);

            var signalRadius = kRadius > 0.0 ? kRadius : size / 2.0 - 1.0;
            var mode = paneMode;
            int rendered;
            if (mode == FringePaneMode.Autocorrelation) {
                PowerSpectrum.Autocorrelation(psdAverage, size, accumulator.Transform, display, scratch);
                PowerSpectrum.RadialSubtract(display, grid, PowerSpectrum.DefaultFlattenSmooth, display);
                rendered = size;
                FringeRenderer.PercentileClipWithin(display, scratch, FringeRenderer.AutocorrelationLowPercentile,
                                                    FringeRenderer.AutocorrelationHighPercentile, null, 0.0);
                FringeRenderer.ToGray8(display, gray);
            } else {
                var displayPedestal = averagePedestal;
                if (mode == FringePaneMode.Frame) {
                    accumulator.CopyLastPsd(display);
                    Fft.FftShift(display, size);
                    displayPedestal = 0.0;
                    if (options.RemoveNoiseBias && kRadius >= 5.0) {
                        displayPedestal = PowerSpectrum.RemovePhotonBias(display, grid, kRadius);
                    }
                } else {
                    Array.Copy(psdAverage, display, display.Length);
                }
                if (options.RadialFlatten) {
                    PowerSpectrum.RadialFlatten(display, grid, PowerSpectrum.DefaultFlattenSmooth,
                                                PowerSpectrum.DefaultFlattenFloorFraction, displayPedestal, display);
                }
                FringeRenderer.BuildPaneDisplay(display, grid, scratch, options.Stretch, options.MaskDcRadiusPx,
                                                signalRadius, detection.PeriodPx);
                rendered = size;
                FringeRenderer.ToGray8(display, gray);
            }

            var image = FringeRenderer.CreateGray8Bitmap(gray, rendered);
            return BuildResult(image, final, detection, kRadius);
        }

        private double DetectionMaxRadius(int size, double exclusionRadius) {
            var bound = size / 3.0;
            if (arcsecPerPixel > 0.0) {
                bound = Math.Min(bound, DetectionMaxSeparationArcsec / arcsecPerPixel);
            }
            return Math.Max(bound, exclusionRadius + 2.0);
        }

        private FringeAnalysisResult BuildResult(BitmapSource image, bool final,
                                                 FringeDetection detection = null, double kRadius = 0.0) {
            var frames = accumulator?.FrameCount ?? 0;
            var captured = Volatile.Read(ref capturedFrames);
            var dropped = Volatile.Read(ref droppedFrames);
            var size = accumulator?.Size ?? 0;
            var limit = PowerSpectrum.DiffractionLimitArcsec(options.ApertureMillimetres / 1000.0, options.WavelengthNm * 1e-9);

            double? period = null;
            double? angle = null;
            double? separation = null;
            double? visibility = null;
            double? ratio = null;
            double? deltaMag = null;
            double? snr = null;
            var detected = false;

            if (detection != null && detection.Offset > 0.0 && !double.IsInfinity(detection.PeriodPx)) {
                period = detection.PeriodPx;
                angle = detection.PositionAngleDeg;
                snr = detection.LobeSnr;
                if (arcsecPerPixel > 0.0) {
                    separation = detection.Offset * arcsecPerPixel;
                }
                var highPass = Math.Clamp(detection.PeriodPx, 3.0, kRadius > 0.0 ? 0.5 * kRadius : 3.0);
                var lowPass = kRadius > 0.0 ? kRadius : size / 2.0;
                var measuredVisibility = FringeMetrics.FringeVisibility(psdAverage, grid, detection.PeriodPx, detection.PositionAngleDeg,
                                                       lowPass, highPass, out _);
                visibility = measuredVisibility;
                if (measuredVisibility > 0.0) {
                    var brightnessRatio = FringeMetrics.RatioFromVisibility(measuredVisibility);
                    ratio = brightnessRatio;
                    deltaMag = FringeMetrics.MagnitudeDifference(brightnessRatio);
                }
                detected = detection.LobeSnr >= DetectionSnrThreshold
                           && detection.Offset >= DetectionMinOffsetPx
                           && frames >= DetectionMinFrames
                           && (limit <= 0.0 || separation == null || separation.Value >= DetectionMinSeparationFactor * limit);
            }

            var result = new FringeAnalysisResult {
                Image = image,
                Label = context?.Label ?? string.Empty,
                Mode = paneMode,
                Size = size,
                FramesAnalysed = frames,
                FramesCaptured = captured,
                FramesDropped = dropped,
                IsFinal = final,
                Detected = detected,
                PeriodPx = period,
                PositionAngleDeg = angle,
                SeparationArcsec = separation,
                Visibility = visibility,
                BrightnessRatio = ratio,
                MagnitudeDifference = deltaMag,
                DetectionSnr = snr,
                ArcsecPerPixel = arcsecPerPixel > 0.0 ? arcsecPerPixel : (double?)null,
                DiffractionLimitArcsec = limit > 0.0 ? limit : (double?)null,
                KSpaceRadiusPx = kRadius > 0.0 ? kRadius : (double?)null,
                ScaleSource = scaleSource
            };
            return result with {
                MetricsLine = FringeText.MetricsLine(result),
                ScaleLine = FringeText.ScaleLine(result)
            };
        }

        private void Publish(FringeAnalysisResult result) {
            Latest = result;
            try {
                Updated?.Invoke(this, result);
            } catch (Exception ex) {
                Logger.Error(ex);
            }
        }

        private double ResolveKSpaceRadius(int size) {
            if (context != null && context.KSpaceRadiusPx > 0.0) {
                return Math.Min(context.KSpaceRadiusPx, size / 2.0 - 1.0);
            }
            var radius = options.KSpaceRadiusPx > 0.0
                ? options.KSpaceRadiusPx
                : PowerSpectrum.KSpaceRadius(size, arcsecPerPixel, options.ApertureMillimetres / 1000.0, options.WavelengthNm * 1e-9);
            if (radius <= 0.0) {
                return 0.0;
            }
            return Math.Min(radius, size / 2.0 - 1.0);
        }

        private void ResolveScale() {
            if (context != null && context.ArcsecPerPixel > 0.0) {
                arcsecPerPixel = context.ArcsecPerPixel;
                scaleSource = string.IsNullOrWhiteSpace(context.ScaleSource) ? "plate solve" : context.ScaleSource;
                return;
            }
            var pixelSize = 0.0;
            var focalLength = 0.0;
            try {
                var profile = profileService?.ActiveProfile;
                if (profile != null) {
                    pixelSize = profile.CameraSettings.PixelSize;
                    focalLength = profile.TelescopeSettings.FocalLength;
                }
            } catch (Exception) {
            }
            var speckle = optionsProvider?.Current;
            if (speckle != null && speckle.Focallength > 0.0) {
                var barlow = speckle.BarlowFactor > 0.0 ? speckle.BarlowFactor : 1.0;
                focalLength = speckle.Focallength * barlow;
            }
            var binning = context != null && context.Binning > 0.0 ? context.Binning : 1.0;
            var scale = PowerSpectrum.ArcsecPerPixel(pixelSize, focalLength, binning);
            arcsecPerPixel = scale;
            scaleSource = scale > 0.0 ? "optics" : "unknown";
        }

        private static void ReturnBuffer(WorkItem item) {
            if (item?.Buffer != null) {
                ArrayPool<ushort>.Shared.Return(item.Buffer);
            }
        }

        private static void DrainQueue(BlockingCollection<WorkItem> localQueue) {
            if (localQueue == null) {
                return;
            }
            try {
                while (localQueue.TryTake(out var pending)) {
                    ReturnBuffer(pending);
                }
            } catch (Exception) {
            }
        }
    }

    public static class FringeText {

        public static string MetricsLine(FringeAnalysisResult result) {
            if (result == null) {
                return string.Empty;
            }
            if (result.FramesAnalysed == 0) {
                return "no frames analysed";
            }
            var parts = new System.Collections.Generic.List<string>();
            if (result.DetectionSnr.HasValue) {
                parts.Add("fringes " + (result.Detected ? "yes" : "no") + " " + Format(result.DetectionSnr.Value, 1) + " sigma");
            } else {
                parts.Add("fringes no");
            }
            if (result.Detected && result.PeriodPx.HasValue) {
                parts.Add("period " + Format(result.PeriodPx.Value, 1) + " px");
            }
            if (result.Detected && result.PositionAngleDeg.HasValue) {
                var pa = result.PositionAngleDeg.Value;
                parts.Add("PA " + Format(pa, 0) + " or " + Format(pa + 180.0, 0) + " deg");
            }
            if (result.Detected && result.SeparationArcsec.HasValue) {
                parts.Add("sep " + Format(result.SeparationArcsec.Value, 3) + " arcsec");
            }
            if (result.Detected && result.Visibility.HasValue) {
                parts.Add("vis " + Format(result.Visibility.Value, 2));
            }
            if (result.Detected && result.MagnitudeDifference.HasValue && !double.IsNaN(result.MagnitudeDifference.Value)) {
                parts.Add("dmag " + Format(result.MagnitudeDifference.Value, 1));
            }
            parts.Add(result.FramesAnalysed + " frames");
            return string.Join(" | ", parts);
        }

        public static string ScaleLine(FringeAnalysisResult result) {
            if (result == null) {
                return string.Empty;
            }
            var parts = new System.Collections.Generic.List<string>();
            if (result.ArcsecPerPixel.HasValue) {
                parts.Add("scale " + Format(result.ArcsecPerPixel.Value, 4) + " arcsec/px (" + result.ScaleSource + ")");
            } else {
                parts.Add("scale unknown");
            }
            if (result.DiffractionLimitArcsec.HasValue) {
                parts.Add("limit " + Format(result.DiffractionLimitArcsec.Value, 4) + " arcsec");
            }
            if (result.Size > 0) {
                parts.Add("N " + result.Size);
            }
            if (result.FramesDropped > 0) {
                parts.Add("analysed " + result.FramesAnalysed + " of " + result.FramesCaptured);
            }
            return string.Join(" | ", parts);
        }

        private static string Format(double value, int decimals) {
            return value.ToString("F" + decimals, CultureInfo.InvariantCulture);
        }
    }
}
