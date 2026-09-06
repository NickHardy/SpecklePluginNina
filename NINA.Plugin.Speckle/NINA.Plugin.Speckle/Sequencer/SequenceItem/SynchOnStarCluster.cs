#region "copyright"

/*
    Copyright (c) 2026 Nick Hardy and Leon Bewersdorff

    This file is part of the Speckle Interferometry plugin for
    N.I.N.A. - Nighttime Imaging 'N' Astronomy.

    Released under the MIT License. See LICENSE.txt in the repository
    root, or https://opensource.org/licenses/MIT
*/

#endregion "copyright"

using Newtonsoft.Json;
using NINA.Astrometry;
using NINA.Core.Locale;
using NINA.Core.Model;
using NINA.Core.Utility.WindowService;
using NINA.Equipment.Interfaces.Mediator;
using NINA.PlateSolving;
using NINA.Plugin.Speckle.Model;
using NINA.Plugin.Speckle.Services;
using NINA.Sequencer.SequenceItem;
using NINA.Sequencer.Utility;
using NINA.Sequencer.Validations;
using NINA.WPF.Base.ViewModel;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Plugin.Speckle.Sequencer.SequenceItem {

    [ExportMetadata("Name", "Synch on StarCluster")]
    [ExportMetadata("Description", "Slew nearby starclusters until a successful synch. Then slew back to the target.")]
    [ExportMetadata("Icon", "PlatesolveSVG")]
    [ExportMetadata("Category", "Speckle Interferometry")]
    [Export(typeof(ISequenceItem))]
    [JsonObject(MemberSerialization.OptIn)]
    public class SynchOnStarCluster : NINA.Sequencer.SequenceItem.SequenceItem, IValidatable {
        protected ITelescopeMediator telescopeMediator;
        protected IWindowServiceFactory windowServiceFactory;
        protected ISpeckleOptionsProvider optionsProvider;
        protected IClusterCenteringService clusterCenteringService;
        public PlateSolvingStatusVM PlateSolveStatusVM { get; } = new PlateSolvingStatusVM();
        private Speckle speckle;

        [ImportingConstructor]
        public SynchOnStarCluster(ITelescopeMediator telescopeMediator,
                      IWindowServiceFactory windowServiceFactory,
                      ISpeckleOptionsProvider optionsProvider,
                      IClusterCenteringService clusterCenteringService) {
            this.telescopeMediator = telescopeMediator;
            this.windowServiceFactory = windowServiceFactory;
            this.optionsProvider = optionsProvider;
            this.clusterCenteringService = clusterCenteringService;
            Coordinates = new InputCoordinates();
            speckle = optionsProvider.Current;

            SearchRadius = speckle.SearchRadius;
            SlewBackToTarget = true;
        }

        private SynchOnStarCluster(SynchOnStarCluster cloneMe) : this(cloneMe.telescopeMediator,
                                              cloneMe.windowServiceFactory,
                                              cloneMe.optionsProvider,
                                              cloneMe.clusterCenteringService) {
            CopyMetaData(cloneMe);
        }

        public override object Clone() {
            return new SynchOnStarCluster(this) {
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

        public override async Task Execute(IProgress<ApplicationStatus> progress, CancellationToken token) {
            var speckleTarget = Utility.ItemUtility.RetrieveSpeckleTarget(Parent);
            var request = new ClusterCenteringRequest {
                Mode = ClusterCenteringMode.SynchLoop,
                TargetCoordinates = Coordinates?.Coordinates,
                SearchRadius = SearchRadius,
                SlewBackToTarget = SlewBackToTarget,
                ClusterSelected = selection => {
                    StarClusterList = selection.Candidates;
                    StarCluster = selection.Chosen;
                    if (speckleTarget != null) {
                        speckleTarget.StarClusterList = selection.Candidates;
                        speckleTarget.StarCluster = selection.Chosen;
                    }
                }
            };

            var service = windowServiceFactory.Create();
            service.Show(PlateSolveStatusVM, PlateSolveStatusVM.Title, System.Windows.ResizeMode.CanResize, System.Windows.WindowStyle.ToolWindow);
            try {
                var result = await clusterCenteringService.CenterViaStarClusterAsync(request, progress, PlateSolveStatusVM.Progress, token);
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
            var issues = new List<string>();
            if (!telescopeMediator.GetInfo().Connected) {
                issues.Add(Loc.Instance["LblTelescopeNotConnected"]);
            }
            if (Utility.ItemUtility.RetrieveSpeckleContainer(Parent) == null && Utility.ItemUtility.RetrieveSpeckleListContainer(Parent) == null) {
                issues.Add("This instruction only works within a SpeckleTargetContainer.");
            }

            Issues = issues;
            return issues.Count == 0;
        }

        public override string ToString() {
            return $"Category: {Category}, Item: {nameof(CenterOnStarCluster)}, StarCluster {StarCluster?.main_id}";
        }
    }
}
