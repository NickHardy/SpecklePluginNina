using Newtonsoft.Json;
using NINA.Astrometry;
using NINA.Core.Enum;
using NINA.Core.Model;
using NINA.Core.Model.Equipment;
using NINA.Core.Utility;
using NINA.Core.Utility.Notification;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Equipment.Model;
using NINA.Image.ImageAnalysis;
using NINA.Image.ImageData;
using NINA.PlateSolving;
using NINA.PlateSolving.Interfaces;
using NINA.Profile.Interfaces;
using NINA.WPF.Base.Interfaces.Mediator;
using NINA.WPF.Base.Interfaces.ViewModel;
using System;
using System.ComponentModel.Composition;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using static NINA.Astrometry.Coordinates;

namespace NINA.Plugin.Speckle.Services {

    [Export(typeof(IRoiPositioningService))]
    [PartCreationPolicy(CreationPolicy.Shared)]
    public class RoiPositioningService : IRoiPositioningService {
        private readonly IProfileService profileService;
        private readonly ITelescopeMediator telescopeMediator;
        private readonly IImagingMediator imagingMediator;
        private readonly IFilterWheelMediator filterWheelMediator;
        private readonly IPlateSolverFactory plateSolverFactory;
        private readonly IImageSaveMediator imageSaveMediator;
        private readonly IImageHistoryVM imageHistoryVM;

        [ImportingConstructor]
        public RoiPositioningService(IProfileService profileService,
                                     ITelescopeMediator telescopeMediator,
                                     IImagingMediator imagingMediator,
                                     IFilterWheelMediator filterWheelMediator,
                                     IPlateSolverFactory plateSolverFactory,
                                     IImageSaveMediator imageSaveMediator,
                                     IImageHistoryVM imageHistoryVM) {
            this.profileService = profileService;
            this.telescopeMediator = telescopeMediator;
            this.imagingMediator = imagingMediator;
            this.filterWheelMediator = filterWheelMediator;
            this.plateSolverFactory = plateSolverFactory;
            this.imageSaveMediator = imageSaveMediator;
            this.imageHistoryVM = imageHistoryVM;
        }

        public async Task<RoiPositionResult> LocateTargetAsync(RoiPositionRequest request, IProgress<ApplicationStatus> progress, CancellationToken token) {
            bool getBiggestStar = !request.PlatesolveFirst;
            var filter = filterWheelMediator.GetInfo()?.SelectedFilter;
            IPlateSolver plateSolver = null;
            if (!getBiggestStar) {
                plateSolver = plateSolverFactory.GetPlateSolver(profileService.ActiveProfile.PlateSolveSettings);
                if (plateSolver == null) {
                    Logger.Warning("No plate solver is configured; centering the ROI on the brightest star instead");
                    Notification.ShowWarning("No plate solver configured in NINA Options - Plate Solving. Using the brightest star in the frame instead.");
                    getBiggestStar = true;
                }
            }
            var locateBySolving = !getBiggestStar;

            double? roiX = null;
            double? roiY = null;
            double? orientation = null;
            double? arcsecPerPixResult = null;
            bool platesolveSucceeded = false;
            string note = null;
            double captureFrameWidth = 0d;
            double captureFrameHeight = 0d;

            var seq = PlateSolveCaptureBuilder.Build(profileService, CaptureSequence.ImageTypes.LIGHT);

            var solveBinning = Math.Max(1, (int)profileService.ActiveProfile.PlateSolveSettings.Binning);
            var captureBinning = Math.Max(1, request.CaptureBinning);
            var locateToCaptureScale = (double)solveBinning / captureBinning;
            if (locateToCaptureScale != 1d) {
                Logger.Info("The locating frame was taken at binning " + solveBinning + " and the capture runs at binning "
                    + captureBinning + ", so the located position is scaled by " + locateToCaptureScale);
            }

            if (locateBySolving) {
                var parameter = new CaptureSolverParameter() {
                    Attempts = profileService.ActiveProfile.PlateSolveSettings.NumberOfAttempts,
                    Binning = profileService.ActiveProfile.PlateSolveSettings.Binning,
                    Coordinates = telescopeMediator.GetCurrentPosition(),
                    DownSampleFactor = profileService.ActiveProfile.PlateSolveSettings.DownSampleFactor,
                    FocalLength = profileService.ActiveProfile.TelescopeSettings.FocalLength,
                    MaxObjects = profileService.ActiveProfile.PlateSolveSettings.MaxObjects,
                    PixelSize = profileService.ActiveProfile.CameraSettings.PixelSize,
                    ReattemptDelay = TimeSpan.FromMinutes(profileService.ActiveProfile.PlateSolveSettings.ReattemptDelay),
                    Regions = profileService.ActiveProfile.PlateSolveSettings.Regions,
                    SearchRadius = profileService.ActiveProfile.PlateSolveSettings.SearchRadius,
                    BlindFailoverEnabled = profileService.ActiveProfile.PlateSolveSettings.BlindFailoverEnabled
                };
                Logger.Debug("Solver parameters: " + JsonConvert.SerializeObject(parameter));

                Logger.Debug("Capturing an image to locate the target in the frame.");
                var exposureData = await imagingMediator.CaptureImage(seq, token, progress);
                var imageData = await exposureData.ToImageData(progress, token);

                var prepareTask = imagingMediator.PrepareImage(imageData, new PrepareImageParameters(true, true), token);
                var image = await prepareTask;
                var width = image.Image.PixelWidth;
                var height = image.Image.PixelHeight;
                var center = new Point(width / 2, height / 2);
                var arcsecPerPix = AstroUtil.ArcsecPerPixel(profileService.ActiveProfile.CameraSettings.PixelSize * profileService.ActiveProfile.PlateSolveSettings.Binning, profileService.ActiveProfile.TelescopeSettings.FocalLength);

                var imageSolver = new ImageSolver(plateSolver, null);

                Logger.Debug("Locating the target in the frame");
                var plateSolveResult = await imageSolver.Solve(image.RawImageData, parameter, progress, token);
                if (plateSolveResult.Success) {
                    Logger.Debug("Solver result: " + JsonConvert.SerializeObject(plateSolveResult));
                    Logger.Debug("Calculating target position");

                    Point targetPoint = request.Target.InputCoordinates.Coordinates.XYProjection(plateSolveResult.Coordinates, center, arcsecPerPix, arcsecPerPix, plateSolveResult.PositionAngle, ProjectionType.Gnomonic);
                    Logger.Debug("Found target at " + targetPoint.X + "x" + targetPoint.Y);

                    if (targetPoint.X < 0 || targetPoint.X > width || targetPoint.Y < 0 || targetPoint.Y > height) {
                        Notification.ShowError("The target is not inside the frame.");
                        throw new SequenceEntityFailedException("Could not locate the target in the frame; the target lies outside the frame");
                    }

                    if (request.ImageFlippedX) { targetPoint.X = width - targetPoint.X; }
                    if (request.ImageFlippedY) { targetPoint.Y = height - targetPoint.Y; }

                    var captureWidth = image.Image.PixelWidth * locateToCaptureScale;
                    var captureHeight = image.Image.PixelHeight * locateToCaptureScale;
                    captureFrameWidth = captureWidth;
                    captureFrameHeight = captureHeight;

                    roiX = Math.Min(Math.Max(Math.Round(targetPoint.X * locateToCaptureScale - (request.RoiWidth / 2), 0), 0), Math.Max(0, captureWidth - request.RoiWidth));
                    roiY = Math.Min(Math.Max(Math.Round(targetPoint.Y * locateToCaptureScale - (request.RoiHeight / 2), 0), 0), Math.Max(0, captureHeight - request.RoiHeight));
                    Logger.Debug("Setting roi position to " + roiX + "x" + roiY);

                    if (request.SpeckleTarget != null) {
                        orientation = plateSolveResult.PositionAngle;
                        arcsecPerPixResult = arcsecPerPix / locateToCaptureScale;
                    }
                    platesolveSucceeded = true;
                } else {
                    getBiggestStar = true;
                }

                if (!plateSolveResult.Success && request.SpeckleTarget != null) {
                    note = "platesolve-failed";
                    imageData.MetaData.GenericHeaders.Add(new StringMetaDataHeader("NOTE", note, "Note"));
                }

                var target = request.Target;
                if (target != null) {
                    imageData.MetaData.Target.Name = target.TargetName;
                    imageData.MetaData.Target.Coordinates = target.InputCoordinates.Coordinates;
                    imageData.MetaData.Target.PositionAngle = plateSolveResult.PositionAngle;
                }

                imageData.MetaData.Sequence.Title = request.Title;

                await imageSaveMediator.Enqueue(imageData, prepareTask, progress, token);
                imageHistoryVM.Add(imageData.MetaData.Image.Id, await imageData.Statistics, CaptureSequence.ImageTypes.LIGHT);
            }

            if (getBiggestStar) {
                seq.ExposureTime = request.FallbackExposureTime;
                var exposureData2 = await imagingMediator.CaptureImage(seq, token, progress);
                var imageData2 = await exposureData2.ToImageData(progress, token);

                var prepareTask2 = imagingMediator.PrepareImage(imageData2, new PrepareImageParameters(true, false), token);
                var image2 = await prepareTask2;

                var starDetection = new Sequencer.Utility.StarDetection();
                var starDetectionParams = new StarDetectionParams() {
                    Sensitivity = StarSensitivityEnum.Normal,
                    NoiseReduction = NoiseReductionEnum.None
                };
                var biggestStar = await starDetection.GetBiggestStar(image2, image2.Image.Format, starDetectionParams, progress, token);

                if (biggestStar != null) {
                    var fallbackCaptureWidth = image2.Image.PixelWidth * locateToCaptureScale;
                    var fallbackCaptureHeight = image2.Image.PixelHeight * locateToCaptureScale;
                    captureFrameWidth = fallbackCaptureWidth;
                    captureFrameHeight = fallbackCaptureHeight;
                    roiX = Math.Min(Math.Max(Math.Round(biggestStar.Position.X * locateToCaptureScale - (request.RoiWidth / 2), 0), 0), Math.Max(0, fallbackCaptureWidth - request.RoiWidth));
                    roiY = Math.Min(Math.Max(Math.Round(biggestStar.Position.Y * locateToCaptureScale - (request.RoiHeight / 2), 0), 0), Math.Max(0, fallbackCaptureHeight - request.RoiHeight));
                    Logger.Debug("Setting roi position to biggest star position " + roiX + "x" + roiY);
                } else {
                    Logger.Warning("Could not find a star in the frame");
                }
            }

            if (filter != null) {
                _ = await filterWheelMediator.ChangeFilter(filter, token, progress);
            }

            return new RoiPositionResult {
                RoiX = roiX,
                RoiY = roiY,
                Orientation = orientation,
                ArcsecPerPix = arcsecPerPixResult,
                PlatesolveSucceeded = platesolveSucceeded,
                Note = note,
                FrameWidth = captureFrameWidth,
                FrameHeight = captureFrameHeight
            };
        }
    }
}
