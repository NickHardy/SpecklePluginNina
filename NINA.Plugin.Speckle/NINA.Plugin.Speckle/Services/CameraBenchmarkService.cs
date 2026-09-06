using NINA.Core.Model;
using NINA.Core.Model.Equipment;
using NINA.Core.Utility;
using NINA.Core.Utility.Notification;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Equipment.Model;
using NINA.Image.FileFormat;
using NINA.Image.Interfaces;
using NINA.Profile.Interfaces;
using NINA.WPF.Base.Interfaces.Mediator;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Plugin.Speckle.Services {

    public class CameraBenchmarkService {
        private const int WarmupFrames = 6;
        private const int MeasuredFrames = 50;
        private const int BigFrameMeasuredFrames = 10;
        private const int SdkCallRepeats = 50;

        [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
        private static extern uint BeginTimerPeriod(uint milliseconds);

        [DllImport("winmm.dll", EntryPoint = "timeEndPeriod")]
        private static extern uint EndTimerPeriod(uint milliseconds);

        private readonly ICameraMediator cameraMediator;
        private readonly IImagingMediator imagingMediator;
        private readonly IProfileService profileService;
        private readonly IFringeAnalysisService fringeAnalysis;
        private readonly Progress<ApplicationStatus> quietProgress = new Progress<ApplicationStatus>(status => { });

        public CameraBenchmarkService(ICameraMediator cameraMediator,
                                      IImagingMediator imagingMediator,
                                      IProfileService profileService,
                                      IFringeAnalysisService fringeAnalysis) {
            this.cameraMediator = cameraMediator;
            this.imagingMediator = imagingMediator;
            this.profileService = profileService;
            this.fringeAnalysis = fringeAnalysis;
        }

        public async Task<string> RunAsync(IProgress<ApplicationStatus> progress, CancellationToken token) {
            var info = cameraMediator.GetInfo();
            if (info == null || !info.Connected) {
                throw new InvalidOperationException("Connect a camera before benchmarking it");
            }
            var cameraName = string.IsNullOrWhiteSpace(info.Name) ? "camera" : info.Name;
            var usbLimitBefore = info.USBLimit;
            var readoutModeBefore = info.ReadoutMode;
            var scratchDirectory = Path.Combine(Path.GetTempPath(), "speckle-benchmark-frames");
            var runs = new List<BenchmarkRun>();
            var sdkTimings = new List<SdkTiming>();
            var started = DateTime.Now;
            var totalWatch = Stopwatch.StartNew();
            var plan = BuildPlan(info);

            Logger.Info("BENCHMARK starting on " + cameraName + ". " + DescribeEnvironment(info));
            Logger.Info("BENCHMARK plan: " + plan.Count + " configurations, single exposures and video mode side by side, roughly "
                + EstimateMinutes(plan) + " minutes.");
            try {
                Directory.CreateDirectory(scratchDirectory);
                var index = 0;
                foreach (var config in plan) {
                    token.ThrowIfCancellationRequested();
                    index++;
                    if (config.UsbLimit > 0) {
                        cameraMediator.SetUSBLimit(config.UsbLimit);
                        await SettleAsync(token).ConfigureAwait(false);
                    }
                    if (config.ReadoutMode >= 0) {
                        cameraMediator.SetReadoutMode(config.ReadoutMode);
                        await SettleAsync(token).ConfigureAwait(false);
                    }
                    if (config.RaiseTimerResolution) {
                        BeginTimerPeriod(1);
                    }
                    try {
                        progress?.Report(new ApplicationStatus { Status = "Benchmark " + index + " of " + plan.Count + ": " + config.Sweep + " " + config.Label + " " + config.Mode });
                        runs.Add(await MeasureAsync(config, scratchDirectory, token).ConfigureAwait(false));
                    } catch (OperationCanceledException) {
                        throw;
                    } catch (Exception ex) {
                        Logger.Error("BENCHMARK " + config.Describe() + " failed, carrying on", ex);
                        runs.Add(new BenchmarkRun { Config = config, Failure = ex.Message });
                    } finally {
                        if (config.RaiseTimerResolution) {
                            EndTimerPeriod(1);
                        }
                        if (config.UsbLimit > 0) {
                            cameraMediator.SetUSBLimit(usbLimitBefore);
                        }
                        if (config.ReadoutMode >= 0) {
                            cameraMediator.SetReadoutMode(readoutModeBefore);
                        }
                    }
                }
                sdkTimings.AddRange(await TimeSdkCallsAsync(info, progress, token).ConfigureAwait(false));
                var reportPath = WriteReport(cameraName, info, started, totalWatch.Elapsed, runs, sdkTimings);
                LogSummary(cameraName, runs, sdkTimings);
                Notification.ShowSuccess("Benchmark finished in " + Math.Round(totalWatch.Elapsed.TotalMinutes, 1)
                    + " min, report at " + reportPath);
                return reportPath;
            } finally {
                RestoreCamera(usbLimitBefore, readoutModeBefore);
                RemoveScratchFrames(scratchDirectory);
            }
        }

        private List<BenchmarkConfig> BuildPlan(NINA.Equipment.Equipment.MyCamera.CameraInfo info) {
            var plan = new List<BenchmarkConfig>();
            var modes = new[] { CaptureMode.Snapshot, CaptureMode.Video };
            var exposures = new[] { 0.0005, 0.001, 0.002, 0.005, 0.0075, 0.01, 0.015, 0.02, 0.025, 0.03, 0.04, 0.06, 0.09, 0.15, 0.2, 0.5 };
            var roiSizes = new[] { 16, 32, 48, 64, 96, 128, 192, 256, 384, 512, 768, 1024, 1536, 2048, 3072, 4096 };

            foreach (var mode in modes) {
                plan.Add(Baseline(mode, "start of run"));

                foreach (var exposureTime in exposures) {
                    plan.Add(new BenchmarkConfig {
                        Sweep = "exposure", Label = FormatSeconds(exposureTime), Mode = mode,
                        ExposureTime = exposureTime, RoiWidth = 512, RoiHeight = 512
                    });
                }

                foreach (var roiSize in roiSizes) {
                    if (roiSize > info.XSize || roiSize > info.YSize) {
                        continue;
                    }
                    plan.Add(new BenchmarkConfig {
                        Sweep = "roi", Label = roiSize + "x" + roiSize, Mode = mode,
                        ExposureTime = 0.02, RoiWidth = roiSize, RoiHeight = roiSize,
                        Frames = roiSize >= 2048 ? BigFrameMeasuredFrames : 0
                    });
                }

                plan.Add(new BenchmarkConfig {
                    Sweep = "roi shape", Label = "1024 wide by 256 tall", Mode = mode,
                    ExposureTime = 0.02, RoiWidth = 1024, RoiHeight = 256
                });
                plan.Add(new BenchmarkConfig {
                    Sweep = "roi shape", Label = "256 wide by 1024 tall", Mode = mode,
                    ExposureTime = 0.02, RoiWidth = 256, RoiHeight = 1024
                });

                plan.Add(Baseline(mode, "middle of run"));

                foreach (var binning in new short[] { 1, 2, 3, 4 }) {
                    plan.Add(new BenchmarkConfig {
                        Sweep = "binning", Label = "bin " + binning, Mode = mode,
                        ExposureTime = 0.02, RoiWidth = 512, RoiHeight = 512, Binning = binning
                    });
                }

                if (info.CanSetUSBLimit) {
                    foreach (var usbLimit in new[] { 40, 50, 60, 70, 80, 90, 100 }) {
                        if (usbLimit < info.USBLimitMin || usbLimit > info.USBLimitMax) {
                            continue;
                        }
                        plan.Add(new BenchmarkConfig {
                            Sweep = "usb limit", Label = "usb " + usbLimit, Mode = mode,
                            ExposureTime = 0.02, RoiWidth = 512, RoiHeight = 512, UsbLimit = usbLimit
                        });
                    }
                }

                var readoutModeCount = info.ReadoutModes?.Count() ?? 0;
                for (var readoutMode = 0; readoutMode < Math.Min(readoutModeCount, 4); readoutMode++) {
                    plan.Add(new BenchmarkConfig {
                        Sweep = "readout mode", Label = "readout " + readoutMode, Mode = mode,
                        ExposureTime = 0.02, RoiWidth = 512, RoiHeight = 512, ReadoutMode = (short)readoutMode
                    });
                }

                foreach (var gain in GainsToTry(info)) {
                    plan.Add(new BenchmarkConfig {
                        Sweep = "gain", Label = "gain " + gain, Mode = mode,
                        ExposureTime = 0.02, RoiWidth = 512, RoiHeight = 512, Gain = gain
                    });
                }

                foreach (var work in new[] { WorkLevel.CaptureOnly, WorkLevel.CaptureAndDownload, WorkLevel.ThroughImageData, WorkLevel.ThroughMetadata, WorkLevel.ThroughFringe, WorkLevel.Everything }) {
                    plan.Add(new BenchmarkConfig {
                        Sweep = "work level", Label = work.ToString(), Mode = mode,
                        ExposureTime = 0.02, RoiWidth = 512, RoiHeight = 512, Work = work
                    });
                }

                plan.Add(new BenchmarkConfig {
                    Sweep = "gain churn", Label = "gain changed every frame", Mode = mode,
                    ExposureTime = 0.02, RoiWidth = 512, RoiHeight = 512, AlternateGain = true
                });
                plan.Add(new BenchmarkConfig {
                    Sweep = "roi churn", Label = "roi moved every frame", Mode = mode,
                    ExposureTime = 0.02, RoiWidth = 512, RoiHeight = 512, MoveRoiEveryFrame = true
                });
                plan.Add(new BenchmarkConfig {
                    Sweep = "timer resolution", Label = "1 ms windows timer", Mode = mode,
                    ExposureTime = 0.02, RoiWidth = 512, RoiHeight = 512, RaiseTimerResolution = true
                });

                if (info.CanSubSample) {
                    plan.Add(new BenchmarkConfig {
                        Sweep = "full frame", Label = "subframing off", Mode = mode,
                        ExposureTime = 0.02, RoiWidth = 0, RoiHeight = 0, Frames = BigFrameMeasuredFrames
                    });
                }

                plan.Add(Baseline(mode, "end of run"));
            }
            return plan;
        }

        private static IEnumerable<int> GainsToTry(NINA.Equipment.Equipment.MyCamera.CameraInfo info) {
            var wanted = new[] { info.GainMin, info.Gain, (info.GainMin + info.GainMax) / 2, info.GainMax };
            return wanted.Where(gain => gain >= info.GainMin && gain <= info.GainMax).Distinct();
        }

        private static BenchmarkConfig Baseline(CaptureMode mode, string label) {
            return new BenchmarkConfig { Sweep = "baseline", Label = label, Mode = mode, ExposureTime = 0.02, RoiWidth = 512, RoiHeight = 512 };
        }

        private static int EstimateMinutes(List<BenchmarkConfig> plan) {
            var seconds = plan.Sum(config => (config.Frames > 0 ? config.Frames : MeasuredFrames + WarmupFrames) * (config.ExposureTime + 0.16));
            return (int)Math.Ceiling(seconds / 60.0) + 2;
        }

        private async Task<BenchmarkRun> MeasureAsync(BenchmarkConfig config, string scratchDirectory, CancellationToken token) {
            var info = cameraMediator.GetInfo();
            var run = new BenchmarkRun { Config = config, TemperatureAtStart = info.Temperature };
            var frames = config.Frames > 0 ? config.Frames : MeasuredFrames;
            Logger.Info("BENCHMARK " + config.Describe() + ": " + WarmupFrames + " warmup then " + frames + " measured");
            var wall = Stopwatch.StartNew();
            if (config.Mode == CaptureMode.Video) {
                await MeasureVideoAsync(run, config, frames, scratchDirectory, token).ConfigureAwait(false);
            } else {
                await MeasureSnapshotAsync(run, config, frames, scratchDirectory, token).ConfigureAwait(false);
            }
            run.TemperatureAtEnd = cameraMediator.GetInfo()?.Temperature ?? 0;
            Logger.Info("BENCHMARK " + config.Sweep + " / " + config.Label + " / " + config.Mode + " -> " + run.Describe());
            return run;
        }

        private async Task MeasureSnapshotAsync(BenchmarkRun run, BenchmarkConfig config, int frames, string scratchDirectory, CancellationToken token) {
            var info = cameraMediator.GetInfo();
            var capture = BuildCaptureSequence(config, info, 0);
            var imageParameters = new PrepareImageParameters(true, false);
            var previousStart = 0L;
            for (var frame = 1; frame <= WarmupFrames + frames; frame++) {
                token.ThrowIfCancellationRequested();
                var measured = frame > WarmupFrames;
                if (config.MoveRoiEveryFrame) {
                    capture.SubSambleRectangle = BuildRectangle(info, config, frame);
                }
                if (config.AlternateGain) {
                    capture.Gain = frame % 2 == 0 ? info.Gain : Math.Max(info.GainMin, info.Gain - 1);
                }
                var sample = new FrameSample();
                var frameStart = Stopwatch.GetTimestamp();
                sample.ArrivalGapMs = previousStart == 0 ? 0 : Milliseconds(previousStart, frameStart);
                previousStart = frameStart;
                var mark = frameStart;

                await cameraMediator.Capture(capture, token, quietProgress).ConfigureAwait(false);
                sample.CaptureMs = Elapsed(ref mark);

                if (config.Work > WorkLevel.CaptureOnly) {
                    var exposureData = await cameraMediator.Download(token).ConfigureAwait(false);
                    sample.DownloadMs = Elapsed(ref mark);
                    sample.DriverDownloadMs = (cameraMediator.GetInfo()?.LastDownloadTime ?? 0) * 1000;
                    if (config.Work > WorkLevel.CaptureAndDownload) {
                        await RunDownstreamAsync(sample, exposureData, config, capture, imageParameters, frame, scratchDirectory, token).ConfigureAwait(false);
                    }
                }
                sample.TotalMs = Milliseconds(frameStart, Stopwatch.GetTimestamp());
                if (measured) {
                    run.Samples.Add(sample);
                }
            }
        }

        private async Task MeasureVideoAsync(BenchmarkRun run, BenchmarkConfig config, int frames, string scratchDirectory, CancellationToken token) {
            var info = cameraMediator.GetInfo();
            var capture = BuildCaptureSequence(config, info, 0);
            var imageParameters = new PrepareImageParameters(true, false);
            var driver = ZwoDriverAccess.Resolve(info.Name);
            run.DroppedFramesAtStart = driver?.ReadDroppedFrames() ?? -1;
            var streamStart = Stopwatch.GetTimestamp();
            var frame = 0;
            var previous = 0L;
            using (var localCts = CancellationTokenSource.CreateLinkedTokenSource(token)) {
                try {
                    await foreach (var exposureData in cameraMediator.LiveView(capture, localCts.Token).ConfigureAwait(false)) {
                        frame++;
                        var now = Stopwatch.GetTimestamp();
                        if (frame == 1) {
                            run.StreamStartMs = Milliseconds(streamStart, now);
                        }
                        var sample = new FrameSample();
                        sample.ArrivalGapMs = previous == 0 ? 0 : Milliseconds(previous, now);
                        previous = now;
                        if (exposureData != null && config.Work > WorkLevel.CaptureAndDownload) {
                            await RunDownstreamAsync(sample, exposureData, config, capture, imageParameters, frame, scratchDirectory, localCts.Token).ConfigureAwait(false);
                        }
                        sample.TotalMs = sample.ArrivalGapMs;
                        if (frame > WarmupFrames) {
                            run.Samples.Add(sample);
                        }
                        if (frame >= WarmupFrames + frames) {
                            localCts.Cancel();
                        }
                    }
                } catch (OperationCanceledException) when (!token.IsCancellationRequested) {
                }
            }
            run.DroppedFramesAtEnd = driver?.ReadDroppedFrames() ?? -1;
        }

        private async Task RunDownstreamAsync(FrameSample sample, IExposureData exposureData, BenchmarkConfig config, CaptureSequence capture, PrepareImageParameters imageParameters, int frame, string scratchDirectory, CancellationToken token) {
            var mark = Stopwatch.GetTimestamp();
            var imageData = await exposureData.ToImageData(quietProgress, token).ConfigureAwait(false);
            sample.ToImageDataMs = Elapsed(ref mark);
            sample.Width = imageData.Properties.Width;
            sample.Height = imageData.Properties.Height;

            if (config.Work >= WorkLevel.ThroughMetadata) {
                imageData.MetaData.Sequence.Title = "benchmark";
                imageData.MetaData.Image.ExposureStart = DateTime.Now;
                imageData.MetaData.Image.ExposureNumber = frame;
                imageData.MetaData.Image.ExposureTime = config.ExposureTime;
                if (capture.SubSambleRectangle != null) {
                    SpeckleMetadataBuilder.AddRoiHeaders(imageData.MetaData, capture.SubSambleRectangle);
                }
                sample.MetadataMs = Elapsed(ref mark);
            }
            if (config.Work >= WorkLevel.ThroughFringe) {
                fringeAnalysis?.Push(imageData.Data?.FlatArray, imageData.Properties.Width, imageData.Properties.Height, capture.SubSambleRectangle);
                sample.FringePushMs = Elapsed(ref mark);
            }
            if (config.Work >= WorkLevel.Everything) {
                if (frame % 30 == 0) {
                    _ = Task.Run(async () => await imagingMediator.PrepareImage(imageData, imageParameters, token));
                }
                sample.DisplayEnqueueMs = Elapsed(ref mark);
                var saveData = imageData;
                _ = Task.Run(async () => {
                    try {
                        var fileSaveInfo = new FileSaveInfo(profileService) { FilePath = scratchDirectory };
                        var tempPath = await saveData.PrepareSave(fileSaveInfo);
                        saveData.FinalizeSave(tempPath, fileSaveInfo.FilePattern, new List<ImagePattern>());
                    } catch (Exception ex) {
                        Logger.Error("BENCHMARK frame save failed", ex);
                    }
                });
                sample.SaveEnqueueMs = Elapsed(ref mark);
            }
        }

        private CaptureSequence BuildCaptureSequence(BenchmarkConfig config, NINA.Equipment.Equipment.MyCamera.CameraInfo info, int frame) {
            var rectangle = BuildRectangle(info, config, frame);
            var binning = config.Binning > 0 ? config.Binning : (short)1;
            return new CaptureSequence {
                ExposureTime = config.ExposureTime,
                Binning = new BinningMode(binning, binning),
                Gain = config.Gain > 0 ? config.Gain : info.Gain,
                Offset = info.Offset,
                ImageType = CaptureSequence.ImageTypes.LIGHT,
                TotalExposureCount = 1,
                ProgressExposureCount = 1,
                EnableSubSample = rectangle != null,
                SubSambleRectangle = rectangle
            };
        }

        private static ObservableRectangle BuildRectangle(NINA.Equipment.Equipment.MyCamera.CameraInfo info, BenchmarkConfig config, int frame) {
            if (config.RoiWidth <= 0 || config.RoiHeight <= 0) {
                return null;
            }
            var width = Math.Min(config.RoiWidth, info.XSize);
            var height = Math.Min(config.RoiHeight, info.YSize);
            var offset = config.MoveRoiEveryFrame ? (frame % 4) * 64 : 0;
            var left = Math.Max(0, Math.Min(info.XSize - width, (info.XSize - width) / 2 + offset));
            var top = Math.Max(0, Math.Min(info.YSize - height, (info.YSize - height) / 2 + offset));
            return new ObservableRectangle(left, top, width, height);
        }

        private async Task<List<SdkTiming>> TimeSdkCallsAsync(NINA.Equipment.Equipment.MyCamera.CameraInfo info, IProgress<ApplicationStatus> progress, CancellationToken token) {
            var timings = new List<SdkTiming>();
            var driver = ZwoDriverAccess.Resolve(info.Name);
            if (driver == null) {
                Logger.Info("BENCHMARK skipped the driver call timings, this is not a reachable ZWO camera");
                return timings;
            }
            Logger.Info("BENCHMARK timing individual ZWO SDK calls on camera id " + driver.CameraId
                + " (sdk " + driver.SdkVersion + "), " + SdkCallRepeats + " repeats each, warm and between frames");
            foreach (var probe in driver.BuildProbes()) {
                token.ThrowIfCancellationRequested();
                progress?.Report(new ApplicationStatus { Status = "Benchmark driver call " + probe.Name });
                var samples = new List<double>();
                try {
                    probe.Invoke();
                    for (var repeat = 0; repeat < SdkCallRepeats; repeat++) {
                        var mark = Stopwatch.GetTimestamp();
                        probe.Invoke();
                        samples.Add(Milliseconds(mark, Stopwatch.GetTimestamp()));
                    }
                } catch (Exception ex) {
                    Logger.Error("BENCHMARK driver call " + probe.Name + " failed", ex);
                    timings.Add(new SdkTiming { Name = probe.Name, Failure = ex.Message });
                    continue;
                }
                var timing = new SdkTiming { Name = probe.Name, Writes = probe.Writes, Samples = samples };
                timings.Add(timing);
                Logger.Info("BENCHMARK driver call " + probe.Name.PadRight(34) + timing.Describe());
                await Task.Delay(20, token).ConfigureAwait(false);
            }
            return timings;
        }

        private void RestoreCamera(int usbLimitBefore, short readoutModeBefore) {
            try {
                cameraMediator.SetUSBLimit(usbLimitBefore);
                cameraMediator.SetReadoutMode(readoutModeBefore);
                Logger.Info("BENCHMARK put the camera back to usb limit " + usbLimitBefore + " and readout mode " + readoutModeBefore);
            } catch (Exception ex) {
                Logger.Error("BENCHMARK could not put the camera settings back", ex);
            }
        }

        private static void RemoveScratchFrames(string scratchDirectory) {
            try {
                if (Directory.Exists(scratchDirectory)) {
                    Directory.Delete(scratchDirectory, true);
                }
            } catch (Exception ex) {
                Logger.Warning("BENCHMARK left its scratch frames behind in " + scratchDirectory + ": " + ex.Message);
            }
        }

        private static async Task SettleAsync(CancellationToken token) {
            await Task.Delay(400, token).ConfigureAwait(false);
        }

        private string DescribeEnvironment(NINA.Equipment.Equipment.MyCamera.CameraInfo info) {
            var fileSettings = profileService.ActiveProfile.ImageFileSettings;
            return "sensor " + info.XSize + "x" + info.YSize
                + ", pixel " + info.PixelSize
                + ", bit depth " + info.BitDepth
                + ", gain " + info.Gain + " (" + info.GainMin + ".." + info.GainMax + ")"
                + ", offset " + info.Offset
                + ", usb limit " + info.USBLimit + " (" + info.USBLimitMin + ".." + info.USBLimitMax + ", settable " + info.CanSetUSBLimit + ")"
                + ", readout mode " + info.ReadoutMode + " of [" + DescribeReadoutModes(info) + "]"
                + ", can subsample " + info.CanSubSample
                + ", driver says can live view " + info.CanShowLiveView
                + ", binning " + info.BinX + "x" + info.BinY
                + ", cooler " + info.CoolerOn + " at " + info.Temperature
                + ", driver " + info.DriverVersion
                + ", file type " + fileSettings.FileType
                + ", processor count " + Environment.ProcessorCount;
        }

        private static string DescribeReadoutModes(NINA.Equipment.Equipment.MyCamera.CameraInfo info) {
            var modes = info.ReadoutModes?.ToList();
            return modes == null || modes.Count == 0 ? "none" : string.Join(" | ", modes);
        }

        private static double Elapsed(ref long mark) {
            var now = Stopwatch.GetTimestamp();
            var milliseconds = Milliseconds(mark, now);
            mark = now;
            return milliseconds;
        }

        private static double Milliseconds(long from, long to) {
            return (to - from) * 1000.0 / Stopwatch.Frequency;
        }

        private static string FormatSeconds(double seconds) {
            return seconds.ToString("0.####", CultureInfo.InvariantCulture) + " s";
        }

        private static string Format(double value) {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private string WriteReport(string cameraName, NINA.Equipment.Equipment.MyCamera.CameraInfo info, DateTime started, TimeSpan took, List<BenchmarkRun> runs, List<SdkTiming> sdkTimings) {
            var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NINA", "Logs");
            Directory.CreateDirectory(directory);
            var safeName = new string(cameraName.Select(character => char.IsLetterOrDigit(character) ? character : '-').ToArray());
            var path = Path.Combine(directory, "camera-benchmark-" + safeName + "-" + started.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".csv");
            var text = new StringBuilder();
            text.AppendLine("camera," + cameraName);
            text.AppendLine("started," + started.ToString("s", CultureInfo.InvariantCulture));
            text.AppendLine("took minutes," + Format(took.TotalMinutes));
            text.AppendLine("environment," + DescribeEnvironment(info).Replace(",", ";"));
            text.AppendLine();
            text.AppendLine("sweep,config,mode,exposure s,roi,binning,gain,usb limit,readout mode,work,frames,fps,"
                + "period median,period mean,period min,p1,p5,p10,p25,p75,p90,p95,p99,period max,period stddev,"
                + "capture median,download median,driver download median,toimagedata median,metadata median,fringe median,display median,save median,"
                + "frame width,frame height,stream start ms,dropped frames,temp start,temp end,failure");
            foreach (var run in runs) {
                text.AppendLine(string.Join(",", new[] {
                    run.Config.Sweep, run.Config.Label, run.Config.Mode.ToString(),
                    run.Config.ExposureTime.ToString(CultureInfo.InvariantCulture),
                    run.Config.RoiWidth > 0 ? run.Config.RoiWidth + "x" + run.Config.RoiHeight : "full frame",
                    (run.Config.Binning > 0 ? run.Config.Binning : (short)1).ToString(CultureInfo.InvariantCulture),
                    run.Config.Gain.ToString(CultureInfo.InvariantCulture),
                    run.Config.UsbLimit.ToString(CultureInfo.InvariantCulture),
                    run.Config.ReadoutMode.ToString(CultureInfo.InvariantCulture),
                    run.Config.Work.ToString(),
                    run.Samples.Count.ToString(CultureInfo.InvariantCulture),
                    Format(run.Fps),
                    Format(run.Median(sample => sample.TotalMs)),
                    Format(run.Mean(sample => sample.TotalMs)),
                    Format(run.Minimum(sample => sample.TotalMs)),
                    Format(run.Percentile(sample => sample.TotalMs, 0.01)),
                    Format(run.Percentile(sample => sample.TotalMs, 0.05)),
                    Format(run.Percentile(sample => sample.TotalMs, 0.10)),
                    Format(run.Percentile(sample => sample.TotalMs, 0.25)),
                    Format(run.Percentile(sample => sample.TotalMs, 0.75)),
                    Format(run.Percentile(sample => sample.TotalMs, 0.90)),
                    Format(run.Percentile(sample => sample.TotalMs, 0.95)),
                    Format(run.Percentile(sample => sample.TotalMs, 0.99)),
                    Format(run.Maximum(sample => sample.TotalMs)),
                    Format(run.StandardDeviation(sample => sample.TotalMs)),
                    Format(run.Median(sample => sample.CaptureMs)),
                    Format(run.Median(sample => sample.DownloadMs)),
                    Format(run.Median(sample => sample.DriverDownloadMs)),
                    Format(run.Median(sample => sample.ToImageDataMs)),
                    Format(run.Median(sample => sample.MetadataMs)),
                    Format(run.Median(sample => sample.FringePushMs)),
                    Format(run.Median(sample => sample.DisplayEnqueueMs)),
                    Format(run.Median(sample => sample.SaveEnqueueMs)),
                    run.Samples.Count > 0 ? run.Samples[0].Width.ToString(CultureInfo.InvariantCulture) : "0",
                    run.Samples.Count > 0 ? run.Samples[0].Height.ToString(CultureInfo.InvariantCulture) : "0",
                    Format(run.StreamStartMs),
                    (run.DroppedFramesAtEnd - run.DroppedFramesAtStart).ToString(CultureInfo.InvariantCulture),
                    Format(run.TemperatureAtStart), Format(run.TemperatureAtEnd),
                    run.Failure ?? ""
                }));
            }
            text.AppendLine();
            text.AppendLine("driver call,writes to camera,repeats,median ms,mean ms,min ms,p90 ms,p99 ms,max ms,failure");
            foreach (var timing in sdkTimings) {
                text.AppendLine(string.Join(",", new[] {
                    timing.Name, timing.Writes.ToString(), timing.Samples.Count.ToString(CultureInfo.InvariantCulture),
                    Format(timing.Median), Format(timing.Mean), Format(timing.Minimum),
                    Format(timing.Percentile(0.9)), Format(timing.Percentile(0.99)), Format(timing.Maximum),
                    timing.Failure ?? ""
                }));
            }
            text.AppendLine();
            text.AppendLine("histogram of frame periods, 2 ms buckets");
            text.AppendLine("sweep,config,mode,bucket ms,count");
            foreach (var run in runs.Where(candidate => candidate.Samples.Count > 0)) {
                foreach (var bucket in run.Histogram(2)) {
                    text.AppendLine(string.Join(",", new[] {
                        run.Config.Sweep, run.Config.Label, run.Config.Mode.ToString(),
                        bucket.Key.ToString(CultureInfo.InvariantCulture), bucket.Value.ToString(CultureInfo.InvariantCulture)
                    }));
                }
            }
            text.AppendLine();
            text.AppendLine("raw frames");
            text.AppendLine("sweep,config,mode,frame,period,arrival gap,capture,download,driver download,toimagedata,metadata,fringe,display,save");
            foreach (var run in runs) {
                for (var index = 0; index < run.Samples.Count; index++) {
                    var sample = run.Samples[index];
                    text.AppendLine(string.Join(",", new[] {
                        run.Config.Sweep, run.Config.Label, run.Config.Mode.ToString(), (index + 1).ToString(CultureInfo.InvariantCulture),
                        Format(sample.TotalMs), Format(sample.ArrivalGapMs), Format(sample.CaptureMs), Format(sample.DownloadMs),
                        Format(sample.DriverDownloadMs), Format(sample.ToImageDataMs), Format(sample.MetadataMs),
                        Format(sample.FringePushMs), Format(sample.DisplayEnqueueMs), Format(sample.SaveEnqueueMs)
                    }));
                }
            }
            File.WriteAllText(path, text.ToString());
            Logger.Info("BENCHMARK report written to " + path);
            return path;
        }

        private static void LogSummary(string cameraName, List<BenchmarkRun> runs, List<SdkTiming> sdkTimings) {
            Logger.Info("BENCHMARK SUMMARY for " + cameraName);
            foreach (var sweep in runs.GroupBy(run => run.Config.Sweep)) {
                Logger.Info("  " + sweep.Key);
                foreach (var run in sweep) {
                    Logger.Info("    " + (run.Config.Label + " [" + run.Config.Mode + "]").PadRight(36) + run.Describe());
                }
            }
            foreach (var mode in runs.Where(run => run.Samples.Count > 0).GroupBy(run => run.Config.Mode)) {
                var baselines = mode.Where(run => run.Config.Sweep == "baseline").ToList();
                if (baselines.Count > 1) {
                    Logger.Info("  drift check " + mode.Key + ": baseline moved from "
                        + Format(baselines.First().Median(sample => sample.TotalMs)) + " ms to "
                        + Format(baselines.Last().Median(sample => sample.TotalMs)) + " ms across the run");
                }
            }
            var single = runs.FirstOrDefault(run => run.Config.Sweep == "baseline" && run.Config.Mode == CaptureMode.Snapshot && run.Samples.Count > 0);
            var video = runs.FirstOrDefault(run => run.Config.Sweep == "baseline" && run.Config.Mode == CaptureMode.Video && run.Samples.Count > 0);
            if (single != null && video != null) {
                var singlePeriod = single.Median(sample => sample.TotalMs);
                var videoPeriod = video.Median(sample => sample.TotalMs);
                Logger.Info("  HEADLINE at 512x512 and 0.02 s: single exposures " + Format(singlePeriod) + " ms per frame ("
                    + Format(1000 / singlePeriod) + " fps), video mode " + Format(videoPeriod) + " ms per frame ("
                    + Format(1000 / videoPeriod) + " fps), video is " + Format(singlePeriod / Math.Max(videoPeriod, 0.001)) + " times faster");
            }
            if (sdkTimings.Count > 0) {
                Logger.Info("  driver calls, warm");
                foreach (var timing in sdkTimings) {
                    Logger.Info("    " + timing.Name.PadRight(34) + timing.Describe());
                }
            }
        }

        private enum CaptureMode {
            Snapshot = 0,
            Video = 1
        }

        private enum WorkLevel {
            CaptureOnly = 0,
            CaptureAndDownload = 1,
            ThroughImageData = 2,
            ThroughMetadata = 3,
            ThroughFringe = 4,
            Everything = 5
        }

        private class BenchmarkConfig {
            public string Sweep { get; set; }
            public string Label { get; set; }
            public CaptureMode Mode { get; set; }
            public double ExposureTime { get; set; }
            public int RoiWidth { get; set; }
            public int RoiHeight { get; set; }
            public int Frames { get; set; }
            public short Binning { get; set; }
            public int Gain { get; set; }
            public int UsbLimit { get; set; }
            public short ReadoutMode { get; set; } = -1;
            public bool AlternateGain { get; set; }
            public bool MoveRoiEveryFrame { get; set; }
            public bool RaiseTimerResolution { get; set; }
            public WorkLevel Work { get; set; } = WorkLevel.Everything;

            public string Describe() {
                return Sweep + " / " + Label + " / " + Mode
                    + " (exposure " + FormatSeconds(ExposureTime)
                    + ", roi " + (RoiWidth > 0 ? RoiWidth + "x" + RoiHeight : "full frame")
                    + ", bin " + (Binning > 0 ? Binning : (short)1)
                    + ", work " + Work + ")";
            }
        }

        private class FrameSample {
            public double CaptureMs { get; set; }
            public double DownloadMs { get; set; }
            public double DriverDownloadMs { get; set; }
            public double ToImageDataMs { get; set; }
            public double MetadataMs { get; set; }
            public double FringePushMs { get; set; }
            public double DisplayEnqueueMs { get; set; }
            public double SaveEnqueueMs { get; set; }
            public double TotalMs { get; set; }
            public double ArrivalGapMs { get; set; }
            public int Width { get; set; }
            public int Height { get; set; }
        }

        private class BenchmarkRun {
            public BenchmarkConfig Config { get; set; }
            public List<FrameSample> Samples { get; } = new List<FrameSample>();
            public string Failure { get; set; }
            public double StreamStartMs { get; set; }
            public int DroppedFramesAtStart { get; set; }
            public int DroppedFramesAtEnd { get; set; }
            public double TemperatureAtStart { get; set; }
            public double TemperatureAtEnd { get; set; }

            public double Fps {
                get {
                    var period = Median(sample => sample.TotalMs);
                    return period > 0 ? 1000.0 / period : 0;
                }
            }

            public double Median(Func<FrameSample, double> pick) {
                return Percentile(pick, 0.5);
            }

            public double Mean(Func<FrameSample, double> pick) {
                return Samples.Count == 0 ? 0 : Samples.Average(pick);
            }

            public double Minimum(Func<FrameSample, double> pick) {
                return Samples.Count == 0 ? 0 : Samples.Min(pick);
            }

            public double Maximum(Func<FrameSample, double> pick) {
                return Samples.Count == 0 ? 0 : Samples.Max(pick);
            }

            public double Percentile(Func<FrameSample, double> pick, double fraction) {
                if (Samples.Count == 0) {
                    return 0;
                }
                var ordered = Samples.Select(pick).OrderBy(value => value).ToList();
                var index = (int)Math.Round(fraction * (ordered.Count - 1), MidpointRounding.AwayFromZero);
                return ordered[Math.Max(0, Math.Min(ordered.Count - 1, index))];
            }

            public double StandardDeviation(Func<FrameSample, double> pick) {
                if (Samples.Count < 2) {
                    return 0;
                }
                var values = Samples.Select(pick).ToList();
                var mean = values.Average();
                return Math.Sqrt(values.Sum(value => (value - mean) * (value - mean)) / (values.Count - 1));
            }

            public IEnumerable<KeyValuePair<int, int>> Histogram(int bucketMilliseconds) {
                return Samples
                    .GroupBy(sample => (int)(sample.TotalMs / bucketMilliseconds) * bucketMilliseconds)
                    .OrderBy(group => group.Key)
                    .Select(group => new KeyValuePair<int, int>(group.Key, group.Count()));
            }

            public string Describe() {
                if (Failure != null) {
                    return "FAILED: " + Failure;
                }
                if (Samples.Count == 0) {
                    return "no frames measured";
                }
                var dropped = DroppedFramesAtEnd - DroppedFramesAtStart;
                return "period median " + Format(Median(sample => sample.TotalMs))
                    + " min " + Format(Minimum(sample => sample.TotalMs))
                    + " p90 " + Format(Percentile(sample => sample.TotalMs, 0.9))
                    + " max " + Format(Maximum(sample => sample.TotalMs))
                    + " sd " + Format(StandardDeviation(sample => sample.TotalMs))
                    + " ms, " + Format(Fps) + " fps"
                    + (Config.Mode == CaptureMode.Video
                        ? ", stream started in " + Format(StreamStartMs) + " ms" + (dropped > 0 ? ", " + dropped + " frames dropped by the camera" : "")
                        : ", capture " + Format(Median(sample => sample.CaptureMs)) + ", download " + Format(Median(sample => sample.DownloadMs)))
                    + ", toimagedata " + Format(Median(sample => sample.ToImageDataMs))
                    + ", metadata " + Format(Median(sample => sample.MetadataMs))
                    + ", fringe " + Format(Median(sample => sample.FringePushMs))
                    + ", save " + Format(Median(sample => sample.SaveEnqueueMs));
            }
        }

        private class SdkTiming {
            public string Name { get; set; }
            public bool Writes { get; set; }
            public List<double> Samples { get; set; } = new List<double>();
            public string Failure { get; set; }

            public double Median => Percentile(0.5);

            public double Mean => Samples.Count == 0 ? 0 : Samples.Average();

            public double Minimum => Samples.Count == 0 ? 0 : Samples.Min();

            public double Maximum => Samples.Count == 0 ? 0 : Samples.Max();

            public double Percentile(double fraction) {
                if (Samples.Count == 0) {
                    return 0;
                }
                var ordered = Samples.OrderBy(value => value).ToList();
                var index = (int)Math.Round(fraction * (ordered.Count - 1), MidpointRounding.AwayFromZero);
                return ordered[Math.Max(0, Math.Min(ordered.Count - 1, index))];
            }

            public string Describe() {
                return Failure != null
                    ? "FAILED: " + Failure
                    : "median " + Format(Median) + " ms, mean " + Format(Mean) + ", min " + Format(Minimum)
                        + ", p99 " + Format(Percentile(0.99)) + ", max " + Format(Maximum)
                        + (Writes ? " (writes to the camera)" : " (read only)");
            }
        }
    }
}
