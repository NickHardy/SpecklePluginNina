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
using NINA.Core.Enum;
using NINA.Profile.Interfaces;
using NINA.Sequencer.Conditions;
using NINA.Sequencer.SequenceItem;
using NINA.Sequencer.Trigger;
using NINA.Core.Utility;
using NINA.Astrometry;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel.Composition;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using NINA.WPF.Base.Interfaces.Mediator;
using NINA.Equipment.Exceptions;
using NINA.Core.Utility.Notification;
using NINA.Core.Locale;
using NINA.Astrometry.Interfaces;
using NINA.Equipment.Interfaces;
using NINA.WPF.Base.Interfaces.ViewModel;
using System.Windows;
using NINA.Sequencer.Container;
using NINA.Sequencer;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Equipment.Equipment.MyCamera;
using NINA.Plugin.Speckle.Model;
using System.Collections.Generic;
using NINA.Sequencer.Interfaces.Mediator;
using NINA.Plugin.Speckle.Sequencer.SequenceItem;
using NINA.Core.Model;
using System.Threading;
using NINA.Plugin.Speckle.Sequencer.Utility;
using System.Windows.Threading;
using System.Windows.Forms;
using System.IO;
using CsvHelper;
using System.Globalization;
using CsvHelper.Configuration;
using NINA.Plugin.Speckle.Sequencer.Container.ExecutionStrategy;
using NINA.Sequencer.SequenceItem.Utility;
using NINA.Sequencer.SequenceItem.FilterWheel;
using NINA.Core.Model.Equipment;
using NINA.Image.ImageData;
using System.Text.RegularExpressions;
using NINA.Sequencer.SequenceItem.Rotator;

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
                ITelescopeMediator telescopeMediator) : base(new SequentialListStrategy()) {
            this.profileService = profileService;
            this.sequenceMediator = sequenceMediator;
            this.nighttimeCalculator = nighttimeCalculator;
            this.applicationStatusMediator = applicationStatusMediator;
            this.cameraMediator = cameraMediator;
            this.applicationMediator = applicationMediator;
            this.framingAssistantVM = framingAssistantVM;
            this.telescopeMediator = telescopeMediator;

            //Task.Run(() => NighttimeData = nighttimeCalculator.Calculate(DateTime.Now.AddHours(4)));
            speckle = new Speckle();
            TargetNr = 0;
            User = speckle.User;
            Template = speckle.DefaultTemplate;
            Cycles = speckle.Cycles;
            Exposures = speckle.Exposures;
            ExposureTime = speckle.ExposureTime;
            SpeckleTargets = new AsyncObservableCollection<SpeckleTarget>();
            GdsTargets = new AsyncObservableCollection<GdsTarget>();
            SimUtils = new SimbadUtils();

            RetrieveTemplates();
            OpenFileCommand = new GalaSoft.MvvmLight.Command.RelayCommand<bool>((o) => { using (executeCTS = new CancellationTokenSource()) { OpenFile(); } });
            DropTargetCommand = new GalaSoft.MvvmLight.Command.RelayCommand<object>(DropTarget);
            LoadTargetCommand = new GalaSoft.MvvmLight.Command.RelayCommand<bool>(async (o) => { using (executeCTS = new CancellationTokenSource()) { await LoadTarget(); } });
            DeleteTargetCommand = new GalaSoft.MvvmLight.Command.RelayCommand<object>(DeleteTarget);
        }

        private Dispatcher _dispatcher = System.Windows.Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        public ICommand OpenFileCommand { get; private set; }
        public ICommand DropTargetCommand { get; set; }
        public ICommand LoadTargetCommand { get; set; }
        public ICommand DeleteTargetCommand { get; set; }

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
            get => new AsyncObservableCollection<SpeckleTarget>(SpeckleTargets.Where(x => x.ImageTarget).ToList());
        }

        public int SpeckleTargetCount {
            get => SpeckleTargets.Where(x => x.ImageTarget).Count();
        }

        private AsyncObservableCollection<GdsTarget> _gdsTargets;

        [JsonProperty]
        public AsyncObservableCollection<GdsTarget> GdsTargets {
            get => _gdsTargets;
            set {
                _gdsTargets = value;
                RaisePropertyChanged();
            }
        }

        private SimbadUtils SimUtils;
        private List<SimbadGalaxy> Galaxies = new List<SimbadGalaxy>();
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
                var templateName = string.IsNullOrWhiteSpace(SpeckleTarget.Template) ? speckle.DefaultTemplate : SpeckleTarget.Template;
                await LoadSpeckleTarget(templateName);

                var refTemplateName = string.IsNullOrWhiteSpace(SpeckleTarget.TemplateRef) ? speckle.DefaultRefTemplate : SpeckleTarget.TemplateRef;
                await LoadReferenceTarget(SpeckleTarget, string.IsNullOrWhiteSpace(refTemplateName) ? templateName : refTemplateName);
            }
        }
        public void DeleteTarget(Object o) {
            Logger.Debug("Object" + o.GetType());
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
            RegisterStatusCurrentTarget();
            SpeckleTarget = GetNextTarget();

            // Remove finished instructions
            foreach (ISequenceItem item in Items) {
                _ = _dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() => { this.Remove(item); }));
            }

            if (SpeckleTarget == null) {
                CurrentSpeckleTarget = null;
                await CoreUtil.Wait(TimeSpan.FromMilliseconds(300));
                RaiseAllPropertiesChanged();
                return false;
            }
            CurrentSpeckleTarget = SpeckleTarget;
            TargetNr++;

            var templateName = string.IsNullOrWhiteSpace(SpeckleTarget.Template) ? speckle.DefaultTemplate : SpeckleTarget.Template;
            await LoadSpeckleTarget(templateName).ConfigureAwait(false);

            var refTemplateName = string.IsNullOrWhiteSpace(SpeckleTarget.TemplateRef) ? speckle.DefaultRefTemplate : SpeckleTarget.TemplateRef;
            await LoadReferenceTarget(SpeckleTarget, string.IsNullOrWhiteSpace(refTemplateName) ? templateName : refTemplateName).ConfigureAwait(false);

            RaiseAllPropertiesChanged();
            return true;
        }

        public async Task LoadSpeckleTarget(string templateName) {

            var templates = sequenceMediator.GetDeepSkyObjectContainerTemplates();

            // Set target
            var template = templates.FirstOrDefault(x => x.Name == templateName);
            if (template == null) {
                Notification.ShowError("No template found. Check the selected template: " + templateName);
            }
            else {
                SpeckleTargetContainer speckleTargetContainer = (SpeckleTargetContainer)template.Clone();
                speckleTargetContainer.Target = new InputTarget(Angle.ByDegree(profileService.ActiveProfile.AstrometrySettings.Latitude), Angle.ByDegree(profileService.ActiveProfile.AstrometrySettings.Longitude), profileService.ActiveProfile.AstrometrySettings.Horizon) {
                    TargetName = SpeckleTarget.Target + "_c" + (SpeckleTarget.Completed_cycles + 1),
                    InputCoordinates = new InputCoordinates() {
                        Coordinates = SpeckleTarget.Coordinates()
                    },
                    PositionAngle = SpeckleTarget.Rotation
                };
                speckleTargetContainer.Title = SpeckleTarget.User;
                speckleTargetContainer.IsRef = false;
                speckleTargetContainer.SpeckleTarget = SpeckleTarget;
                speckleTargetContainer.Name = SpeckleTarget.User + "_" + SpeckleTarget.Target + "_c" + (SpeckleTarget.Completed_cycles + 1);
                speckleTargetContainer.Items.ToList().ForEach(x => {
                    if (x is TakeRoiExposures takeRoiExposures) {
                        takeRoiExposures.ExposureTime = SpeckleTarget.ExposureTime;
                        takeRoiExposures.TotalExposureCount = SpeckleTarget.Exposures;
                    }
                    if (x is TakeLiveExposures takeLiveExposures) {
                        takeLiveExposures.ExposureTime = SpeckleTarget.ExposureTime;
                        takeLiveExposures.TotalExposureCount = SpeckleTarget.Exposures;
                    }
                    if (x is WaitForTime waitForTime) {
                        waitForTime.Hours = SpeckleTarget.ImageTime.Hour;
                        waitForTime.Minutes = SpeckleTarget.ImageTime.Minute;
                        waitForTime.Seconds = SpeckleTarget.ImageTime.Second;
                    }
                    if (x is SwitchFilter switchFilter && SpeckleTarget.Filter != null && SpeckleTarget.Filter != "-" && SpeckleTarget.Filter.Length > 0) {
                        switchFilter.Filter = Filters?.FirstOrDefault(f => f.Name == SpeckleTarget.Filter);
                    }
                    if (x is MoveRotatorMechanical rotatorMechanical) {
                        rotatorMechanical.MechanicalPosition = (float)SpeckleTarget.Rotation;
                    }
                });
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

            var template = templates.FirstOrDefault(x => x.Name == templateName);
            if (template == null) {
                Notification.ShowError("No template found. Check the selected template: " + templateName);
            }
            else {
                using (executeCTS = new CancellationTokenSource()) {
                    await RetrieveReferenceStars(new Progress<ApplicationStatus>(p => AppStatus = p), executeCTS.Token).ConfigureAwait(false);
                }
                if (speckleTarget.ReferenceStar != null && speckleTarget.ReferenceStar.main_id != "") {
                    SpeckleTargetContainer speckleTargetContainerRef = (SpeckleTargetContainer)template.Clone();
                    speckleTargetContainerRef.Target = new InputTarget(Angle.ByDegree(profileService.ActiveProfile.AstrometrySettings.Latitude), Angle.ByDegree(profileService.ActiveProfile.AstrometrySettings.Longitude), profileService.ActiveProfile.AstrometrySettings.Horizon) {
                        TargetName = speckleTarget.Target + "_c" + (speckleTarget.Completed_cycles + 1) + "_ref_" + speckleTarget.ReferenceStar.main_id,
                        InputCoordinates = new InputCoordinates() {
                            Coordinates = speckleTarget.ReferenceStar.Coordinates()
                        },
                        PositionAngle = speckleTarget.Rotation
                    };
                    speckleTargetContainerRef.Title = speckleTarget.User;
                    speckleTargetContainerRef.IsRef = true;
                    speckleTargetContainerRef.Name = speckleTarget.User + "_" + speckleTarget.Target + "_" + (speckleTarget.Completed_cycles + 1) + "_ref_" + speckleTarget.ReferenceStar.main_id;
                    speckleTargetContainerRef.Items.ToList().ForEach(x => {
                        if (x is CalculateExposure calculateExposure) {
                            calculateExposure.ExposureTime = speckleTarget.ExposureTime;
                        }
                        if (x is TakeRoiExposures takeRoiExposures) {
                            takeRoiExposures.ExposureTime = speckleTarget.ExposureTime;
                            takeRoiExposures.TotalExposureCount = Math.Min(speckle.ReferenceExposures, speckleTarget.Exposures);
                        }
                        if (x is TakeLiveExposures takeLiveExposures) {
                            takeLiveExposures.ExposureTime = speckleTarget.ExposureTime;
                            takeLiveExposures.TotalExposureCount = Math.Min(speckle.ReferenceExposures, speckleTarget.Exposures);
                        }
                        if (x is WaitForTime waitForTime) {
                            waitForTime.Hours = speckleTarget.ImageTime.Hour;
                            waitForTime.Minutes = speckleTarget.ImageTime.Minute;
                            waitForTime.Seconds = speckleTarget.ImageTime.Second;
                        }
                        if (x is SwitchFilter switchFilter && speckleTarget.Filter != null && speckleTarget.Filter != "-" && speckleTarget.Filter.Length > 0) {
                            switchFilter.Filter = Filters?.FirstOrDefault(f => f.Name == speckleTarget.Filter);
                        }
                        if (x is MoveRotatorMechanical rotatorMechanical) {
                            rotatorMechanical.MechanicalPosition = (float)speckleTarget.Rotation;
                        }
                    });

                    await _dispatcher.BeginInvoke(DispatcherPriority.Send, new Action(() => {
                        lock (Items) {
                            this.InsertIntoSequenceBlocks(100, speckleTargetContainerRef);
                            Logger.Debug("Adding reference container: " + speckleTargetContainerRef);
                        }
                    }));
                }
            }
        }

        private SpeckleTarget GetNextTarget() {
            Logger.Debug("Getting next target from list: " + SpeckleTargets.Count + " targets total.");

            var targetAzimuth = speckle.DomePosition;
            var quarterAgo = DateTime.Now.AddMinutes(-15);
            var slitAz1 = speckle.DomePosition - (speckle.DomeSlitWidth / 2);
            var slitAz2 = speckle.DomePosition + (speckle.DomeSlitWidth / 2);

            // First get the next target with an imageTime in the future
            var targets = SpeckleTargets.Where(t => t.ImageTarget)
                .Where(t => t.Nights > t.Completed_nights)
                .Where(t => t.Cycles > t.Completed_cycles)
                .Where(t => t.ImagedAt == null || t.ImagedAt < quarterAgo)
                .Where(t => t.DomeSlitAltTimeList.Count > 0)
                .Where(t => t.DomeSlitObservationStartTime > DateTime.Now)
                .OrderBy(t => t.DomeSlitObservationStartTime); // Favor targets that have been observed before

            if (targets.Count() > 0) {
                Logger.Debug(JsonConvert.SerializeObject(targets, Formatting.Indented));
                SpeckleTarget = targets.First();
                var altTime = SpeckleTarget.getCurrentDomeAltTime();
                SpeckleTarget.ImageTime = altTime?.datetime ?? DateTime.Now;
            } else {
                DateTime maxImageTime = DateTime.Now.AddMinutes(-5);
                SpeckleTarget = SpeckleTargets.Where(t => t.ImageTarget)
                    .Where(t => t.Nights > t.Completed_nights)
                    .Where(t => t.Cycles > t.Completed_cycles)
                    .Where(t => t.ImageTime > maxImageTime)
                    .OrderBy(t => t.Completed_cycles)
                    .ThenBy(t => t.ImageTime)
                    .FirstOrDefault();
            }
            //SpeckleTarget.AltList.Where(x => x.datetime > maxImageTime).Count() // && x.datetime < DateTime.Now.AddHours(8)).Count()
            //    .Where(x => x.alt > speckle.AltitudeMin && x.alt < speckle.AltitudeMax)
            //    //.Where(x => x.airmass > this.AirmassMin && x.airmass < this.AirmassMax)
            //    .OrderByDescending(x => x.alt).FirstOrDefault()

            // Check imagetime is within 5 minutes, then set it to now
            if (SpeckleTarget.ImageTime > DateTime.Now && SpeckleTarget.ImageTime < DateTime.Now.AddMinutes(5)) {
                SpeckleTarget.ImageTime = DateTime.Now;
            }
            return SpeckleTarget;
        }

        private SpeckleTarget GetNextTargetOld() {
            Logger.Debug("Getting next target from list: " + SpeckleTargets.Count + " targets total.");
            
            // First get the next target with an imageTime in the future
            DateTime maxImageTime = DateTime.Now.AddMinutes(-5);
            SpeckleTarget = SpeckleTargets.Where(t => t.ImageTarget)
                .Where(t => t.Nights > t.Completed_nights)
                .Where(t => t.Cycles > t.Completed_cycles)
                .Where(t => t.ImageTime > maxImageTime)
                .OrderBy(t => t.Completed_cycles)
                .ThenBy(t => t.ImageTime)
                .FirstOrDefault();
            if (SpeckleTarget != null) {
                Logger.Debug("Getting next target " + SpeckleTarget.Target);

                // When the target is more than 6 minutes away, check for earlier targets that can be used as a fill in.
                if (SpeckleTarget.ImageTime > DateTime.Now.AddMinutes(6)) {
                    maxImageTime = SpeckleTarget.ImageTime;
                    var fillinTarget = SpeckleTargets.Where(t => t.ImageTarget)
                        .Where(t => t.Nights > t.Completed_nights)
                        .Where(t => t.Cycles > t.Completed_cycles)
                        .Where(t => t.ImageTime < maxImageTime)
                        .Where(t => t.getCurrentAltTime(speckle.AltitudeMax, speckle.MDistance) != null
                            && t.getCurrentAltTime(speckle.AltitudeMax, speckle.MDistance).alt > t.MinAltitude
                            && t.getCurrentAltTime(speckle.AltitudeMax, speckle.MDistance).distanceToMoon > speckle.MoonDistance)
                        .OrderBy(t => t.Completed_cycles)
                        .ThenBy(t => t.getCurrentAltTime(speckle.AltitudeMax, speckle.MDistance).alt)
                        .FirstOrDefault();
                    if (fillinTarget != null) {
                        Logger.Debug("Getting fillin target " + fillinTarget.Target);
                        SpeckleTarget = fillinTarget;
                    } else if (speckle.GetGalaxyFillins) {
                        // Getting fillin galaxy
                        var coords = new InputTopocentricCoordinates(Angle.ByDegree(profileService.ActiveProfile.AstrometrySettings.Latitude), Angle.ByDegree(profileService.ActiveProfile.AstrometrySettings.Longitude));
                        coords.AltDegrees = 85;
                        using (executeCTS = new CancellationTokenSource()) {
                            if (Galaxies.Count <= 0)
                                Galaxies = SimUtils.FindSimbadGalaxies(new Progress<ApplicationStatus>(p => AppStatus = p), executeCTS.Token, coords.Coordinates.Transform(Epoch.J2000), 30, speckle.MaxGalaxyMag).Result;
                            SimbadGalaxy galaxy = Galaxies.FirstOrDefault();
                            // TODO: check altitude and distance to the moon
                            if (galaxy != null) {
                                Logger.Debug("Getting fillin galaxy target: " + galaxy.main_id);
                                SpeckleTarget = new SpeckleTarget();
                                SpeckleTarget.User = "Galaxies";
                                SpeckleTarget.Ra = galaxy.ra.ToString();
                                SpeckleTarget.Dec = galaxy.dec.ToString();
                                SpeckleTarget.Target = galaxy.main_id;
                                SpeckleTarget.PMag = galaxy.v_mag;
                                SpeckleTarget.Template = speckle.GalaxyTemplate;
                                SpeckleTarget.GetRef = 0;
                                SpeckleTarget.RegisterTarget = false;
                            }
                            Galaxies.Remove(galaxy);
                        }
                    }
                }
            } else {
                Logger.Debug("No next target. Looking for previous target.");
                var fillinTarget = SpeckleTargets.Where(t => t.ImageTarget)
                    .Where(t => t.Nights > t.Completed_nights)
                    .Where(t => t.Cycles > t.Completed_cycles)
                    .Where(t => t.getCurrentAltTime(speckle.AltitudeMax, speckle.MDistance) != null 
                        && t.getCurrentAltTime(speckle.AltitudeMax, speckle.MDistance).alt > t.MinAltitude
                        && t.getCurrentAltTime(speckle.AltitudeMax, speckle.MDistance).distanceToMoon > speckle.MoonDistance)
                    .OrderBy(t => t.getCurrentAltTime(speckle.AltitudeMax, speckle.MDistance).alt)
                    .FirstOrDefault();
                if (fillinTarget != null) {
                    Logger.Debug("Getting fillin target " + fillinTarget.Target);
                    SpeckleTarget = fillinTarget;
                }
            }

            // Check imagetime is within 5 minutes, then set it to now
            if (SpeckleTarget.ImageTime > DateTime.Now && SpeckleTarget.ImageTime < DateTime.Now.AddMinutes(5)) {
                SpeckleTarget.ImageTime = DateTime.Now;
            }
            return SpeckleTarget;
        }

        private void RegisterStatusCurrentTarget() {
            if (CurrentSpeckleTarget == null) return;
            SpeckleTarget = SpeckleTargets.Where(x => x.TargetId == CurrentSpeckleTarget.TargetId).FirstOrDefault();
            if (SpeckleTarget != null) {
                if (!SpeckleTarget.RegisterTarget) return;
                SpeckleTarget.ImagedAt = DateTime.Now;
                SpeckleTarget.Completed_cycles += 1;
                if (SpeckleTarget.Completed_cycles == SpeckleTarget.Cycles) {
                    SpeckleTarget.Completed_nights += 1;
                }
            }
            /*            var nights = false;
                        foreach (ISequenceItem item in Items) {
                            var container = item as SpeckleTargetContainer;
                            if (container != null && !container.Target.TargetName.Contains("_ref_")) {
                                List<ISequenceItem> nonFinishedItems = container.Items.Where(x => x.Status != SequenceEntityStatus.FINISHED).Cast<ISequenceItem>().ToList();
                                if (nonFinishedItems.Count == 0) {
                                    SpeckleTarget.Completed_cycles += 1;
                                } else {
                                    var exposures = nonFinishedItems.Where(x => x is TakeLiveExposures || x is TakeRoiExposures).FirstOrDefault();
                                    if (exposures != null) {
                                        SpeckleTarget.Completed_cycles += 1; // TODO maybe add an error marker or something
                                    }
                                }
                                if (nights == false && SpeckleTarget.Completed_cycles == SpeckleTarget.Cycles) {
                                    SpeckleTarget.Completed_nights += 1;
                                    nights = true;
                                }
                            }
                            if (container != null && container.Target.TargetName.Contains("_ref_")) {
                                List<ISequenceItem> nonFinishedItems = container.Items.Where(x => x.Status != SequenceEntityStatus.FINISHED).Cast<ISequenceItem>().ToList();
                                if (nonFinishedItems.Count == 0) {
                                    SpeckleTarget.Completed_ref_cycles += 1;
                                } else {
                                    var exposures = nonFinishedItems.Where(x => x is TakeLiveExposures || x is TakeRoiExposures).FirstOrDefault();
                                    if (exposures == null) {
                                        SpeckleTarget.Completed_ref_cycles += 1; // TODO maybe add an error marker or something
                                    }
                                }
                            }
                        }*/

            // Write current status to the target csv file
            string csvfile = Path.Combine(profileService.ActiveProfile.ImageFileSettings.FilePath, "TargetList-" + DateTime.Now.AddHours(-12).ToString("yyyy-MM-dd") + ".csv");
            using (var writer = new StreamWriter(csvfile))
            using (var csv = new CsvWriter(writer, CultureInfo.InvariantCulture)) {
                csv.Context.RegisterClassMap<SpeckleTargetMap>();
                csv.WriteRecords(SpeckleTargets);
            }
        }

        private void DropTarget(object obj) {
            var p = obj as NINA.Sequencer.DragDrop.DropIntoParameters;
            if (p != null) {
                var con = p.Source as TargetSequenceContainer;
                if (con != null) {
                    var dropTarget = con.Container.Target;
                    if (dropTarget != null) {
                        // TODO Add target/template to list
/*                        this.Name = dropTarget.TargetName;
                        this.Target.TargetName = dropTarget.TargetName;
                        this.Target.InputCoordinates = dropTarget.InputCoordinates.Clone();
                        this.Target.Rotation = dropTarget.Rotation;*/
                    }
                }
            }
        }

        private async Task RetrieveReferenceStars(IProgress<ApplicationStatus> externalProgress, CancellationToken token) {
            if (SpeckleTarget.GetRef > 0 && (SpeckleTarget.ReferenceStarList == null || !SpeckleTarget.ReferenceStarList.Any())) {
                SimbadStar targetStar;
                double targetColor;

                try {
                    targetStar = await RetrieveTargetStar(externalProgress, token);
                    if (targetStar == null) throw new Exception("Target star not found.");
                    targetColor = targetStar.color;
                } catch (Exception ex) {
                    Logger.Debug("Couldn't find target star for SpeckleTarget. Assuming G-type star. Error: " + ex.Message);
                    targetColor = 0.65;  // b-v of a g type star
                }

                double magnitude = SpeckleTarget.PMag > 1 ? Math.Min(SpeckleTarget.PMag - 1, Math.Min(SpeckleTarget.SMag - 1, 8d)) : 8d;
                SpeckleTarget.ReferenceStarList = await SimUtils.FindSimbadSaoStars(externalProgress, token, SpeckleTarget.Coordinates(), speckle.SearchRadius, magnitude, speckle.MaxReferenceMag).ConfigureAwait(false);

                if (speckle.DomePositionLock) {
                    var slitAz1 = speckle.DomePosition - (speckle.DomeSlitWidth / 2);
                    var slitAz2 = speckle.DomePosition + (speckle.DomeSlitWidth / 2);
                    foreach (var rstar in SpeckleTarget.ReferenceStarList) {
                        rstar.AltList = GetAltList(rstar.Coordinates());
                        rstar.setDomeSlitAltTimeList(speckle, slitAz1, slitAz2);
                    }

                    // prioritize stars with a matching color and those which are at the top of setDomeSlitAltTimeList => these have the most time to pass across the shutter
                    // rather prioritize targets that don't perfectly match in color, but there is some time to still observe it, vs a target that's perfect but gone in 20s
                    SpeckleTarget.ReferenceStar = SpeckleTarget.ReferenceStarList
                        .Where(r => r.setDomeSlitAltTimeList != null && r.setDomeSlitAltTimeList.Any())
                        .OrderBy(r => Math.Abs(r.color - targetColor))
                        .ThenBy(r => r.setDomeSlitAltTimeList.OrderBy(altTime => altTime.datetime).FirstOrDefault()?.datetime)
                        .FirstOrDefault();

                    Logger.Debug(JsonConvert.SerializeObject(SpeckleTarget.ReferenceStarList, Formatting.Indented));
                } else {
                    // color match first, then distance (the distance is already limited in the simbadutils)
                    SpeckleTarget.ReferenceStar = SpeckleTarget.ReferenceStarList
                        .OrderBy(r => Math.Abs(r.color - targetColor))
                        .ThenBy(r => r.distance)
                        .FirstOrDefault();
                }
                if (SpeckleTarget.ReferenceStar == null) {
                    Logger.Debug("Couldn't find reference SAO star for SpeckleTarget within " + speckle.SearchRadius + " degrees and magnitudes: " + magnitude + " and " + speckle.MaxReferenceMag);
                }
                RaiseAllPropertiesChanged();
            }
        }

        private async Task<SimbadStar> RetrieveTargetStar(IProgress<ApplicationStatus> externalProgress, CancellationToken token) {
            var coords = SpeckleTarget.Coordinates();
            var magnitude = SpeckleTarget.PMag;
            return await SimUtils.GetStarByPosition(externalProgress, token, coords.RADegrees, coords.Dec, magnitude);
        }

        private List<AltTime> GetAltList(Coordinates coords) {
            var start = DateTime.Now; // NighttimeData.NauticalTwilightRiseAndSet.Set ?? DateTime.Now;
            start = start.AddHours(-1);
            var siderealTime = AstroUtil.GetLocalSiderealTime(start, profileService.ActiveProfile.AstrometrySettings.Longitude);
            var hourAngle = AstroUtil.GetHourAngle(siderealTime, coords.RA);
            var end = DateTime.Now.AddHours(10); // NighttimeData.NauticalTwilightRiseAndSet.Rise ?? DateTime.Now.AddHours(23);
            end = end.AddHours(1);
            var siderealEndTime = AstroUtil.GetLocalSiderealTime(end, profileService.ActiveProfile.AstrometrySettings.Longitude);
            var hourEndAngle = AstroUtil.GetHourAngle(siderealEndTime, coords.RA);
            if (hourEndAngle < hourAngle) {
                hourEndAngle += 24;
            }
            
            List<AltTime> altList = new List<AltTime>();
            for (double angle = hourAngle; angle < hourEndAngle; angle += speckle.DomePositionLock ? 0.01 : 0.05) {
                var degAngle = AstroUtil.HoursToDegrees(angle);
                var altitude = AstroUtil.GetAltitude(degAngle, profileService.ActiveProfile.AstrometrySettings.Latitude, coords.Dec);
                var azimuth = AstroUtil.GetAzimuth(degAngle, altitude, profileService.ActiveProfile.AstrometrySettings.Latitude, coords.Dec);
                var horizonAltitude = 0d;
                if (profileService.ActiveProfile.AstrometrySettings.Horizon != null) {
                    horizonAltitude = profileService.ActiveProfile.AstrometrySettings.Horizon.GetAltitude(azimuth);
                }
                // Run the whole thing and get the top value
                if (altitude > horizonAltitude)
                    altList.Add(new AltTime(altitude, azimuth, degAngle, start, AstroUtil.Airmass(altitude), CalculateSeparation(start, coords)));
                start = start.AddHours(speckle.DomePositionLock ? 0.01 : 0.05);
            }
            return altList;
        }

        private void OpenFile() {
            OpenFileDialog fileDialog = new OpenFileDialog();
            fileDialog.DefaultExt = ".csv"; // Required file extension 
            fileDialog.Filter = "Csv documents (.csv)|*.csv"; // Optional file extensions

            if (fileDialog.ShowDialog() == DialogResult.OK) {
                LoadTargets(fileDialog.FileName);
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

        private Task<bool> LoadTargets(string file) {
            LoadingTargets = true;
            return Task.Run(() => {
                if (DateTime.Now.Hour <= 12 && DateTime.Now.Hour >= 8) {
                    NighttimeData = nighttimeCalculator.Calculate(DateTime.Now.AddHours(5));
                } else {
                    NighttimeData = nighttimeCalculator.Calculate();
                }
                try {
                    var slitAz1 = speckle.DomePosition - (speckle.DomeSlitWidth / 2);
                    var slitAz2 = speckle.DomePosition + (speckle.DomeSlitWidth / 2);
                    var config = new CsvConfiguration(CultureInfo.InvariantCulture);
                    config.MissingFieldFound = null;
                    using (var reader = new StreamReader(file))
                    using (var csv = new CsvReader(reader, config)) {
                        // Do any configuration to `CsvReader` before creating CsvDataReader.
                        using (var dr = new CsvDataReader(csv)) {
                            csv.Context.RegisterClassMap<GdsTargetClassMap>();
                            var records = csv.GetRecords<GdsTarget>();
                            foreach (GdsTarget record in records.ToList()) {
                                if (record.RA0 == null || record.Decl0 == null) {
                                    Notification.ShowError("Csv file doesn't contain mandatory RA0 and/or Decl0 columns.");
                                    LoadingTargets = false;
                                    return false;
                                }
                                GdsTargets.Add(record);
                                SpeckleTarget speckleTarget = new SpeckleTarget();
                                speckleTarget.Ra = record.RA0.Trim();
                                speckleTarget.Dec = record.Decl0.Trim();
                                speckleTarget.RA0 = record.RA0.Trim();
                                speckleTarget.Decl0 = record.Decl0.Trim();
                                speckleTarget.User = record.User.Trim() != "" ? record.User.Trim() : User.Trim() != "" ? User.Trim() : speckle.User;
                                speckleTarget.Target = record.Target.Trim() != "" ? record.Target.Trim() : record.WDSName != null && record.WDSName.Trim() != "" ? record.WDSName.Trim() + "_" + record.DD.Trim() :
                                    "Ra" + speckleTarget.Coordinates().RAString.Replace(":", "_") + "_Dec" + speckleTarget.Coordinates().DecString.Replace(" ", "_");
                                if (speckleTarget.Coordinates().RADegrees == 0d && speckleTarget.Coordinates().Dec == 0d) {
                                    Logger.Debug("No coordinates found. Skipping target " + speckleTarget.Target + " for user " + speckleTarget.User);
                                    continue;
                                }
                                if (record.Nights > 0 && record.Nights <= record.Completed_nights) {
                                    Logger.Debug("Target already imaged enough nights. Skipping target " + speckleTarget.Target + " for user " + speckleTarget.User);
                                    speckleTarget.ImageTarget = false; // Can't image this target
                                    speckleTarget.Note = "Target finished.";
                                }
                                if (record.Gmag0 > 0 && (record.Gmag0 < speckle.MinMag || record.Gmag0 > speckle.MaxMag)) {
                                    Logger.Debug("Magnitude not within limits. Skipping target " + speckleTarget.Target + " for user " + speckleTarget.User);
                                    speckleTarget.ImageTarget = false; // Can't image this target
                                    speckleTarget.Note = "Target not within magnitude limits.";
                                }
                                speckleTarget.Separation = record.Separation != 0 ? record.Separation : record.GaiaSep;
                                if (speckleTarget.Separation > 0 && (speckleTarget.Separation < speckle.MinSep || speckleTarget.Separation > speckle.MaxSep)) {
                                    Logger.Debug("Seperation not within limits. Skipping target " + speckleTarget.Target + " for user " + speckleTarget.User);
                                    speckleTarget.ImageTarget = false; // Can't image this target
                                    speckleTarget.Note = "Target not within separation limits.";
                                }
                                speckleTarget.Cycles = record.Cycles > 0 ? record.Cycles : Cycles;
                                speckleTarget.Nights = record.Nights > 0 ? record.Nights : speckle.Nights;
                                speckleTarget.AirmassMin = record.AirmassMin;
                                speckleTarget.AirmassMax = record.AirmassMax;
                                speckleTarget.MinAltitude = record.MinAltitude == 0d ? speckle.AltitudeMin : record.MinAltitude;
                                speckleTarget.AltList = GetAltList(speckleTarget.Coordinates());
                                speckleTarget.setDomeSlitAltTimeList(speckle, slitAz1, slitAz2, speckleTarget.AirmassMin, speckleTarget.AirmassMax);
                                var imageTo = speckleTarget.DomeSlitAltTimeList.OrderBy(x => x.datetime).FirstOrDefault();
                                if (!speckle.DomePositionLock) {
                                    imageTo = speckleTarget.ImageTo(NighttimeData, speckle.AltitudeMax, speckle.MDistance, speckleTarget.AirmassMin, speckleTarget.AirmassMax, speckle.MoonDistance);
                                }
                                if (imageTo != null && imageTo.alt > speckleTarget.MinAltitude && imageTo.datetime >= NighttimeData.NauticalTwilightRiseAndSet.Set && imageTo.datetime <= NighttimeData.NauticalTwilightRiseAndSet.Rise) {
                                    speckleTarget.ImageTime = speckle.DomePositionLock ? imageTo.datetime : RoundUp(imageTo.datetime, TimeSpan.FromMinutes(5));
                                    speckleTarget.ImageTimeAlt = imageTo.alt;
                                }
                                else {
                                    Logger.Debug("Image time not within limits or too close to the moon. Skipping target " + speckleTarget.Target + " for user " + speckleTarget.User);
                                    speckleTarget.ImageTarget = false; // Can't image this target
                                    speckleTarget.Note = "Target cannot be imaged tonight.";
                                }
                                speckleTarget.Template = record.Template != "" ? record.Template : Template != "" ? Template : speckle.DefaultTemplate;
                                speckleTarget.TemplateRef = record.TemplateRef != "" ? record.TemplateRef : TemplateRef != "" ? TemplateRef : speckle.DefaultRefTemplate;
                                speckleTarget.Exposures = record.Exposures > 0 ? record.Exposures : Exposures;
                                speckleTarget.ExposureTime = record.ExposureTime > 0 ? record.ExposureTime : ExposureTime;
                                speckleTarget.NoCalculation = record.NoCalculation;
                                speckleTarget.PMag = record.Gmag0 != 0 ? record.Gmag0 : record.PMag;
                                speckleTarget.SMag = record.Gmag1 != 0 ? record.Gmag1 : record.SMag;
                                if (speckleTarget.SMag == 0) {
                                    Logger.Debug("Failed to get secondary magnitude for " + speckleTarget.Target);
                                    speckleTarget.NoCalculation = 1;
                                }
                                speckleTarget.Completed_nights = record.Completed_nights;
                                speckleTarget.Filter = record.Filter;
                                speckleTarget.Priority = record.Priority;
                                speckleTarget.Rotation = record.Rotation;
                                speckleTarget.GetRef = record.GetRef;
                                SpeckleTargets.Add(speckleTarget);
                                RaisePropertyChanged("SpeckleTargetCount");
                                RaisePropertyChanged("SpeckleTargetsView");
                            }
                            Logger.Debug("Loaded " + SpeckleTargets.Count + " speckletargets");

                            SpeckleTargets = new AsyncObservableCollection<SpeckleTarget>(
                                SpeckleTargets.Where(i => i.ImageTime != null).GroupBy(i => i.ImageTime)
                                .SelectMany(g => g.OrderByDescending(n => n.Priority).ThenByDescending(i => i.ImageTimeAlt).ToList())
                                .OrderBy(i => i.ImageTime).ThenBy(i => i.ImageTimeAlt));
                        }
                    }
                } catch (Exception e) {
                    Notification.ShowError("Failed to load list. " + e.Message);
                }
                LoadingTargets = false;
                return true;
            });
        }

        private DateTime RoundUp(DateTime dt, TimeSpan d) {
            return new DateTime((dt.Ticks + d.Ticks - 1) / d.Ticks * d.Ticks, dt.Kind);
        }

        private double CalculateSeparation(DateTime time, Coordinates coords) {
            NOVAS.SkyPosition pos = AstroUtil.GetMoonPosition(time, AstroUtil.GetJulianDate(time), new ObserverInfo { Latitude = profileService.ActiveProfile.AstrometrySettings.Latitude, Longitude = profileService.ActiveProfile.AstrometrySettings.Longitude, Elevation = profileService.ActiveProfile.AstrometrySettings.Elevation });
            var moonRaRadians = AstroUtil.ToRadians(AstroUtil.HoursToDegrees(pos.RA));
            var moonDecRadians = AstroUtil.ToRadians(pos.Dec);

            Coordinates target = coords.Transform(Epoch.JNOW);
            var targetRaRadians = AstroUtil.ToRadians(target.RADegrees);
            var targetDecRadians = AstroUtil.ToRadians(target.Dec);

            var theta = SOFA.Seps(moonRaRadians, moonDecRadians, targetRaRadians, targetDecRadians);
            return AstroUtil.ToDegree(theta); // return separation
        }

        public override object Clone() {
            var clone = new SpeckleTargetListContainer(profileService, sequenceMediator, nighttimeCalculator, applicationStatusMediator, cameraMediator, framingAssistantVM, applicationMediator, telescopeMediator) {
                Icon = Icon,
                Name = Name,
                Category = Category,
                Description = Description,
                Items = new ObservableCollection<ISequenceItem>(Items.Select(i => i.Clone() as ISequenceItem)),
                Triggers = new ObservableCollection<ISequenceTrigger>(Triggers.Select(t => t.Clone() as ISequenceTrigger)),
                Conditions = new ObservableCollection<ISequenceCondition>(Conditions.Select(t => t.Clone() as ISequenceCondition)),
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