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
        private Speckle speckle;

        [ImportingConstructor]
        public LoadReferenceStar(IProfileService profileService, IOptionsVM options) {
            this.profileService = profileService;
            this.options = options;
            speckle = new Speckle();
        }

        private LoadReferenceStar(LoadReferenceStar cloneMe) : this(cloneMe.profileService, cloneMe.options) {
            CopyMetaData(cloneMe);
        }

        public override object Clone() {
            var clone = new LoadReferenceStar(this) {};
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

        private string _SimbadStarName2 { get; set; }
        public string SimbadStarName2 {
            get => _SimbadStarName2;
            set {
                _SimbadStarName2 = value;
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

        public override async Task Execute(IProgress<ApplicationStatus> progress, CancellationToken token) {

            var listContainer = ItemUtility.RetrieveSpeckleListContainer(Parent);
            var speckleTarget = ItemUtility.RetrieveSpeckleTarget(Parent);
            if (RefStar != null) {
                speckleTarget.ReferenceStar = RefStar;

                var templateName = string.IsNullOrWhiteSpace(speckleTarget.Template) ? speckle.DefaultTemplate : speckleTarget.Template;
                var refTemplateName = string.IsNullOrWhiteSpace(speckleTarget.TemplateRef) ? speckle.DefaultRefTemplate : speckleTarget.TemplateRef;
                await listContainer.LoadReferenceTarget(speckleTarget, string.IsNullOrWhiteSpace(refTemplateName) ? templateName : refTemplateName);
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
                if (ReferenceStarList?.Count == 0 && speckleTargetContainer?.SpeckleTarget?.ReferenceStarList?.Count > 0)
                    ReferenceStarList = new AsyncObservableCollection<ReferenceStar>(speckleTargetContainer?.SpeckleTarget?.ReferenceStarList);
                if (RefStar == null) {
                    RefStar = ReferenceStarList?.Count > 0 ? ReferenceStarList?.First() : null;
                    SimbadStarName2 = RefStar?.Name2;
                }
            }

            Issues = i;
            return i.Count == 0;
        }

        public override string ToString() {
            return $"Category: {Category}, Item: {nameof(LoadReferenceStar)}";
        }
    }
}