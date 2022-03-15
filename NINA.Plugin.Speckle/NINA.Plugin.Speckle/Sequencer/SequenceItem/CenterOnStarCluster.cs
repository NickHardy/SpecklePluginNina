#region "copyright"

/*
    Copyright © 2016 - 2022 Stefan Berg <isbeorn86+NINA@googlemail.com> and the N.I.N.A. contributors

    This file is part of N.I.N.A. - Nighttime Imaging 'N' Astronomy.

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using Newtonsoft.Json;
using NINA.Core.Model;
using NINA.PlateSolving;
using NINA.Profile.Interfaces;
using NINA.Sequencer.Container;
using NINA.Sequencer.Utility;
using NINA.Sequencer.Validations;
using NINA.Astrometry;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Core.Utility.WindowService;
using NINA.ViewModel;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel.Composition;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NINA.Equipment.Model;
using NINA.Core.Model.Equipment;
using NINA.Core.Locale;
using NINA.WPF.Base.ViewModel;
using NINA.PlateSolving.Interfaces;
using NINA.Core.Utility.Notification;
using NINA.Core.Utility;
using NINA.Equipment.Interfaces;
using NINA.Sequencer.SequenceItem;
using NINA.Plugin.Speckle.Model;

namespace NINA.Plugin.Speckle.Sequencer.SequenceItem {

    [ExportMetadata("Name", "Center on StarCluster")]
    [ExportMetadata("Description", "Slew and center a nearby starcluster. Then slew back to the target.")]
    [ExportMetadata("Icon", "PlatesolveSVG")]
    [ExportMetadata("Category", "Speckle Interferometry")]
    [Export(typeof(ISequenceItem))]
    [JsonObject(MemberSerialization.OptIn)]
    public class CenterOnStarCluster : NINA.Sequencer.SequenceItem.SequenceItem, IValidatable {
        protected IProfileService profileService;
        protected ITelescopeMediator telescopeMediator;
        protected IImagingMediator imagingMediator;
        protected IFilterWheelMediator filterWheelMediator;
        protected IGuiderMediator guiderMediator;
        protected IDomeMediator domeMediator;
        protected IDomeFollower domeFollower;
        protected IPlateSolverFactory plateSolverFactory;
        protected IWindowServiceFactory windowServiceFactory;
        public PlateSolvingStatusVM PlateSolveStatusVM { get; } = new PlateSolvingStatusVM();
        private Speckle speckle;

        [ImportingConstructor]
        public CenterOnStarCluster(IProfileService profileService,
                      ITelescopeMediator telescopeMediator,
                      IImagingMediator imagingMediator,
                      IFilterWheelMediator filterWheelMediator,
                      IGuiderMediator guiderMediator,
                      IDomeMediator domeMediator,
                      IDomeFollower domeFollower,
                      IPlateSolverFactory plateSolverFactory,
                      IWindowServiceFactory windowServiceFactory) {
            this.profileService = profileService;
            this.telescopeMediator = telescopeMediator;
            this.imagingMediator = imagingMediator;
            this.filterWheelMediator = filterWheelMediator;
            this.guiderMediator = guiderMediator;
            this.domeMediator = domeMediator;
            this.domeFollower = domeFollower;
            this.plateSolverFactory = plateSolverFactory;
            this.windowServiceFactory = windowServiceFactory;
            Coordinates = new InputCoordinates();
            speckle = new Speckle();

            SearchRadius = speckle.SearchRadius;
            SlewBackToTarget = true;
        }

        private CenterOnStarCluster(CenterOnStarCluster cloneMe) : this(cloneMe.profileService,
                                              cloneMe.telescopeMediator,
                                              cloneMe.imagingMediator,
                                              cloneMe.filterWheelMediator,
                                              cloneMe.guiderMediator,
                                              cloneMe.domeMediator,
                                              cloneMe.domeFollower,
                                              cloneMe.plateSolverFactory,
                                              cloneMe.windowServiceFactory) {
            CopyMetaData(cloneMe);
        }

        public override object Clone() {
            return new CenterOnStarCluster(this) {
                Coordinates = Coordinates.Clone(),
                SearchRadius = SearchRadius,
                SlewBackToTarget = SlewBackToTarget
            };
        }

        private bool inherited;

        [JsonProperty]
        public bool Inherited {
            get => inherited;
            set {
                inherited = value;
                RaisePropertyChanged();
            }
        }

        [JsonProperty]
        public InputCoordinates Coordinates { get; set; }

        public List<SimbadStarCluster> StarClusterList { get; set; }
        public SimbadStarCluster StarCluster { get; set; } = new SimbadStarCluster();

        private double _SearchRadius;
        [JsonProperty]
        public double SearchRadius { get => _SearchRadius; set { _SearchRadius = value; RaisePropertyChanged(); } }

        private bool _SlewBackToTarget;
        [JsonProperty]
        public bool SlewBackToTarget { get => _SlewBackToTarget; set { _SlewBackToTarget = value; RaisePropertyChanged(); } }

        private IList<string> issues = new List<string>();

        public IList<string> Issues {
            get => issues;
            set {
                issues = value;
                RaisePropertyChanged();
            }
        }

        protected virtual async Task<PlateSolveResult> DoCenter(IProgress<ApplicationStatus> progress, CancellationToken token) {
            Logger.Debug("Searching for nearby StarCluster.");
            Utility.SimbadUtils simUtils = new Utility.SimbadUtils();
            StarClusterList = await simUtils.FindSimbadStarClusters(progress, token, Coordinates.Coordinates, SearchRadius);
            StarCluster = StarClusterList.FirstOrDefault();
            if (StarCluster == null)
                throw new SequenceEntityFailedException("Couldn't find nearby star cluster.");
            
            var speckleTarget = Utility.ItemUtility.RetrieveSpeckleTarget(Parent);
            if (speckleTarget != null) {
                speckleTarget.StarClusterList = StarClusterList;
                speckleTarget.StarCluster = StarCluster;
            }

            Logger.Debug("Slewing to StarCluster.");
            await telescopeMediator.SlewToCoordinatesAsync(StarCluster.Coordinates(), token);

            var domeInfo = domeMediator.GetInfo();
            if (domeInfo.Connected && domeInfo.CanSetAzimuth && !domeFollower.IsFollowing) {
                progress.Report(new ApplicationStatus() { Status = Loc.Instance["LblSynchronizingDome"] });
                Logger.Info($"Centering Solver - Synchronize dome to scope since dome following is not enabled");
                if (!await domeFollower.TriggerTelescopeSync()) {
                    Notification.ShowWarning(Loc.Instance["LblDomeSyncFailureDuringCentering"]);
                    Logger.Warning("Centering Solver - Synchronize dome operation didn't complete successfully. Moving on");
                }
            }

            var plateSolver = plateSolverFactory.GetPlateSolver(profileService.ActiveProfile.PlateSolveSettings);
            var blindSolver = plateSolverFactory.GetBlindSolver(profileService.ActiveProfile.PlateSolveSettings);

            var solver = plateSolverFactory.GetCenteringSolver(plateSolver, blindSolver, imagingMediator, telescopeMediator, filterWheelMediator, domeMediator, domeFollower);
            var parameter = new CenterSolveParameter() {
                Attempts = profileService.ActiveProfile.PlateSolveSettings.NumberOfAttempts,
                Binning = profileService.ActiveProfile.PlateSolveSettings.Binning,
                Coordinates = Coordinates?.Coordinates ?? telescopeMediator.GetCurrentPosition(),
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

            var seq = new CaptureSequence(
                profileService.ActiveProfile.PlateSolveSettings.ExposureTime,
                CaptureSequence.ImageTypes.SNAPSHOT,
                profileService.ActiveProfile.PlateSolveSettings.Filter,
                new BinningMode(profileService.ActiveProfile.PlateSolveSettings.Binning, profileService.ActiveProfile.PlateSolveSettings.Binning),
                1
            );
            return await solver.Center(seq, parameter, PlateSolveStatusVM.Progress, progress, token);
        }

        public override async Task Execute(IProgress<ApplicationStatus> progress, CancellationToken token) {
            var service = windowServiceFactory.Create();
            service.Show(PlateSolveStatusVM, PlateSolveStatusVM.Title, System.Windows.ResizeMode.CanResize, System.Windows.WindowStyle.ToolWindow);
            try {
                var stoppedGuiding = await guiderMediator.StopGuiding(token);
                PlateSolveResult result = new PlateSolveResult();
                result.Success = false;
                try {
                    result = await DoCenter(progress, token);
                } finally {
                    if (SlewBackToTarget) {
                        // Hopefully centered on Star cluster. Now return to the target.
                        Logger.Debug("Slewing back to target.");
                        await telescopeMediator.SlewToCoordinatesAsync(Coordinates.Coordinates, token);
                    }
                }
                if (stoppedGuiding) {
                    await guiderMediator.StartGuiding(false, progress, token);
                }
                if (result.Success == false) {
                    throw new SequenceEntityFailedException(Loc.Instance["LblPlatesolveFailed"]);
                }
            } finally {
                service.DelayedClose(TimeSpan.FromSeconds(1));
            }
        }

        public override void AfterParentChanged() {
            var contextCoordinates = ItemUtility.RetrieveContextCoordinates(this.Parent);
            if (contextCoordinates != null) {
                Coordinates.Coordinates = contextCoordinates.Coordinates;
                Inherited = true;
            } else {
                Inherited = false;
            }
            Validate();
        }

        public virtual bool Validate() {
            var i = new List<string>();
            if (!telescopeMediator.GetInfo().Connected) {
                i.Add(Loc.Instance["LblTelescopeNotConnected"]);
            }
            if (Utility.ItemUtility.RetrieveSpeckleContainer(Parent) == null) {
                i.Add("This instruction only works within a SpeckleTargetContainer.");
            }

            Issues = i;
            return i.Count == 0;
        }

        public override string ToString() {
            return $"Category: {Category}, Item: {nameof(CenterOnStarCluster)}, StarCluster {StarCluster?.main_id}";
        }
    }
}