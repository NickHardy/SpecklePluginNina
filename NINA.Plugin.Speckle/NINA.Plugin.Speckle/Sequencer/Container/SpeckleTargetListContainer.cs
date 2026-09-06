#region "copyright"

/*
    Copyright © 2016 - 2021 Stefan Berg <isbeorn86+NINA@googlemail.com> and the N.I.N.A. contributors

    This file is part of N.I.N.A. - Nighttime Imaging 'N' Astronomy.

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using Newtonsoft.Json;
using NINA.Astrometry;
using NINA.Astrometry.Interfaces;
using NINA.Core.Model;
using NINA.Core.Model.Equipment;
using NINA.Core.Utility;
using NINA.Core.Utility.Notification;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Plugin.Speckle.Model;
using NINA.Plugin.Speckle.Sequencer.Container.ExecutionStrategy;
using NINA.Plugin.Speckle.Sequencer.SequenceItem;
using NINA.Plugin.Speckle.Sequencer.Utility;
using NINA.Plugin.Speckle.Services;
using NINA.Profile.Interfaces;
using NINA.Sequencer;
using NINA.Sequencer.Conditions;
using NINA.Sequencer.Container;
using NINA.Sequencer.Interfaces.Mediator;
using NINA.Sequencer.SequenceItem;
using NINA.Sequencer.SequenceItem.FilterWheel;
using NINA.Sequencer.SequenceItem.Utility;
using NINA.Sequencer.Trigger;
using NINA.WPF.Base.Interfaces.Mediator;
using NINA.WPF.Base.Interfaces.ViewModel;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Threading;

namespace NINA.Plugin.Speckle.Sequencer.Container {

    [ExportMetadata("Name", "Speckle Target List Container")]
    [ExportMetadata("Description", "Lbl_SequenceContainer_DeepSkyObjectContainer_Description")]
    [ExportMetadata("Icon", "SequentialSVG")]
    [ExportMetadata("Category", "Speckle Interferometry")]
    [Export(typeof(ISequenceItem))]
    [Export(typeof(ISequenceContainer))]
    [JsonObject(MemberSerialization.OptIn)]
    public class SpeckleTargetListContainer : SequenceContainer {
        private readonly IProfileService profileService;
        private ISequenceMediator sequenceMediator;
        private IApplicationStatusMediator applicationStatusMediator;
        private ICameraMediator cameraMediator;
        private readonly IFramingAssistantVM framingAssistantVM;
        private readonly IApplicationMediator applicationMediator;
        private ITelescopeMediator telescopeMediator;
        private INighttimeCalculator nighttimeCalculator;
        private readonly ITargetListService targetListService;
        private readonly ITargetSchedulerService targetSchedulerService;
        private readonly IReferenceStarService referenceStarService;
        private readonly ITargetPlanner targetPlanner;
        private readonly ISpeckleOptionsProvider optionsProvider;
        private Speckle speckle;
        private CancellationTokenSource executeCTS;

        [ImportingConstructor]
        public SpeckleTargetListContainer(
                IProfileService profileService,
                ISequenceMediator sequenceMediator,
                INighttimeCalculator nighttimeCalculator,
                IApplicationStatusMediator applicationStatusMediator,
                ICameraMediator cameraMediator,
                IFramingAssistantVM framingAssistantVM,
                IApplicationMediator applicationMediator,
                ITelescopeMediator telescopeMediator,
                ITargetListService targetListService,
                ITargetSchedulerService targetSchedulerService,
                IReferenceStarService referenceStarService,
                ITargetPlanner targetPlanner,
                ISpeckleOptionsProvider optionsProvider) : base(new SequentialListStrategy()) {
            this.profileService = profileService;
            this.sequenceMediator = sequenceMediator;
            this.nighttimeCalculator = nighttimeCalculator;
            this.applicationStatusMediator = applicationStatusMediator;
            this.cameraMediator = cameraMediator;
            this.applicationMediator = applicationMediator;
            this.framingAssistantVM = framingAssistantVM;
            this.telescopeMediator = telescopeMediator;
            this.targetListService = targetListService;
            this.targetSchedulerService = targetSchedulerService;
            this.referenceStarService = referenceStarService;
            this.targetPlanner = targetPlanner;
            this.optionsProvider = optionsProvider;

            speckle = optionsProvider.Current;
            TargetNr = 0;
            User = speckle.User;
            Template = speckle.DefaultTemplate;
            TemplateRef = speckle.DefaultRefTemplate;
            Cycles = speckle.Cycles;
            Exposures = speckle.Exposures;
            ExposureTime = speckle.ExposureTime;
            AutoLoadTargetStar = true;
            AutoLoadReferenceStar = true;
            SpeckleTargets = new AsyncObservableCollection<SpeckleTarget>();

            RetrieveTemplates();
            Task.Run(() => {
                _ = LoadReferenceStarList();
            });

            OpenFileCommand = new GalaSoft.MvvmLight.Command.RelayCommand<bool>((ignored) => { using (executeCTS = new CancellationTokenSource()) { OpenFile(); } });
            DropTargetCommand = new GalaSoft.MvvmLight.Command.RelayCommand<object>(DropTarget);
            LoadTargetCommand = new GalaSoft.MvvmLight.Command.RelayCommand<bool>(async (ignored) => { using (executeCTS = new CancellationTokenSource()) { await LoadTarget(); } });
            DeleteTargetCommand = new GalaSoft.MvvmLight.Command.RelayCommand<object>(DeleteTarget);
            ExportTargetsCommand = new GalaSoft.MvvmLight.Command.RelayCommand<bool>(async (ignored) => { using (executeCTS = new CancellationTokenSource()) { await ExportAllTargetsWithReferenceStars(); } });
            ExportTargetsCsvCommand = new GalaSoft.MvvmLight.Command.RelayCommand<bool>(async (ignored) => { using (executeCTS = new CancellationTokenSource()) { await ExportAllTargetsWithReferenceStarsToCsv(); } });

        }

        private Dispatcher _dispatcher = System.Windows.Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        public ICommand OpenFileCommand { get; private set; }
        public ICommand DropTargetCommand { get; set; }
        public ICommand LoadTargetCommand { get; set; }
        public ICommand DeleteTargetCommand { get; set; }
        public ICommand ExportTargetsCommand { get; set; }
        public ICommand ExportTargetsCsvCommand { get; set; }

        public NighttimeData NighttimeData { get; private set; }
        public ObserveAllCollection<FilterInfo> Filters => profileService.ActiveProfile.FilterWheelSettings.FilterWheelFilters;

        private int _targetnr;

        [JsonProperty]
        public int TargetNr { get => _targetnr; set { _targetnr = value; RaisePropertyChanged(); } }

        private string _User;

        [JsonProperty]
        public string User { get => _User; set { _User = value; RaisePropertyChanged(); } }

        private string _Template;

        [JsonProperty]
        public string Template { get => _Template; set { _Template = value; RaisePropertyChanged(); } }

        private string _TemplateRef;

        [JsonProperty]
        public string TemplateRef { get => _TemplateRef; set { _TemplateRef = value; RaisePropertyChanged(); } }

        private int _Cycles;

        [JsonProperty]
        public int Cycles { get => _Cycles; set { _Cycles = value; RaisePropertyChanged(); } }

        private int _Exposures;

        [JsonProperty]
        public int Exposures { get => _Exposures; set { _Exposures = value; RaisePropertyChanged(); } }

        private double _ExposureTime;

        [JsonProperty]
        public double ExposureTime { get => _ExposureTime; set { _ExposureTime = value; RaisePropertyChanged(); } }

        private bool _sortByRa;

        [JsonProperty]
        public bool SortByRa { get => _sortByRa; set { _sortByRa = value; RaisePropertyChanged(); } }

        private bool _showReferenceStars;

        [JsonProperty]
        public bool ShowReferenceStars { get => _showReferenceStars; set { _showReferenceStars = value; RaisePropertyChanged(); RaisePropertyChanged("SpeckleTargetsView"); } }

        private bool _AutoLoadTargetStar;

        [JsonProperty]
        public bool AutoLoadTargetStar { get => _AutoLoadTargetStar; set { _AutoLoadTargetStar = value; RaisePropertyChanged(); } }

        private bool _AutoLoadReferenceStar;

        [JsonProperty]
        public bool AutoLoadReferenceStar { get => _AutoLoadReferenceStar; set { _AutoLoadReferenceStar = value; RaisePropertyChanged(); } }

        private string _SearchTarget;

        [JsonProperty]
        public string SearchTarget { get => _SearchTarget; set { _SearchTarget = value; RaisePropertyChanged(); RaisePropertyChanged("SpeckleTargetsView"); } }

        private SpeckleTarget _speckleTarget;

        [JsonProperty]
        public SpeckleTarget SpeckleTarget {
            get => _speckleTarget;
            set {
                _speckleTarget = value;
                RaisePropertyChanged();
            }
        }

        private SpeckleTarget _currentSpeckleTarget;

        [JsonProperty]
        public SpeckleTarget CurrentSpeckleTarget {
            get => _currentSpeckleTarget;
            set {
                _currentSpeckleTarget = value;
                RaisePropertyChanged();
            }
        }


        private AsyncObservableCollection<ReferenceStar> _referenceStarList;

        [JsonProperty]
        public AsyncObservableCollection<ReferenceStar> ReferenceStarList {
            get => _referenceStarList;
            set {
                _referenceStarList = value;
                RaisePropertyChanged();
            }
        }

        private AsyncObservableCollection<GaiaReferenceStar> _gaiaReferenceStarList;

        [JsonProperty]
        public AsyncObservableCollection<GaiaReferenceStar> GaiaReferenceStarList {
            get => _gaiaReferenceStarList;
            set {
                _gaiaReferenceStarList = value;
                RaisePropertyChanged();
            }
        }

        private bool _LoadingReferenceStars = false;

        public bool LoadingReferenceStars {
            get { return _LoadingReferenceStars; }
            set {
                _LoadingReferenceStars = value;
                RaisePropertyChanged();
            }
        }

        public async Task LoadReferenceStarList() {
            if (string.IsNullOrWhiteSpace(speckle.ReferenceStarListLocation)) {
                Logger.Debug("No path to reference star list.");
                return;
            }
            if (LoadingReferenceStars)
                return;
            LoadingReferenceStars = true;
            try {
                await referenceStarService.EnsureReferenceListsLoadedAsync(CancellationToken.None);
                if (speckle.UseReferenceStarList && (ReferenceStarList == null || ReferenceStarList.Count == 0) && referenceStarService.ReferenceStars.Count > 0) {
                    ReferenceStarList = new AsyncObservableCollection<ReferenceStar>(referenceStarService.ReferenceStars);
                }
                if (speckle.UseGaiaReferenceStarList && (GaiaReferenceStarList == null || GaiaReferenceStarList.Count == 0) && referenceStarService.GaiaReferenceStars.Count > 0) {
                    GaiaReferenceStarList = new AsyncObservableCollection<GaiaReferenceStar>(referenceStarService.GaiaReferenceStars);
                }
            } finally {
                LoadingReferenceStars = false;
            }
        }

        private AsyncObservableCollection<SpeckleTarget> _speckleTargets;

        [JsonProperty]
        public AsyncObservableCollection<SpeckleTarget> SpeckleTargets {
            get => _speckleTargets;
            set {
                _speckleTargets = value;
                RaisePropertyChanged();
            }
        }

        public AsyncObservableCollection<SpeckleTarget> SpeckleTargetsView {
            get {
                var targets = SpeckleTargets.Where(target => target.ImageTarget && (ShowReferenceStars || target.Type != "R"));
                if (string.IsNullOrWhiteSpace(SearchTarget)) {
                    if (SortByRa) {
                        return new AsyncObservableCollection<SpeckleTarget>(targets.OrderBy(target => target.RA2000).ToList());
                    }
                    return new AsyncObservableCollection<SpeckleTarget>(targets.ToList());
                } else {
                    return new AsyncObservableCollection<SpeckleTarget>(targets.Where(target => target.Name1.IndexOf(SearchTarget, StringComparison.OrdinalIgnoreCase) >= 0 || target.Name2.IndexOf(SearchTarget, StringComparison.OrdinalIgnoreCase) >= 0 || target.GaiaNum.ToString().StartsWith(SearchTarget)).ToList());
                }
            }
        }

        public int SpeckleTargetCount {
            get => SpeckleTargets.Where(target => target.ImageTarget).Count();
        }

        private AsyncObservableCollection<SpeckleTargetContainer> _speckleTemplates = new AsyncObservableCollection<SpeckleTargetContainer>();

        public AsyncObservableCollection<SpeckleTargetContainer> SpeckleTemplates {
            get => _speckleTemplates;
            set {
                _speckleTemplates = value;
                RaisePropertyChanged();
            }
        }

        public async Task LoadTarget() {
            if (SpeckleTarget != null) {
                CurrentSpeckleTarget = SpeckleTarget;
                var templateName = targetPlanner.BuildPlan(SpeckleTarget, false, speckle).TemplateName;
                await LoadSpeckleTarget(templateName);

                using (executeCTS = new CancellationTokenSource()) {
                    await ResolveReferenceStars(SpeckleTarget, new Progress<ApplicationStatus>(status => AppStatus = status), executeCTS.Token).ConfigureAwait(false);
                }

                if (AutoLoadReferenceStar) {
                    var refTemplateName = targetPlanner.BuildPlan(SpeckleTarget, true, speckle).TemplateName;
                    await LoadReferenceTarget(SpeckleTarget, refTemplateName).ConfigureAwait(false);
                }
            }
        }

        public async Task ExportAllTargetsWithReferenceStars() {
            if (SpeckleTargets == null || SpeckleTargets.Count == 0) {
                Notification.ShowWarning("No targets to export. Load a target list first.");
                return;
            }
            LoadingTargets = true;
            try {
                using (executeCTS = new CancellationTokenSource()) {
                    var result = await targetListService.ExportWithReferenceStarsAsync(SpeckleTargets.ToList(), ExportFormat.Json, profileService.ActiveProfile.ImageFileSettings.FilePath, new Progress<ApplicationStatus>(status => AppStatus = status), executeCTS.Token).ConfigureAwait(false);
                    foreach (var refStar in result.SurfacedReferenceRecords) {
                        SurfaceReferenceStarInList(refStar);
                    }
                    Notification.ShowSuccess("Exported targets with reference stars to " + result.FilePath);
                }
            } catch (Exception ex) {
                Logger.Error("Failed to export targets with reference stars.", ex);
                Notification.ShowError("Failed to export targets with reference stars: " + ex.Message);
            } finally {
                LoadingTargets = false;
            }
        }

        public async Task ExportAllTargetsWithReferenceStarsToCsv() {
            if (SpeckleTargets == null || SpeckleTargets.Count == 0) {
                Notification.ShowWarning("No targets to export. Load a target list first.");
                return;
            }
            LoadingTargets = true;
            try {
                using (executeCTS = new CancellationTokenSource()) {
                    var result = await targetListService.ExportWithReferenceStarsAsync(SpeckleTargets.ToList(), ExportFormat.Csv, profileService.ActiveProfile.ImageFileSettings.FilePath, new Progress<ApplicationStatus>(status => AppStatus = status), executeCTS.Token).ConfigureAwait(false);
                    foreach (var refStar in result.SurfacedReferenceRecords) {
                        SurfaceReferenceStarInList(refStar);
                    }
                    Notification.ShowSuccess("Exported targets with reference stars to " + result.FilePath);
                }
            } catch (Exception ex) {
                Logger.Error("Failed to export targets with reference stars to CSV.", ex);
                Notification.ShowError("Failed to export targets with reference stars to CSV: " + ex.Message);
            } finally {
                LoadingTargets = false;
            }
        }

        private void SurfaceReferenceStarInList(SpeckleTarget refStar) {
            bool alreadyInList = SpeckleTargets.Any(target => target.Type == "R" &&
                ((!string.IsNullOrWhiteSpace(refStar.GaiaNum) && refStar.GaiaNum != "0" && target.GaiaNum == refStar.GaiaNum)
                 || (target.RA2000 == refStar.RA2000 && target.Dec2000 == refStar.Dec2000)));
            if (!alreadyInList)
                SpeckleTargets.Add(refStar);
        }

        public void DeleteTarget(Object targetToDelete) {
            Logger.Debug("Object" + targetToDelete.GetType());
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

        public async Task<bool> LoadNewTarget() {
            await RegisterStatusCurrentTarget();

            if (AutoLoadTargetStar) {
                SpeckleTarget = GetNextTarget();
            } else {
                SpeckleTarget = null;
            }

            foreach (ISequenceItem item in Items) {
                _ = _dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() => { this.Remove(item); }));
            }

            if (SpeckleTarget == null) {
                CurrentSpeckleTarget = null;
                await CoreUtil.Wait(TimeSpan.FromMilliseconds(300));
                if (!AutoLoadTargetStar)
                    base.ResetAll();
                RaiseAllPropertiesChanged();
                return false;
            }
            CurrentSpeckleTarget = SpeckleTarget;
            TargetNr++;

            var templateName = targetPlanner.BuildPlan(SpeckleTarget, false, speckle).TemplateName;
            if (AutoLoadTargetStar) {
                await LoadSpeckleTarget(templateName).ConfigureAwait(false);
            }
            using (executeCTS = new CancellationTokenSource()) {
                if (SpeckleTarget.Type == "M" || SpeckleTarget.Type == "C")
                    await ResolveReferenceStars(SpeckleTarget, new Progress<ApplicationStatus>(status => AppStatus = status), executeCTS.Token).ConfigureAwait(false);
            }

            if ((SpeckleTarget.Type == "M" || SpeckleTarget.Type == "C") && AutoLoadReferenceStar) {
                var refTemplateName = targetPlanner.BuildPlan(SpeckleTarget, true, speckle).TemplateName;
                await LoadReferenceTarget(SpeckleTarget, refTemplateName).ConfigureAwait(false);
            }

            RaiseAllPropertiesChanged();
            return true;
        }

        public async Task LoadSpeckleTarget(string templateName) {

            var templates = sequenceMediator.GetDeepSkyObjectContainerTemplates();

            var template = templates.FirstOrDefault(candidate => candidate.Name == templateName);
            if (template == null) {
                Notification.ShowWarning("No template found, loading default. Check the selected template: " + templateName);
                template = templates.FirstOrDefault(candidate => candidate.Name == speckle.DefaultTemplate);
            }
            else {
                var plan = targetPlanner.BuildPlan(SpeckleTarget, false, speckle);
                SpeckleTargetContainer speckleTargetContainer = (SpeckleTargetContainer)template.Clone();
                speckleTargetContainer.Target = new InputTarget(Angle.ByDegree(profileService.ActiveProfile.AstrometrySettings.Latitude), Angle.ByDegree(profileService.ActiveProfile.AstrometrySettings.Longitude), profileService.ActiveProfile.AstrometrySettings.Horizon) {
                    TargetName = plan.TargetName,
                    InputCoordinates = new InputCoordinates() {
                        Coordinates = plan.Coordinates
                    }
                };
                speckleTargetContainer.Title = plan.Title;
                speckleTargetContainer.IsRef = false;
                speckleTargetContainer.SpeckleTarget = SpeckleTarget;
                speckleTargetContainer.Name = plan.ContainerName;
                ApplyPlanToItems(speckleTargetContainer, plan);
                await _dispatcher.BeginInvoke(DispatcherPriority.Send, new Action(() => {
                    lock (Items) {
                        this.InsertIntoSequenceBlocks(100, speckleTargetContainer);
                        Logger.Debug("Adding target container: " + speckleTargetContainer);
                    }
                }));
            }
        }

        public async Task LoadReferenceTarget(SpeckleTarget speckleTarget, string templateName) {
            var templates = sequenceMediator.GetDeepSkyObjectContainerTemplates();
            var template = templates.FirstOrDefault(candidate => candidate.Name == templateName) ?? templates.FirstOrDefault(candidate => candidate.Name == speckle.DefaultRefTemplate);
            if (template == null) {
                Notification.ShowWarning("No ref template found, loading default. Check the selected template: " + templateName);
                template = templates.FirstOrDefault(candidate => candidate.Name == speckle.DefaultRefTemplate);
            }
            else {
                using (executeCTS = new CancellationTokenSource()) {
                    await ResolveReferenceStars(speckleTarget, new Progress<ApplicationStatus>(status => AppStatus = status), executeCTS.Token).ConfigureAwait(false);
                }
                if (speckleTarget.ReferenceStar != null && speckleTarget.ReferenceStar.RA2000 != 0) {
                    var plan = targetPlanner.BuildPlan(speckleTarget, true, speckle);
                    SpeckleTargetContainer speckleTargetContainerRef = (SpeckleTargetContainer)template.Clone();
                    speckleTargetContainerRef.Target = new InputTarget(Angle.ByDegree(profileService.ActiveProfile.AstrometrySettings.Latitude), Angle.ByDegree(profileService.ActiveProfile.AstrometrySettings.Longitude), profileService.ActiveProfile.AstrometrySettings.Horizon) {
                        TargetName = plan.TargetName,
                        InputCoordinates = new InputCoordinates() {
                            Coordinates = plan.Coordinates
                        }
                    };
                    speckleTargetContainerRef.Title = plan.Title;
                    speckleTargetContainerRef.IsRef = true;
                    speckleTargetContainerRef.SpeckleTarget = speckleTarget;
                    speckleTargetContainerRef.Name = plan.ContainerName;
                    ApplyPlanToItems(speckleTargetContainerRef, plan);

                    await _dispatcher.BeginInvoke(DispatcherPriority.Send, new Action(() => {
                        lock (Items) {
                            this.InsertIntoSequenceBlocks(100, speckleTargetContainerRef);
                            Logger.Debug("Adding reference container: " + speckleTargetContainerRef);
                        }
                    }));
                }
            }
        }

        private void ApplyPlanToItems(SpeckleTargetContainer container, TargetPlan plan) {
            container.Items.ToList().ForEach(item => {
                if (item is CalculateExposure calculateExposure && plan.IsReference) {
                    calculateExposure.ExposureTime = plan.Exp;
                }
                if (item is TakeRoiExposures takeRoiExposures && takeRoiExposures.AutoUpdate) {
                    takeRoiExposures.ExposureTime = plan.Exp;
                    takeRoiExposures.TotalExposureCount = plan.NExp;
                }
                if (item is TakeLiveExposures takeLiveExposures && takeLiveExposures.AutoUpdate) {
                    takeLiveExposures.ExposureTime = plan.Exp;
                    takeLiveExposures.TotalExposureCount = plan.NExp;
                }
                if (item is TakeSingleExposure takeSingleExposure && takeSingleExposure.AutoUpdate) {
                    takeSingleExposure.ExposureTime = plan.Exp;
                }
                if (item is TakeSingleRoiExposure takeSingleRoiExposure && takeSingleRoiExposure.AutoUpdate) {
                    takeSingleRoiExposure.ExposureTime = plan.Exp;
                }
                if (item is WaitForTime waitForTime) {
                    waitForTime.Hours = plan.ImageTime.Hour;
                    waitForTime.Minutes = plan.ImageTime.Minute;
                    waitForTime.Seconds = plan.ImageTime.Second;
                }
                if (item is SwitchFilter switchFilter && plan.FilterName != null && plan.FilterName != "-" && plan.FilterName.Length > 0) {
                    switchFilter.Filter = Filters?.FirstOrDefault(filter => filter.Name == plan.FilterName);
                }
            });
        }

        private SpeckleTarget GetNextTarget() {
            var opts = SchedulingOptions.FromOptions(speckle, SortByRa, IgnoreLimits);
            return targetSchedulerService.PickNextTarget(SpeckleTargets.ToList(), opts, CurrentSpeckleTarget?.RA2000, DateTime.Now);
        }

        private async Task RegisterStatusCurrentTarget() {
            if (CurrentSpeckleTarget == null) return;
            SpeckleTarget = SpeckleTargets.Where(target => target.TargetId == CurrentSpeckleTarget.TargetId).FirstOrDefault();
            if (SpeckleTarget != null) {
                if (!SpeckleTarget.RegisterTarget) return;
                targetSchedulerService.MarkCycleComplete(SpeckleTarget, DateTime.Now);
            }

            await targetListService.SaveSnapshotAsync(SpeckleTargets.ToList(), profileService.ActiveProfile.ImageFileSettings.FilePath).ConfigureAwait(false);
        }

        private void DropTarget(object obj) {
        }

        private async Task ResolveReferenceStars(SpeckleTarget target, IProgress<ApplicationStatus> externalProgress, CancellationToken token) {
            if (target.ReferenceStarList != null && target.ReferenceStarList.Any())
                return;

            var result = await referenceStarService.ResolveAsync(target, SpeckleTargets.ToList(), ReferenceStarQueryOptions.FromOptions(speckle), externalProgress, token).ConfigureAwait(false);
            result.ApplyTo(target);
            RaiseAllPropertiesChanged();
        }

        private void OpenFile() {
            OpenFileDialog fileDialog = new OpenFileDialog();
            fileDialog.DefaultExt = ".csv";
            fileDialog.Filter = "Csv documents (.csv)|*.csv";

            if (fileDialog.ShowDialog() == DialogResult.OK) {
                _ = LoadTargets(fileDialog.FileName);
                _ = LoadReferenceStarList();
            }
        }

        private bool _LoadingTargets = false;

        public bool LoadingTargets {
            get { return _LoadingTargets; }
            set {
                _LoadingTargets = value;
                RaiseAllPropertiesChanged();
            }
        }

        private bool _ignoreLimits = false;

        public bool IgnoreLimits {
            get { return _ignoreLimits; }
            set {
                _ignoreLimits = value;
                RaiseAllPropertiesChanged();
            }
        }

        private Task<bool> LoadTargets(string file) {
            LoadingTargets = true;
            return Task.Run(async () => {
                NighttimeData = targetListService.GetNighttimeData(DateTime.Now);
                try {
                    var defaults = new TargetListDefaults {
                        User = User,
                        Template = Template,
                        TemplateRef = TemplateRef,
                        Cycles = Cycles,
                        Exposures = Exposures,
                        ExposureTime = ExposureTime
                    };
                    var targets = await targetListService.LoadTargetsAsync(file, defaults, IgnoreLimits, CancellationToken.None).ConfigureAwait(false);
                    SpeckleTargets = new AsyncObservableCollection<SpeckleTarget>(targets);
                    Logger.Debug("Loaded " + SpeckleTargets.Count + " speckletargets");
                    RaisePropertyChanged("SpeckleTargetCount");
                    RaisePropertyChanged("SpeckleTargetsView");
                } catch (Exception ex) {
                    Notification.ShowError("Failed to load list. " + ex.Message);
                }
                LoadingTargets = false;
                return true;
            });
        }

        public override object Clone() {
            var clone = new SpeckleTargetListContainer(profileService, sequenceMediator, nighttimeCalculator, applicationStatusMediator, cameraMediator, framingAssistantVM, applicationMediator, telescopeMediator, targetListService, targetSchedulerService, referenceStarService, targetPlanner, optionsProvider) {
                Icon = Icon,
                Name = Name,
                Category = Category,
                Description = Description,
                Items = new ObservableCollection<ISequenceItem>(Items.Select(item => item.Clone() as ISequenceItem)),
                Triggers = new ObservableCollection<ISequenceTrigger>(Triggers.Select(item => item.Clone() as ISequenceTrigger)),
                Conditions = new ObservableCollection<ISequenceCondition>(Conditions.Select(item => item.Clone() as ISequenceCondition)),
                SortByRa = SortByRa,
                ShowReferenceStars = ShowReferenceStars,
                AutoLoadTargetStar = AutoLoadTargetStar,
                AutoLoadReferenceStar = AutoLoadReferenceStar,
                Cycles = Cycles,
                Exposures = Exposures,
                ExposureTime = ExposureTime,
            };

            foreach (var item in clone.Items) {
                item.AttachNewParent(clone);
            }

            foreach (var condition in clone.Conditions) {
                condition.AttachNewParent(clone);
            }

            foreach (var trigger in clone.Triggers) {
                trigger.AttachNewParent(clone);
            }

            return clone;
        }

        private ApplicationStatus _status;

        public ApplicationStatus AppStatus {
            get {
                return _status;
            }
            set {
                _status = value;
                if (string.IsNullOrWhiteSpace(_status.Source)) {
                    _status.Source = "Speckle";
                }

                RaisePropertyChanged();

                applicationStatusMediator.StatusUpdate(_status);
            }
        }

        public override string ToString() {
            var baseString = base.ToString();
            return $"{baseString}";
        }
    }
}
