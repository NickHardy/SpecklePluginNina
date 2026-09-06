using NINA.Astrometry;
using NINA.Core.Locale;
using NINA.Core.Model;
using NINA.Core.Model.Equipment;
using NINA.Core.Utility;
using NINA.Core.Utility.Notification;
using NINA.Equipment.Interfaces;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Equipment.Model;
using NINA.PlateSolving;
using NINA.PlateSolving.Interfaces;
using NINA.Plugin.Speckle.Model;
using NINA.Profile.Interfaces;
using NINA.WPF.Base.Interfaces.Mediator;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Plugin.Speckle.Services {

    [Export(typeof(IClusterCenteringService))]
    [PartCreationPolicy(CreationPolicy.Shared)]
    public class ClusterCenteringService : IClusterCenteringService {
        private readonly IProfileService profileService;
        private readonly ITelescopeMediator telescopeMediator;
        private readonly IImagingMediator imagingMediator;
        private readonly IFilterWheelMediator filterWheelMediator;
        private readonly IGuiderMediator guiderMediator;
        private readonly IDomeMediator domeMediator;
        private readonly IDomeFollower domeFollower;
        private readonly IPlateSolverFactory plateSolverFactory;
        private readonly ISimbadUtils simUtils;

        [ImportingConstructor]
        public ClusterCenteringService(IProfileService profileService,
                                       ITelescopeMediator telescopeMediator,
                                       IImagingMediator imagingMediator,
                                       IFilterWheelMediator filterWheelMediator,
                                       IGuiderMediator guiderMediator,
                                       IDomeMediator domeMediator,
                                       IDomeFollower domeFollower,
                                       IPlateSolverFactory plateSolverFactory,
                                       ISimbadUtils simUtils) {
            this.profileService = profileService;
            this.telescopeMediator = telescopeMediator;
            this.imagingMediator = imagingMediator;
            this.filterWheelMediator = filterWheelMediator;
            this.guiderMediator = guiderMediator;
            this.domeMediator = domeMediator;
            this.domeFollower = domeFollower;
            this.plateSolverFactory = plateSolverFactory;
            this.simUtils = simUtils;
        }

        public async Task<PlateSolveResult> CenterViaStarClusterAsync(ClusterCenteringRequest request, IProgress<ApplicationStatus> progress, IProgress<PlateSolveProgress> solveStatus, CancellationToken token) {
            if (request.Mode == ClusterCenteringMode.Center && !request.Platesolve) {
                var starClusterList = await FindStarClusters(request, progress, token);
                var starCluster = starClusterList.FirstOrDefault();
                request.ClusterSelected?.Invoke(new ClusterSelection { Candidates = starClusterList, Chosen = starCluster });

                Logger.Debug("Slewing to StarCluster.");
                await telescopeMediator.SlewToCoordinatesAsync(starCluster.Coordinates(), token);
                return new PlateSolveResult();
            }

            var stoppedGuiding = await guiderMediator.StopGuiding(token);
            PlateSolveResult result = new PlateSolveResult();
            result.Success = false;
            try {
                result = request.Mode == ClusterCenteringMode.Center
                    ? await DoCenter(request, progress, solveStatus, token)
                    : await DoSynchLoop(request, progress, token);
            } finally {
                if (request.SlewBackToTarget) {
                    Logger.Debug("Slewing back to target.");
                    await telescopeMediator.SlewToCoordinatesAsync(request.TargetCoordinates, token);
                }
            }
            if (stoppedGuiding) {
                await guiderMediator.StartGuiding(false, progress, token);
            }
            return result;
        }

        private async Task<PlateSolveResult> DoCenter(ClusterCenteringRequest request, IProgress<ApplicationStatus> progress, IProgress<PlateSolveProgress> solveStatus, CancellationToken token) {
            Logger.Debug("Searching for nearby StarCluster.");
            var starClusterList = await FindStarClusters(request, progress, token);
            var starCluster = starClusterList.FirstOrDefault();
            if (starCluster == null)
                throw new SequenceEntityFailedException("Couldn't find star cluster.");

            request.ClusterSelected?.Invoke(new ClusterSelection { Candidates = starClusterList, Chosen = starCluster });

            Logger.Debug("Slewing to StarCluster.");
            await telescopeMediator.SlewToCoordinatesAsync(starCluster.Coordinates(), token);

            await SynchronizeDome(progress);

            var plateSolver = plateSolverFactory.GetPlateSolver(profileService.ActiveProfile.PlateSolveSettings);
            var blindSolver = plateSolverFactory.GetBlindSolver(profileService.ActiveProfile.PlateSolveSettings);

            var solver = plateSolverFactory.GetCenteringSolver(plateSolver, blindSolver, imagingMediator, telescopeMediator, filterWheelMediator, domeMediator, domeFollower);
            var parameter = BuildCenterSolveParameter(request, profileService.ActiveProfile.PlateSolveSettings.NumberOfAttempts);

            var seq = BuildSolveCaptureSequence();
            return await solver.Center(seq, parameter, solveStatus, progress, token);
        }

        private async Task<PlateSolveResult> DoSynchLoop(ClusterCenteringRequest request, IProgress<ApplicationStatus> progress, CancellationToken token) {
            Logger.Debug("Searching for nearby StarCluster.");
            var starClusterList = await simUtils.FindSimbadStarClusters(progress, token, request.TargetCoordinates, request.SearchRadius);
            var starCluster = starClusterList.FirstOrDefault();
            if (starCluster == null)
                throw new SequenceEntityFailedException("Couldn't find nearby star cluster.");

            request.ClusterSelected?.Invoke(new ClusterSelection { Candidates = starClusterList, Chosen = starCluster });

            foreach (var sc in starClusterList) {
                starCluster = sc;
                request.ClusterSelected?.Invoke(new ClusterSelection { Candidates = starClusterList, Chosen = starCluster });
                Logger.Debug("Slewing to StarCluster.");
                await telescopeMediator.SlewToCoordinatesAsync(starCluster.Coordinates(), token);

                await SynchronizeDome(progress);

                var plateSolver = plateSolverFactory.GetPlateSolver(profileService.ActiveProfile.PlateSolveSettings);

                var parameter = BuildCenterSolveParameter(request, 1);

                var seq = BuildSolveCaptureSequence();

                Logger.Debug("Capturing an image to locate the field.");
                var exposureData = await imagingMediator.CaptureImage(seq, token, progress);
                var imageData = await exposureData.ToImageData(progress, token);

                var prepareTask = imagingMediator.PrepareImage(imageData, new PrepareImageParameters(true, true), token);
                var image = await prepareTask;

                var imageSolver = new ImageSolver(plateSolver, null);

                Logger.Debug("Locating the field");
                var plateSolveResult = await imageSolver.Solve(image.RawImageData, parameter, progress, token);
                if (plateSolveResult.Success)
                    return plateSolveResult;
            }
            return new PlateSolveResult();
        }

        private async Task<List<SimbadStarCluster>> FindStarClusters(ClusterCenteringRequest request, IProgress<ApplicationStatus> progress, CancellationToken token) {
            var coords = request.TargetCoordinates;
            if (request.Zenith) {
                var zenithCoords = new InputTopocentricCoordinates(Angle.ByDegree(profileService.ActiveProfile.AstrometrySettings.Latitude), Angle.ByDegree(profileService.ActiveProfile.AstrometrySettings.Longitude));
                zenithCoords.AltDegrees = 90;
                coords = zenithCoords.Coordinates.Transform(Epoch.J2000);
            }
            return await simUtils.FindSimbadStarClusters(progress, token, coords, request.SearchRadius);
        }

        private async Task SynchronizeDome(IProgress<ApplicationStatus> progress) {
            var domeInfo = domeMediator.GetInfo();
            if (domeInfo.Connected && domeInfo.CanSetAzimuth && !domeFollower.IsFollowing) {
                progress.Report(new ApplicationStatus() { Status = Loc.Instance["LblSynchronizingDome"] });
                Logger.Info($"Centering Solver - Synchronize dome to scope since dome following is not enabled");
                if (!await domeFollower.TriggerTelescopeSync()) {
                    Notification.ShowWarning(Loc.Instance["LblDomeSyncFailureDuringCentering"]);
                    Logger.Warning("Centering Solver - Synchronize dome operation didn't complete successfully. Moving on");
                }
            }
        }

        private CenterSolveParameter BuildCenterSolveParameter(ClusterCenteringRequest request, int attempts) {
            return new CenterSolveParameter() {
                Attempts = attempts,
                Binning = profileService.ActiveProfile.PlateSolveSettings.Binning,
                Coordinates = request.TargetCoordinates ?? telescopeMediator.GetCurrentPosition(),
                DownSampleFactor = profileService.ActiveProfile.PlateSolveSettings.DownSampleFactor,
                FocalLength = profileService.ActiveProfile.TelescopeSettings.FocalLength,
                MaxObjects = profileService.ActiveProfile.PlateSolveSettings.MaxObjects,
                PixelSize = profileService.ActiveProfile.CameraSettings.PixelSize,
                ReattemptDelay = TimeSpan.FromMinutes(profileService.ActiveProfile.PlateSolveSettings.ReattemptDelay),
                Regions = profileService.ActiveProfile.PlateSolveSettings.Regions,
                SearchRadius = profileService.ActiveProfile.PlateSolveSettings.SearchRadius,
                Threshold = profileService.ActiveProfile.PlateSolveSettings.Threshold,
                NoSync = profileService.ActiveProfile.TelescopeSettings.NoSync,
                BlindFailoverEnabled = profileService.ActiveProfile.PlateSolveSettings.BlindFailoverEnabled
            };
        }

        private CaptureSequence BuildSolveCaptureSequence() {
            return PlateSolveCaptureBuilder.Build(profileService, CaptureSequence.ImageTypes.SNAPSHOT);
        }
    }
}
