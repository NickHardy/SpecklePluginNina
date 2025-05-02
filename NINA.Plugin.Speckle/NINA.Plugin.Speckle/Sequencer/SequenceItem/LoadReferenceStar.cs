using Newtonsoft.Json;
using NINA.Core.Model;
using NINA.Profile.Interfaces;
using NINA.Sequencer.Validations;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Threading;
using System.Threading.Tasks;
using NINA.WPF.Base.Interfaces.ViewModel;
using NINA.Sequencer.SequenceItem;
using NINA.Plugin.Speckle.Sequencer.Utility;
using NINA.Plugin.Speckle.Model;
using System.Linq;
using NINA.Core.Utility;
using NINA.Plugin.Speckle.Sequencer.Container;
using NINA.Sequencer.Mediator;
using NINA.Sequencer.Interfaces.Mediator;
using System.Diagnostics.Eventing.Reader;

namespace NINA.Plugin.Speckle.Sequencer.SequenceItem {

    [ExportMetadata("Name", "Load reference star")]
    [ExportMetadata("Description", "This instruction will load the reference star for the current speckle target.")]
    [ExportMetadata("Icon", "StarSVG")]
    [ExportMetadata("Category", "Speckle Interferometry")]
    [Export(typeof(ISequenceItem))]
    [JsonObject(MemberSerialization.OptIn)]
    public class LoadReferenceStar : NINA.Sequencer.SequenceItem.SequenceItem, IValidatable {
        private IProfileService profileService;
        private IOptionsVM options;
        private ISequenceMediator sequenceMediator;
        private Speckle speckle;

        [ImportingConstructor]
        public LoadReferenceStar(IProfileService profileService, IOptionsVM options, ISequenceMediator sequenceMediator) {
            this.profileService = profileService;
            this.options = options;
            this.sequenceMediator = sequenceMediator;
            speckle = new Speckle(profileService);

            RetrieveTemplates();
        }

        private LoadReferenceStar(LoadReferenceStar cloneMe) : this(cloneMe.profileService, cloneMe.options, cloneMe.sequenceMediator) {
            CopyMetaData(cloneMe);
        }

        public override object Clone() {
            var clone = new LoadReferenceStar(this) {
                TemplateRef = this.TemplateRef
            };
            return clone;
        }

        private IList<string> issues = new List<string>();

        public IList<string> Issues {
            get => issues;
            set {
                issues = value;
                RaisePropertyChanged();
            }
        }

        private ReferenceStar _ReferenceStar { get; set; }
        public ReferenceStar RefStar {
            get => _ReferenceStar;
            set {
                _ReferenceStar = value;
                RaisePropertyChanged();
            }
        }

        private string _ReferenceStarName { get; set; }
        public string ReferenceStarName {
            get => _ReferenceStarName;
            set {
                _ReferenceStarName = value;
                RaisePropertyChanged();
            }
        }

        private AsyncObservableCollection<ReferenceStar> _referenceStarList { get; set; } = new AsyncObservableCollection<ReferenceStar>();
        public AsyncObservableCollection<ReferenceStar> ReferenceStarList {
            get => _referenceStarList;
            set {
                _referenceStarList = value;
                RaisePropertyChanged();
            }
        }

        private AsyncObservableCollection<SpeckleTargetContainer> _speckleTemplates = new AsyncObservableCollection<SpeckleTargetContainer>();

        public AsyncObservableCollection<SpeckleTargetContainer> SpeckleTemplates {
            get => _speckleTemplates;
            set {
                _speckleTemplates = value;
                RaisePropertyChanged();
            }
        }

        public void RetrieveTemplates() {
            if (sequenceMediator.Initialized) {
                SpeckleTemplates.Clear();
                var templates = sequenceMediator.GetDeepSkyObjectContainerTemplates();
                foreach (var template in templates) {
                    var speckleTemplate = template as SpeckleTargetContainer;
                    if (speckleTemplate != null)
                        SpeckleTemplates.Add(speckleTemplate);
                }
            }
        }

        private string _TemplateRef;

        [JsonProperty]
        public string TemplateRef { get => _TemplateRef; set { _TemplateRef = value; RaisePropertyChanged(); } }

        public override async Task Execute(IProgress<ApplicationStatus> progress, CancellationToken token) {

            var listContainer = ItemUtility.RetrieveSpeckleListContainer(Parent);
            var speckleTarget = ItemUtility.RetrieveSpeckleTarget(Parent);
            if (RefStar != null) {
                speckleTarget.ReferenceStar = RefStar;
                await listContainer.LoadReferenceTarget(speckleTarget, TemplateRef);
            }
        }

        public override void AfterParentChanged() {
            Validate();
        }

        public bool Validate() {
            var i = new List<string>();

            if (ItemUtility.RetrieveSpeckleContainer(Parent) == null && ItemUtility.RetrieveSpeckleListContainer(Parent) == null) {
                i.Add("This instruction only works within a SpeckleTargetContainer.");
            } else {
                var speckleTargetContainer = ItemUtility.RetrieveSpeckleContainer(Parent);
                var speckleTarget = ItemUtility.RetrieveSpeckleTarget(Parent);
                if (ReferenceStarList?.Count == 0 && speckleTarget?.ReferenceStarList?.Count > 0)
                    ReferenceStarList = new AsyncObservableCollection<ReferenceStar>(speckleTarget?.ReferenceStarList);
                if (RefStar == null) {
                    RefStar = ReferenceStarList?.Count > 0 ? ReferenceStarList?.First() : null;
                    ReferenceStarName = RefStar?.Name;
                }
                if (!string.IsNullOrWhiteSpace(speckleTarget?.TemplateRef))
                    TemplateRef = speckleTarget.TemplateRef;
                else if (!string.IsNullOrWhiteSpace(RefStar?.Template) && RefStar?.Template != "_")
                    TemplateRef = RefStar.Template;
            }

            Issues = i;
            return i.Count == 0;
        }

        public override string ToString() {
            return $"Category: {Category}, Item: {nameof(LoadReferenceStar)}";
        }
    }
}