using Dasync.Collections;
using NINA.Core.Model;
using NINA.Core.Utility;
using NINA.Core.Utility.Notification;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Equipment.Model;
using NINA.Equipment.Utility;
using NINA.Image.FileFormat;
using NINA.Image.ImageData;
using NINA.Image.Interfaces;
using NINA.Plugin.Speckle.Sequencer.Utility;
using NINA.Profile.Interfaces;
using NINA.WPF.Base.Interfaces.Mediator;
using NINA.WPF.Base.Interfaces.ViewModel;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Plugin.Speckle.Services {

    [Export(typeof(ISpeckleAcquisitionService))]
    [PartCreationPolicy(CreationPolicy.Shared)]
    public class SpeckleAcquisitionService : ISpeckleAcquisitionService {
        private readonly IProfileService profileService;
        private readonly ICameraMediator cameraMediator;
        private readonly IImagingMediator imagingMediator;
        private readonly IImageSaveMediator imageSaveMediator;
        private readonly IImageHistoryVM imageHistoryVM;
        private readonly IFilterWheelMediator filterWheelMediator;
        private readonly ITelescopeMediator telescopeMediator;
        private readonly IFocuserMediator focuserMediator;
        private readonly IRotatorMediator rotatorMediator;
        private readonly IWeatherDataMediator weatherDataMediator;
        private readonly ISpeckleOptionsProvider optionsProvider;
        private readonly IFringeAnalysisService fringeAnalysis;

        [ImportingConstructor]
        public SpeckleAcquisitionService(IProfileService profileService,
                                         ICameraMediator cameraMediator,
                                         IImagingMediator imagingMediator,
                                         IImageSaveMediator imageSaveMediator,
                                         IImageHistoryVM imageHistoryVM,
                                         IFilterWheelMediator filterWheelMediator,
                                         ITelescopeMediator telescopeMediator,
                                         IFocuserMediator focuserMediator,
                                         IRotatorMediator rotatorMediator,
                                         IWeatherDataMediator weatherDataMediator,
                                         ISpeckleOptionsProvider optionsProvider,
                                         IFringeAnalysisService fringeAnalysis) {
            this.profileService = profileService;
            this.cameraMediator = cameraMediator;
            this.imagingMediator = imagingMediator;
            this.imageSaveMediator = imageSaveMediator;
            this.imageHistoryVM = imageHistoryVM;
            this.filterWheelMediator = filterWheelMediator;
            this.telescopeMediator = telescopeMediator;
            this.focuserMediator = focuserMediator;
            this.rotatorMediator = rotatorMediator;
            this.weatherDataMediator = weatherDataMediator;
            this.optionsProvider = optionsProvider;
            this.fringeAnalysis = fringeAnalysis;
        }

        public async Task<ExposureRunResult> RunRoiSeriesAsync(RoiSeriesRequest request, IProgress<ApplicationStatus> progress, CancellationToken ct) {
            var watch = Stopwatch.StartNew();
            Logger.Info("Video exposures starting: mode " + request.Mode + ", exposure " + request.ExposureTime + " s, frames "
                + request.TotalExposureCount + ", roi " + DescribeRoi(request) + ", title " + (request.Title ?? "none"));
            try {
                var result = request.Mode == RoiSeriesMode.Video
                    ? await RunLiveSeriesAsync(request, progress, ct)
                    : await RunSequencedSeriesAsync(request, progress, ct);
                Logger.Info("Video exposures finished: " + result.FramesCaptured + " frames, " + Math.Round(result.Fps, 2)
                    + " fps, " + watch.ElapsedMilliseconds + " ms");
                return result;
            } catch (OperationCanceledException) {
                Logger.Info("Video exposures cancelled after " + watch.ElapsedMilliseconds + " ms");
                throw;
            } catch (Exception ex) {
                Logger.Error("Video exposures failed after " + watch.ElapsedMilliseconds + " ms", ex);
                throw;
            }
        }

        private static string DescribeRoi(RoiSeriesRequest request) {
            if (!request.EnableSubSample || request.SubSampleRectangle == null) {
                return "full frame";
            }
            var roi = request.SubSampleRectangle;
            return roi.X + "," + roi.Y + " " + roi.Width + "x" + roi.Height;
        }

        private static FringeRunContext BuildFringeContext(RoiSeriesRequest request) {
            var roi = request.SubSampleRectangle;
            return new FringeRunContext {
                Label = request.Title,
                IsReference = request.IsReference,
                RoiX = roi?.X ?? 0,
                RoiY = roi?.Y ?? 0,
                RoiWidth = roi?.Width ?? 0,
                RoiHeight = roi?.Height ?? 0,
                ArcsecPerPixel = request.ArcsecPerPixel,
                ScaleSource = request.ScaleSource,
                Binning = request.Binning?.X ?? 1,
                TotalExposureCount = request.TotalExposureCount
            };
        }

        private static async Task AwaitPauseAsync(PauseGate pause, int frame, CancellationToken token) {
            if (pause == null) {
                return;
            }
            if (!pause.IsPauseRequested) {
                await pause.WaitWhilePausedAsync(token);
                return;
            }
            var watch = Stopwatch.StartNew();
            Logger.Info("Video exposures pause engaged before frame " + frame);
            await pause.WaitWhilePausedAsync(token);
            Logger.Info("Video exposures pause released before frame " + frame + " after " + watch.ElapsedMilliseconds + " ms");
        }

        private int rendersInFlight;
        private int rendersSkipped;

        private static CaptureSequence BuildSeriesCapture(RoiSeriesRequest request, int exposureCount) {
            return new CaptureSequence() {
                ExposureTime = request.ExposureTime,
                Binning = request.Binning,
                Gain = request.Gain,
                Offset = request.Offset,
                ImageType = request.ImageType,
                ProgressExposureCount = exposureCount,
                TotalExposureCount = request.TotalExposureCount,
                EnableSubSample = request.EnableSubSample,
                SubSambleRectangle = request.SubSampleRectangle
            };
        }

        private bool WantsPanelFrame(int exposureCount, RoiSeriesRequest request, Speckle speckle) {
            return exposureCount == 1
                || exposureCount == request.TotalExposureCount
                || (speckle.ShowEveryNthImage > 0 && exposureCount % speckle.ShowEveryNthImage == 0);
        }

        private void MaybePublishFrame(IImageData imageData, PrepareImageParameters imageParams, int exposureCount,
                                       RoiSeriesRequest request, Speckle speckle, CancellationToken token) {
            if (!WantsPanelFrame(exposureCount, request, speckle)) {
                return;
            }
            if (Interlocked.CompareExchange(ref rendersInFlight, 1, 0) != 0) {
                rendersSkipped++;
                Logger.Debug("Panel render skipped for frame " + exposureCount + ", the previous one is still running");
                return;
            }
            var frameToShow = imageData;
            _ = Task.Run(async () => {
                try {
                    var renderedImage = await imagingMediator.PrepareImage(frameToShow, imageParams, token);
                    PreparedFrameRelay.Publish(renderedImage);
                } catch (Exception ex) {
                    Logger.Error("Preparing a frame for the panel failed", ex);
                } finally {
                    Interlocked.Exchange(ref rendersInFlight, 0);
                }
            });
        }

        private void ReportSaveFolder(string savedPath) {
            if (string.IsNullOrWhiteSpace(savedPath)) {
                return;
            }
            try {
                fringeAnalysis?.SetOutputFolder(Path.GetDirectoryName(savedPath));
            } catch (Exception ex) {
                Logger.Error("Could not work out the folder the frames are being saved to", ex);
            }
        }

        private void EnqueueSave(List<Task> saveTasks, IImageData imageData, IList<ImagePattern> customPatterns) {
            saveTasks.Add(Task.Run(async () => {
                try {
                    FileSaveInfo fileSaveInfo = new FileSaveInfo(profileService);
                    string tempPath = await imageData.PrepareSave(fileSaveInfo);
                    var savedPath = imageData.FinalizeSave(tempPath, fileSaveInfo.FilePattern, customPatterns);
                    ReportSaveFolder(savedPath);
                } catch (Exception ex) {
                    ReportSaveFailure(ex);
                    throw;
                }
            }));
        }

        private async Task<ExposureRunResult> RunSequencedSeriesAsync(RoiSeriesRequest request, IProgress<ApplicationStatus> progress, CancellationToken token) {
            var speckle = optionsProvider.Current;
            var exposureCount = 1;
            var capture = BuildSeriesCapture(request, exposureCount);

            var imageParams = new PrepareImageParameters(true, false);
            var customPatterns = SpeckleMetadataBuilder.BuildCustomPatterns(speckle, request.SpeckleRun, request.GenericHeaders);
            var saveTasks = new List<Task>();

            rendersSkipped = 0;
            PreparedFrameRelay.TakeOverDisplay();
            fringeAnalysis?.BeginRun(BuildFringeContext(request));
            try {
                Stopwatch seqDuration = Stopwatch.StartNew();
                while (exposureCount <= request.TotalExposureCount) {
                    await AwaitPauseAsync(request.Pause, exposureCount, token);
                    Stopwatch roiDuration = Stopwatch.StartNew();
                    var exposureStart = DateTime.Now;
                    await cameraMediator.Capture(capture, token, progress);
                    Logger.Debug("Capture: " + roiDuration.ElapsedMilliseconds);
                    progress.Report(new ApplicationStatus() { Status = "Taking Roi image: " + exposureCount });
                    token.ThrowIfCancellationRequested();
                    IExposureData exposureData = await cameraMediator.Download(token);
                    Logger.Debug("Download: " + roiDuration.ElapsedMilliseconds);
                    token.ThrowIfCancellationRequested();

                    var imageData = await exposureData.ToImageData(progress, token);
                    Logger.Debug("ImageData: " + roiDuration.ElapsedMilliseconds);
                    if (request.GenericHeaders != null)
                        imageData.MetaData.GenericHeaders.AddRange(request.GenericHeaders);

                    imageData.MetaData.Sequence.Title = request.Title;
                    imageData.MetaData.Image.ExposureStart = exposureStart;
                    imageData.MetaData.Image.ExposureNumber = exposureCount;
                    imageData.MetaData.Image.ExposureTime = request.ExposureTime;

                    SpeckleMetadataBuilder.AddSpeckleRunHeader(imageData.MetaData, request.SpeckleRun);
                    SpeckleMetadataBuilder.AddJulianDateHeaders(imageData.MetaData, request.ExposureTime);

                    AddEquipmentMetaData(imageData.MetaData);
                    ItemUtility.FromTelescopeInfo(imageData.MetaData, telescopeMediator.GetInfo());

                    SpeckleMetadataBuilder.AddRoiHeaders(imageData.MetaData, capture.SubSambleRectangle);
                    SpeckleMetadataBuilder.ApplyTarget(imageData.MetaData, request.Target);

                    fringeAnalysis?.Push(imageData.Data?.FlatArray, imageData.Properties.Width, imageData.Properties.Height, capture.SubSambleRectangle);

                    MaybePublishFrame(imageData, imageParams, exposureCount, request, speckle, token);

                    var filterWheel = filterWheelMediator.GetInfo();
                    if (filterWheel?.Connected == true && filterWheel.SelectedFilter != null) {
                        imageData.MetaData.FilterWheel.Filter = filterWheel.SelectedFilter.Name;
                    }

                    Logger.Debug("Metadata: " + roiDuration.ElapsedMilliseconds);
                    EnqueueSave(saveTasks, imageData, customPatterns);
                    Logger.Debug("Task save: " + roiDuration.ElapsedMilliseconds);

                    capture.ProgressExposureCount = exposureCount;
                    exposureCount++;
                    request.OnFrame?.Invoke(exposureCount);
                }
                exposureCount--;
                request.OnFrame?.Invoke(exposureCount);
                double fps = exposureCount / (((double)seqDuration.ElapsedMilliseconds) / 1000);
                Logger.Info("Captured " + exposureCount + " times " + request.ExposureTime + "s images in " + seqDuration.ElapsedMilliseconds + " ms. : " + Math.Round(fps, 2) + " fps"
                    + (rendersSkipped > 0 ? ", " + rendersSkipped + " panel renders skipped because one was still running" : ""));
                return new ExposureRunResult {
                    FramesCaptured = exposureCount,
                    Fps = fps,
                    PendingSaves = Task.WhenAll(saveTasks)
                };
            } catch (Exception) {
                cameraMediator.AbortExposure();
                await AwaitPendingSavesAsync(saveTasks);
                throw;
            } finally {
                PreparedFrameRelay.HandDisplayBack();
                fringeAnalysis?.EndRun();
            }
        }

        private async Task<ExposureRunResult> RunLiveSeriesAsync(RoiSeriesRequest request, IProgress<ApplicationStatus> progress, CancellationToken token) {
            var speckle = optionsProvider.Current;
            var exposureCount = 1;
            var capture = BuildSeriesCapture(request, exposureCount);

            var imageParams = new PrepareImageParameters(null, false);
            if (IsLightSequence(request.ImageType)) {
                imageParams = new PrepareImageParameters(true, false);
            }

            var customPatterns = SpeckleMetadataBuilder.BuildCustomPatterns(speckle, request.SpeckleRun, request.GenericHeaders);
            var saveTasks = new List<Task>();
            bool _firstImage = true;
            double fps = 0;

            var localCTS = CancellationTokenSource.CreateLinkedTokenSource(token);

            var liveViewEnumerable = cameraMediator.LiveView(capture, localCTS.Token);
            Stopwatch seqDuration = Stopwatch.StartNew();
            var reachedTheFrameCount = false;
            rendersSkipped = 0;
            PreparedFrameRelay.TakeOverDisplay();
            fringeAnalysis?.BeginRun(BuildFringeContext(request));
            try {
                await liveViewEnumerable.ForEachAsync(async exposureData => {
                    token.ThrowIfCancellationRequested();
                    await AwaitPauseAsync(request.Pause, exposureCount, token);
                    if (exposureData != null) {
                        if (_firstImage) {
                            _firstImage = false;
                            seqDuration = Stopwatch.StartNew();
                            return;
                        }
                        var imageData = await exposureData.ToImageData(progress, localCTS.Token);

                        imageData.MetaData.Sequence.Title = request.Title;
                        SpeckleMetadataBuilder.AddSpeckleRunHeader(imageData.MetaData, request.SpeckleRun);
                        AddLiveMetaData(imageData.MetaData, request, exposureCount);
                        if (request.GenericHeaders != null)
                            imageData.MetaData.GenericHeaders.AddRange(request.GenericHeaders);

                        fringeAnalysis?.Push(imageData.Data?.FlatArray, imageData.Properties.Width, imageData.Properties.Height, capture.SubSambleRectangle);

                        MaybePublishFrame(imageData, imageParams, exposureCount, request, speckle, token);

                        EnqueueSave(saveTasks, imageData, customPatterns);

                        if (exposureCount >= request.TotalExposureCount) {
                            fps = exposureCount / (((double)seqDuration.ElapsedMilliseconds) / 1000);
                            Logger.Info("Captured " + exposureCount + " times " + request.ExposureTime + "s live images in " + seqDuration.ElapsedMilliseconds + " ms. : " + Math.Round(fps, 2) + " fps"
                                + (rendersSkipped > 0 ? ", " + rendersSkipped + " panel renders skipped because one was still running" : ""));
                            reachedTheFrameCount = true;
                            localCTS.Cancel();
                        } else {
                            exposureCount++;
                            request.OnFrame?.Invoke(exposureCount);
                        }
                    }
                });
            } catch (OperationCanceledException) when (reachedTheFrameCount && !token.IsCancellationRequested) {
                Logger.Info("Video mode series finished and the stream was stopped");
            } catch (Exception) {
                await AwaitPendingSavesAsync(saveTasks);
                throw;
            } finally {
                PreparedFrameRelay.HandDisplayBack();
                fringeAnalysis?.EndRun();
            }

            await WaitForCameraReadyAsync(token).ConfigureAwait(false);

            return new ExposureRunResult {
                FramesCaptured = exposureCount,
                Fps = fps,
                PendingSaves = Task.WhenAll(saveTasks)
            };
        }

        private static readonly TimeSpan CameraSettleStep = TimeSpan.FromMilliseconds(100);
        private static readonly TimeSpan CameraSettleLimit = TimeSpan.FromSeconds(10);

        private async Task WaitForCameraReadyAsync(CancellationToken token) {
            await Task.Delay(CameraSettleStep, token).ConfigureAwait(false);
            var waited = Stopwatch.StartNew();
            while (cameraMediator.GetInfo()?.Connected != true) {
                if (waited.Elapsed > CameraSettleLimit) {
                    Logger.Warning("The camera was still reported as disconnected " + waited.Elapsed.TotalSeconds
                        + " seconds after the video stream stopped, so the run continues anyway");
                    return;
                }
                await Task.Delay(CameraSettleStep, token).ConfigureAwait(false);
            }
        }

        private void AddEquipmentMetaData(ImageMetaData metaData) {
            var cameraInfo = cameraMediator.GetInfo();
            metaData.FromProfile(profileService.ActiveProfile);
            metaData.FromCameraInfo(cameraInfo);
            metaData.FromTelescopeInfo(telescopeMediator.GetInfo());
            metaData.FromFilterWheelInfo(filterWheelMediator.GetInfo());
            metaData.FromRotatorInfo(rotatorMediator.GetInfo());
            metaData.FromFocuserInfo(focuserMediator.GetInfo());
            metaData.FromWeatherDataInfo(weatherDataMediator.GetInfo());

            if (metaData.Target.Coordinates == null || double.IsNaN(metaData.Target.Coordinates.RA)) {
                metaData.Target.Coordinates = metaData.Telescope.Coordinates;
            }

            var assembly = Assembly.GetExecutingAssembly();
            metaData.GenericHeaders.Add(new StringMetaDataHeader("PLCREATE", "Speckle-" + assembly.GetName().Version.ToString(), "The plugin used to create this file."));
            metaData.GenericHeaders.Add(new StringMetaDataHeader("CAMERA", cameraInfo.Name));
        }

        private void AddLiveMetaData(ImageMetaData metaData, RoiSeriesRequest request, int exposureCount) {
            SpeckleMetadataBuilder.ApplyTarget(metaData, request.Target);
            metaData.Image.ExposureStart = DateTime.Now - TimeSpan.FromSeconds(request.ExposureTime);
            metaData.Image.ExposureNumber = exposureCount;
            metaData.Image.ExposureTime = request.ExposureTime;
            metaData.Image.ImageType = request.ImageType;

            AddEquipmentMetaData(metaData);

            SpeckleMetadataBuilder.AddRoiHeaders(metaData, request.SubSampleRectangle);
            SpeckleMetadataBuilder.AddJulianDateHeaders(metaData, request.ExposureTime);
        }

        public async Task TakeSingleAsync(SingleExposureRequest request, IProgress<ApplicationStatus> progress, CancellationToken token) {
            var telescopeInfo = telescopeMediator.GetInfo();

            var capture = new CaptureSequence() {
                ExposureTime = request.ExposureTime,
                Binning = request.Binning,
                Gain = request.Gain,
                Offset = request.Offset,
                ImageType = request.ImageType,
                ProgressExposureCount = request.ExposureCount,
                TotalExposureCount = request.ExposureCount + 1,
            };
            if (request.EnableSubSample) {
                capture.EnableSubSample = true;
                capture.SubSambleRectangle = request.SubSampleRectangle;
            }

            var imageParams = new PrepareImageParameters(null, false);
            if (IsLightSequence(request.ImageType)) {
                imageParams = new PrepareImageParameters(true, true);
            }

            var exposureData = await imagingMediator.CaptureImage(capture, token, progress);

            var imageData = await exposureData.ToImageData(progress, token);
            SpeckleMetadataBuilder.AddSpeckleRunHeader(imageData.MetaData, request.SpeckleRun);
            SpeckleMetadataBuilder.AddJulianDateHeaders(imageData.MetaData, request.ExposureTime);
            if (request.GenericHeaders != null)
                imageData.MetaData.GenericHeaders.AddRange(request.GenericHeaders);

            var prepareTask = imagingMediator.PrepareImage(imageData, imageParams, token);

            SpeckleMetadataBuilder.ApplyTarget(imageData.MetaData, request.Target);
            imageData.MetaData.Sequence.Title = request.Title;
            if (request.EnableSubSample) {
                SpeckleMetadataBuilder.AddSubFrameHeaders(imageData.MetaData, capture.SubSambleRectangle);
            } else {
                ItemUtility.FromTelescopeInfo(imageData.MetaData, telescopeInfo);
            }

            await imageSaveMediator.Enqueue(imageData, prepareTask, progress, token);

            if (IsLightSequence(request.ImageType)) {
                imageHistoryVM.Add(imageData.MetaData.Image.Id, await imageData.Statistics, request.ImageType);
            }
        }

        public async Task RunPreviewLoopAsync(PreviewLoopRequest request, IProgress<ApplicationStatus> progress, CancellationToken ct) {
            var capture = new CaptureSequence() {
                ExposureTime = request.GetExposureTime(),
                Gain = request.Gain,
                Offset = request.Offset,
                Binning = request.Binning,
                EnableSubSample = request.EnableSubSample,
                SubSambleRectangle = request.SubSampleRectangle
            };
            var imageParams = new PrepareImageParameters(true, false);
            var exposures = 1;
            Logger.Info("Preview loop starting at " + capture.ExposureTime + " s per frame, "
                + (request.EnableSubSample && request.SubSampleRectangle != null
                    ? "roi " + request.SubSampleRectangle.Width + "x" + request.SubSampleRectangle.Height
                    : "full frame")
                + ". Previews always take single exposures so the screen keeps up; video mode is only for the frames that are saved.");
            try {
                while (!ct.IsCancellationRequested) {
                    capture.ExposureTime = request.GetExposureTime();
                    await imagingMediator.CaptureAndPrepareImage(capture, imageParams, ct, progress);
                    Logger.Debug("Preview loop frame " + exposures);
                    exposures++;
                    request.LoopProgress?.Report(new ApplicationStatus() { Status = $"Images {exposures}" });
                }
            } catch (OperationCanceledException) {
                throw;
            } finally {
                Logger.Info("Preview loop stopped after " + (exposures - 1) + " frames");
            }
        }

        private static bool IsLightSequence(string imageType) {
            return imageType == CaptureSequence.ImageTypes.SNAPSHOT || imageType == CaptureSequence.ImageTypes.LIGHT;
        }

        private int saveFailures;

        private void ReportSaveFailure(Exception ex) {
            var failures = Interlocked.Increment(ref saveFailures);
            if (failures > 1) {
                return;
            }
            var reason = ex is AggregateException aggregate ? aggregate.Flatten().InnerException?.Message ?? ex.Message : ex.Message;
            Logger.Error("Frames are being captured but not saved. Capture continues; nothing is reaching disk.", ex);
            Notification.ShowError("Frames are not being saved: " + reason);
        }

        public void EnsureFramesCanBeSaved() {
            saveFailures = 0;
            var directory = profileService.ActiveProfile.ImageFileSettings.FilePath;
            if (string.IsNullOrWhiteSpace(directory)) {
                throw new Exception("No image folder is set in NINA, so the frames would be lost. Set it under Options, Imaging, Image file path.");
            }
            try {
                Directory.CreateDirectory(directory);
                var probe = Path.Combine(directory, "speckle-write-test.tmp");
                File.WriteAllText(probe, string.Empty);
                File.Delete(probe);
            } catch (Exception ex) {
                throw new Exception("The frames cannot be written to " + directory + " - " + ex.Message, ex);
            }
        }

        private static async Task AwaitPendingSavesAsync(List<Task> saveTasks) {
            if (saveTasks.Count == 0)
                return;
            var pending = Task.WhenAll(saveTasks);
            var completed = await Task.WhenAny(pending, Task.Delay(TimeSpan.FromSeconds(30))).ConfigureAwait(false);
            if (completed != pending) {
                Logger.Warning("Timed out after 30s waiting for pending image saves.");
                return;
            }
            try {
                await pending.ConfigureAwait(false);
            } catch (Exception ex) {
                Logger.Error(ex);
            }
        }
    }
}
