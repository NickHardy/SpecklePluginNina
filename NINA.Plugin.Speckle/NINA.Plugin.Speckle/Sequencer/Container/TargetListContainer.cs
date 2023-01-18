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

    [ExportMetadata("Name", "Target List Container")]
    [ExportMetadata("Description", "Lbl_SequenceContainer_DeepSkyObjectContainer_Description")]
    [ExportMetadata("Icon", "SequentialSVG")]
    [ExportMetadata("Category", "Speckle Interferometry")]
    [Export(typeof(ISequenceItem))]
    [Export(typeof(ISequenceContainer))]
    [JsonObject(MemberSerialization.OptIn)]
    public class TargetListContainer : SequenceContainer {
        private readonly IProfileService profileService;
        private ISequenceMediator sequenceMediator;
        private IApplicationStatusMediator applicationStatusMediator;
        private ICameraMediator cameraMediator;
        private readonly IFramingAssistantVM framingAssistantVM;
        private readonly IApplicationMediator applicationMediator;
        private INighttimeCalculator nighttimeCalculator;
        private Speckle speckle;
        private CancellationTokenSource executeCTS;

        [ImportingConstructor]
        public TargetListContainer(
                IProfileService profileService,
                ISequenceMediator sequenceMediator,
                INighttimeCalculator nighttimeCalculator,
                IApplicationStatusMediator applicationStatusMediator,
                ICameraMediator cameraMediator,
                IFramingAssistantVM framingAssistantVM,
                IApplicationMediator applicationMediator) : base(new SequentialListStrategy()) {
            this.profileService = profileService;
            this.sequenceMediator = sequenceMediator;
            this.nighttimeCalculator = nighttimeCalculator;
            this.applicationStatusMediator = applicationStatusMediator;
            this.cameraMediator = cameraMediator;
            this.applicationMediator = applicationMediator;
            this.framingAssistantVM = framingAssistantVM;

            //Task.Run(() => NighttimeData = nighttimeCalculator.Calculate(DateTime.Now.AddHours(4)));
            speckle = new Speckle();
            TargetNr = 0;
            User = speckle.User;
            Template = speckle.DefaultTemplate;
            Cycles = speckle.Cycles;
            Exposures = speckle.Exposures;
            ExposureTime = speckle.ExposureTime;
            ListTargets = new AsyncObservableCollection<ListTarget>();
            GdsTargets = new AsyncObservableCollection<GdsTarget>();
            SimUtils = new SimbadUtils();

            RetrieveTemplates();
            OpenFileCommand = new GalaSoft.MvvmLight.Command.RelayCommand<bool>((o) => { using (executeCTS = new CancellationTokenSource()) { OpenFile(); } });
            DropTargetCommand = new GalaSoft.MvvmLight.Command.RelayCommand<object>(DropTarget);
            LoadTargetCommand = new GalaSoft.MvvmLight.Command.RelayCommand<object>(LoadTarget);
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

        private int _Cycles;

        [JsonProperty]
        public int Cycles { get => _Cycles; set { _Cycles = value; RaisePropertyChanged(); } }

        private int _Exposures;

        [JsonProperty]
        public int Exposures { get => _Exposures; set { _Exposures = value; RaisePropertyChanged(); } }

        private double _ExposureTime;

        [JsonProperty]
        public double ExposureTime { get => _ExposureTime; set { _ExposureTime = value; RaisePropertyChanged(); } }

        private ListTarget _ListTarget;

        [JsonProperty]
        public ListTarget ListTarget {
            get => _ListTarget;
            set {
                _ListTarget = value;
                RaisePropertyChanged();
            }
        }

        private ListTarget _currentListTarget;

        [JsonProperty]
        public ListTarget CurrentListTarget {
            get => _currentListTarget;
            set {
                _currentListTarget = value;
                RaisePropertyChanged();
            }
        }

        private AsyncObservableCollection<ListTarget> _ListTargets;

        [JsonProperty]
        public AsyncObservableCollection<ListTarget> ListTargets {
            get => _ListTargets;
            set {
                _ListTargets = value;
                RaisePropertyChanged();
            }
        }

        public int ListTargetCount {
            get => ListTargets.Where(x => x.ImageTarget).Count();
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
        private AsyncObservableCollection<TargetListContainer> _speckleTemplates = new AsyncObservableCollection<TargetListContainer>();

        public AsyncObservableCollection<TargetListContainer> SpeckleTemplates {
            get => _speckleTemplates;
            set {
                _speckleTemplates = value;
                RaisePropertyChanged();
            }
        }

        public void LoadTarget(Object o) {
            Logger.Debug("Object" + o.GetType());
        }
        public void DeleteTarget(Object o) {
            Logger.Debug("Object" + o.GetType());
        }

        public void RetrieveTemplates() {
            if (sequenceMediator.Initialized) {
                SpeckleTemplates.Clear();
                var templates = sequenceMediator.GetDeepSkyObjectContainerTemplates();
                foreach (var template in templates) {
                    var speckleTemplate = template as TargetListContainer;
                    if (speckleTemplate != null)
                        SpeckleTemplates.Add(speckleTemplate);
                }
            }
        }

        public bool LoadNewTarget() {
            RegisterStatusCurrentTarget();
            ListTarget = GetNextTarget();

            // Remove finished instructions
            foreach (ISequenceItem item in Items) {
                _ = _dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() => { this.Remove(item); }));
            }

            if (ListTarget == null) {
                CurrentListTarget = null;
                CoreUtil.Wait(TimeSpan.FromMilliseconds(300));
                RaiseAllPropertiesChanged();
                return false;
            }
            CurrentListTarget = ListTarget;
            TargetNr++;

            var templates = sequenceMediator.GetDeepSkyObjectContainerTemplates();

            // Set target
            var template = templates.FirstOrDefault(x => x.Name == (ListTarget.Template != "" ? ListTarget.Template : speckle.DefaultTemplate));
            if (template == null) {
                Notification.ShowError("No template found. Check the selected template: " + ListTarget.Template);
                return false;
            }
            TargetContainer my1TargetContainer = (TargetContainer)template.Clone();
            my1TargetContainer.Target = new InputTarget(Angle.ByDegree(profileService.ActiveProfile.AstrometrySettings.Latitude), Angle.ByDegree(profileService.ActiveProfile.AstrometrySettings.Longitude), profileService.ActiveProfile.AstrometrySettings.Horizon) {
                TargetName = ListTarget.Target + "_c" + (ListTarget.Completed_cycles + 1),
                InputCoordinates = new InputCoordinates() {
                    Coordinates = ListTarget.Coordinates()
                },
                Rotation = ListTarget.Rotation
            };
            my1TargetContainer.Title = ListTarget.User;
            my1TargetContainer.Name = ListTarget.User + "_" + ListTarget.Target + "_c" + (ListTarget.Completed_cycles + 1);
            my1TargetContainer.Items.ToList().ForEach(x => {
                if (x is CalculateExposure calculateExposure)
                {
                    calculateExposure.ExposureTime = ListTarget.ExposureTime;
                }
                if (x is TakeRoiExposures takeRoiExposures) {
                    takeRoiExposures.ExposureTime = ListTarget.ExposureTime;
                    takeRoiExposures.TotalExposureCount = ListTarget.Exposures;
                }
                if (x is TakeLiveExposures takeLiveExposures) {
                    takeLiveExposures.ExposureTime = ListTarget.ExposureTime;
                    takeLiveExposures.TotalExposureCount = ListTarget.Exposures;
                }
                if (x is CalculateRoiExposureTime calculateRoiExposureTime) {
                    calculateRoiExposureTime.ExposureTime = ListTarget.ExposureTime;
                    calculateRoiExposureTime.ExposureTimeMax = ListTarget.ExposureTime;
                }
                if (x is WaitForTime waitForTime) {
                    waitForTime.Hours = ListTarget.ImageTime.Hour;
                    waitForTime.Minutes = ListTarget.ImageTime.Minute;
                    waitForTime.Seconds = ListTarget.ImageTime.Second;
                }
                if (x is SwitchFilter switchFilter && ListTarget.Filter != null && ListTarget.Filter != "-" && ListTarget.Filter.Length > 0) {
                    switchFilter.Filter = Filters?.FirstOrDefault(f => f.Name == ListTarget.Filter);
                }
                if (x is MoveRotatorMechanical rotatorMechanical) {
                    rotatorMechanical.MechanicalPosition = (float)ListTarget.Rotation;
                }
            });
            _ = _dispatcher.BeginInvoke(DispatcherPriority.Send, new Action(() => {
                lock (Items) {
                    this.InsertIntoSequenceBlocks(100, my1TargetContainer);
                    Logger.Debug("Adding target container: " + my1TargetContainer);
                }
            }));

            /* Set Reference
             * 
             * Uncertain whether to have this here as it's exclusively used for speckle targets. Maybe keep the "get a reference" part of the function, and then decide what to do based on what it is?
             * Speckle target = Move to reference, calculate speckle exptime, speckle imaging.
             * Asteroid = Also move to reference, but use different calculation (?), then normal imaging.
             * 
            using (executeCTS = new CancellationTokenSource()) {
                RetrieveReferenceStars(new Progress<ApplicationStatus>(p => AppStatus = p), executeCTS.Token);
            }
            if (ListTarget.ReferenceStar != null && ListTarget.ReferenceStar.main_id != "") {
                TargetContainer myTargetContainer = (TargetContainer)template.Clone();
                myTargetContainer.Target = new InputTarget(Angle.ByDegree(profileService.ActiveProfile.AstrometrySettings.Latitude), Angle.ByDegree(profileService.ActiveProfile.AstrometrySettings.Longitude), profileService.ActiveProfile.AstrometrySettings.Horizon) {
                    TargetName = ListTarget.Target + "_c" + (ListTarget.Completed_cycles + 1) + "_ref_" + ListTarget.ReferenceStar.main_id,
                    InputCoordinates = new InputCoordinates() {
                        Coordinates = ListTarget.ReferenceStar.Coordinates()
                    },
                    Rotation = ListTarget.Rotation
                };
                myTargetContainer.Title = ListTarget.User;
                myTargetContainer.Name = ListTarget.User + "_" + ListTarget.Target + "_" + (ListTarget.Completed_cycles + 1) + "_ref_" + ListTarget.ReferenceStar.main_id;
                myTargetContainer.Items.ToList().ForEach(x => {
                    if (x is CalculateExposure calculateExposure)
                    {
                        calculateExposure.ExposureTime = ListTarget.ExposureTime;
                    }
                    if (x is TakeRoiExposures takeRoiExposures) {
                        takeRoiExposures.ExposureTime = ListTarget.ExposureTime;
                        takeRoiExposures.TotalExposureCount = Math.Min(speckle.ReferenceExposures, ListTarget.Exposures);
                    }
                    if (x is TakeLiveExposures takeLiveExposures) {
                        takeLiveExposures.ExposureTime = ListTarget.ExposureTime;
                        takeLiveExposures.TotalExposureCount = Math.Min(speckle.ReferenceExposures, ListTarget.Exposures);
                    }
                    if (x is CalculateRoiExposureTime calculateRoiExposureTime) {
                        calculateRoiExposureTime.ExposureTime = ListTarget.ExposureTime;
                        calculateRoiExposureTime.ExposureTimeMax = ListTarget.ExposureTime;
                    }
                    if (x is WaitForTime waitForTime) {
                        waitForTime.Hours = ListTarget.ImageTime.Hour;
                        waitForTime.Minutes = ListTarget.ImageTime.Minute;
                        waitForTime.Seconds = ListTarget.ImageTime.Second;
                    }
                    if (x is SwitchFilter switchFilter && ListTarget.Filter != null && ListTarget.Filter != "-" && ListTarget.Filter.Length > 0) {
                        switchFilter.Filter = Filters?.FirstOrDefault(f => f.Name == ListTarget.Filter);
                    }
                    if (x is MoveRotatorMechanical rotatorMechanical) {
                        rotatorMechanical.MechanicalPosition = (float)ListTarget.Rotation;
                    }
                });

                _ = _dispatcher.BeginInvoke(DispatcherPriority.Send, new Action(() => {
                    lock (Items) {
                        this.InsertIntoSequenceBlocks(100, myTargetContainer);
                        Logger.Debug("Adding reference container: " + myTargetContainer);
                    }
                }));
            }*/
            RaiseAllPropertiesChanged();
            return true;
        }

        private ListTarget GetNextTarget() {
            Logger.Debug("Getting next target from list: " + ListTargets.Count + " targets total.");
            
            // First get the next target with an imageTime in the future
            DateTime maxImageTime = DateTime.Now.AddMinutes(-5);
            ListTarget = ListTargets.Where(t => t.ImageTarget)
                .Where(t => t.Nights > t.Completed_nights)
                .Where(t => t.Cycles > t.Completed_cycles)
                .Where(t => t.ImageTime > maxImageTime)
                .OrderBy(t => t.Completed_cycles)
                .ThenBy(t => t.ImageTime)
                .FirstOrDefault();
            if (ListTarget != null) {
                Logger.Debug("Getting next target " + ListTarget.Target);

                // When the target is more than 6 minutes away, check for earlier targets that can be used as a fill in.
                if (ListTarget.ImageTime > DateTime.Now.AddMinutes(6)) {
                    maxImageTime = ListTarget.ImageTime;
                    var fillinTarget = ListTargets.Where(t => t.ImageTarget)
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
                        ListTarget = fillinTarget;
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
                                ListTarget = new ListTarget();
                                ListTarget.User = "Galaxies";
                                ListTarget.Ra = galaxy.ra.ToString();
                                ListTarget.Dec = galaxy.dec.ToString();
                                ListTarget.Target = galaxy.main_id;
                                ListTarget.Magnitude = galaxy.v_mag;
                                ListTarget.Template = speckle.GalaxyTemplate;
                                ListTarget.GetRef = 0;
                                ListTarget.RegisterTarget = false;
                            }
                            Galaxies.Remove(galaxy);
                        }
                    }
                }
            } else {
                Logger.Debug("No next target. Looking for previous target.");
                var fillinTarget = ListTargets.Where(t => t.ImageTarget)
                    .Where(t => t.Nights > t.Completed_nights)
                    .Where(t => t.Cycles > t.Completed_cycles)
                    .Where(t => t.getCurrentAltTime(speckle.AltitudeMax, speckle.MDistance) != null 
                        && t.getCurrentAltTime(speckle.AltitudeMax, speckle.MDistance).alt > t.MinAltitude
                        && t.getCurrentAltTime(speckle.AltitudeMax, speckle.MDistance).distanceToMoon > speckle.MoonDistance)
                    .OrderBy(t => t.getCurrentAltTime(speckle.AltitudeMax, speckle.MDistance).alt)
                    .FirstOrDefault();
                if (fillinTarget != null) {
                    Logger.Debug("Getting fillin target " + fillinTarget.Target);
                    ListTarget = fillinTarget;
                }
            }

            return ListTarget;
        }

        private void RegisterStatusCurrentTarget() {
            if (CurrentListTarget == null) return;
            ListTarget = ListTargets.Where(x => x.TargetId == CurrentListTarget.TargetId).FirstOrDefault();
            if (ListTarget != null) {
                if (!ListTarget.RegisterTarget) return;
                ListTarget.Completed_cycles += 1;
                if (ListTarget.Completed_cycles == ListTarget.Cycles) {
                    ListTarget.Completed_nights += 1;
                }
            }
            /*            var nights = false;
                        foreach (ISequenceItem item in Items) {
                            var container = item as TargetListContainer;
                            if (container != null && !container.Target.TargetName.Contains("_ref_")) {
                                List<ISequenceItem> nonFinishedItems = container.Items.Where(x => x.Status != SequenceEntityStatus.FINISHED).Cast<ISequenceItem>().ToList();
                                if (nonFinishedItems.Count == 0) {
                                    ListTarget.Completed_cycles += 1;
                                } else {
                                    var exposures = nonFinishedItems.Where(x => x is TakeLiveExposures || x is TakeRoiExposures).FirstOrDefault();
                                    if (exposures != null) {
                                        ListTarget.Completed_cycles += 1; // TODO maybe add an error marker or something
                                    }
                                }
                                if (nights == false && ListTarget.Completed_cycles == ListTarget.Cycles) {
                                    ListTarget.Completed_nights += 1;
                                    nights = true;
                                }
                            }
                            if (container != null && container.Target.TargetName.Contains("_ref_")) {
                                List<ISequenceItem> nonFinishedItems = container.Items.Where(x => x.Status != SequenceEntityStatus.FINISHED).Cast<ISequenceItem>().ToList();
                                if (nonFinishedItems.Count == 0) {
                                    ListTarget.Completed_ref_cycles += 1;
                                } else {
                                    var exposures = nonFinishedItems.Where(x => x is TakeLiveExposures || x is TakeRoiExposures).FirstOrDefault();
                                    if (exposures == null) {
                                        ListTarget.Completed_ref_cycles += 1; // TODO maybe add an error marker or something
                                    }
                                }
                            }
                        }*/

            // Write current status to the target csv file
            string csvfile = Path.Combine(profileService.ActiveProfile.ImageFileSettings.FilePath, "TargetList-" + DateTime.Now.AddHours(-12).ToString("yyyy-MM-dd") + ".csv");
            using (var writer = new StreamWriter(csvfile))
            using (var csv = new CsvWriter(writer, CultureInfo.InvariantCulture)) {
                csv.Context.RegisterClassMap<ListTargetMap>();
                csv.WriteRecords(ListTargets);
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

        private void RetrieveReferenceStars(IProgress<ApplicationStatus> externalProgress, CancellationToken token) {
            if (ListTarget.GetRef > 0 && (ListTarget.ReferenceStarList == null || !ListTarget.ReferenceStarList.Any())) {
                double magnitude = ListTarget.Magnitude > 1 ? Math.Min(ListTarget.Magnitude - 1, 8d) : 8d;
                ListTarget.ReferenceStarList = SimUtils.FindSimbadSaoStars(externalProgress, token, ListTarget.Coordinates(), speckle.SearchRadius, magnitude, speckle.MaxReferenceMag).Result;
                ListTarget.ReferenceStar = ListTarget.ReferenceStarList.FirstOrDefault();
                if (ListTarget.ReferenceStar == null) {
                    Logger.Debug("Couldn't find reference SAO star for ListTarget within " + speckle.SearchRadius + " degrees and magnitudes: " + magnitude + " and " + speckle.MaxReferenceMag);
                }
                RaiseAllPropertiesChanged();
            }
        }

        private List<AltTime> GetAltList(Coordinates coords) {
            var start = NighttimeData.NauticalTwilightRiseAndSet.Set ?? DateTime.Now;
            start = start.AddHours(-1);
            var siderealTime = AstroUtil.GetLocalSiderealTime(start, profileService.ActiveProfile.AstrometrySettings.Longitude);
            var hourAngle = AstroUtil.GetHourAngle(siderealTime, coords.RA);
            var end = NighttimeData.NauticalTwilightRiseAndSet.Rise ?? DateTime.Now.AddHours(23);
            end = end.AddHours(1);
            var siderealEndTime = AstroUtil.GetLocalSiderealTime(end, profileService.ActiveProfile.AstrometrySettings.Longitude);
            var hourEndAngle = AstroUtil.GetHourAngle(siderealEndTime, coords.RA);
            if (hourEndAngle < hourAngle) {
                hourEndAngle += 24;
            }

            List<AltTime> altList = new List<AltTime>();
            for (double angle = hourAngle; angle < hourEndAngle; angle += 0.05) {
                var degAngle = AstroUtil.HoursToDegrees(angle);
                var altitude = AstroUtil.GetAltitude(degAngle, profileService.ActiveProfile.AstrometrySettings.Latitude, coords.Dec);
                var azimuth = AstroUtil.GetAzimuth(degAngle, altitude, profileService.ActiveProfile.AstrometrySettings.Latitude, coords.Dec);
                var horizonAltitude = 0d;
                if (profileService.ActiveProfile.AstrometrySettings.Horizon != null) {
                    horizonAltitude = profileService.ActiveProfile.AstrometrySettings.Horizon.GetAltitude(azimuth);
                }
                // Run the whole thing and get the top value
                if (altitude > horizonAltitude)
                    altList.Add(new AltTime(altitude, degAngle, start, AstroUtil.Airmass(altitude), CalculateSeparation(start, coords)));
                start = start.AddHours(0.05);
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
                            ListTarget ListTarget = new ListTarget();
                            ListTarget.Ra = record.RA0.Trim();
                            ListTarget.Dec = record.Decl0.Trim();
                            ListTarget.RA0 = record.RA0.Trim();
                            ListTarget.Decl0 = record.Decl0.Trim();
                            ListTarget.User = record.User.Trim() != "" ? record.User.Trim() : User.Trim() != "" ? User.Trim() : speckle.User;
                            ListTarget.Target = record.Target.Trim() != "" ? record.Target.Trim() : record.WDSName != null && record.WDSName.Trim() != "" ? record.WDSName.Trim() + "_" + record.DD.Trim() :
                                "Ra" + ListTarget.Coordinates().RAString.Replace(":", "_") + "_Dec" + ListTarget.Coordinates().DecString.Replace(" ", "_");
                            if (ListTarget.Coordinates().RADegrees == 0d && ListTarget.Coordinates().Dec == 0d) {
                                Logger.Debug("No coordinates found. Skipping target " + ListTarget.Target + " for user " + ListTarget.User);
                                continue;
                            }
                            if (record.Nights > 0 && record.Nights <= record.Completed_nights) {
                                Logger.Debug("Target already imaged enough nights. Skipping target " + ListTarget.Target + " for user " + ListTarget.User);
                                continue;
                            }
                            if (record.Gmag0 > 0 && (record.Gmag0 < speckle.MinMag || record.Gmag0 > speckle.MaxMag)) {
                                Logger.Debug("Magnitude not within limits. Skipping target " + ListTarget.Target + " for user " + ListTarget.User);
                                continue;
                            }
                            if (record.GaiaSep > 0 && (record.GaiaSep < speckle.MinSep || record.GaiaSep > speckle.MaxSep)) {
                                Logger.Debug("Seperation not within limits. Skipping target " + ListTarget.Target + " for user " + ListTarget.User);
                                continue;
                            }
                            ListTarget.Cycles = record.Cycles > 0 ? record.Cycles : Cycles;
                            ListTarget.Nights = record.Nights > 0 ? record.Nights : speckle.Nights;
                            ListTarget.Airmass = record.Airmass;
                            ListTarget.MinAltitude = record.MinAltitude == 0d ? speckle.AltitudeMin : record.MinAltitude;
                            ListTarget.AltList = GetAltList(ListTarget.Coordinates());
                            var imageTo = ListTarget.ImageTo(NighttimeData, speckle.AltitudeMax, speckle.MDistance, ListTarget.Airmass, speckle.MoonDistance);
                            if (imageTo != null && imageTo.alt > ListTarget.MinAltitude && imageTo.datetime >= NighttimeData.NauticalTwilightRiseAndSet.Set && imageTo.datetime <= NighttimeData.NauticalTwilightRiseAndSet.Rise) {
                                ListTarget.ImageTime = RoundUp(imageTo.datetime, TimeSpan.FromMinutes(5));
                                ListTarget.ImageTimeAlt = imageTo.alt;
                            } else {
                                Logger.Debug("Image time not within limits or too close to the moon. Skipping target " + ListTarget.Target + " for user " + ListTarget.User);
                                ListTarget.ImageTarget = false; // Can't image this target
                                ListTarget.Note = "Target cannot be imaged tonight.";
                            }
                            ListTarget.Template = record.Template != "" ? record.Template : Template != "" ? Template : speckle.DefaultTemplate;
                            ListTarget.Exposures = record.Exposures > 0 ? record.Exposures : Exposures;
                            ListTarget.ExposureTime = record.ExposureTime > 0 ? record.ExposureTime : ExposureTime;
                            ListTarget.Magnitude = record.Gmag0;
                            if (!(record.Gmag1.ToString() == "" || record.Gmag1 == 0)) { ListTarget.Magnitude2 = record.Gmag1; }
                            else if (!(record.Smag.ToString() == "" || record.Smag == 0)) { ListTarget.Magnitude2 = record.Smag; }
                            else { throw new SequenceEntityFailedException("Failed to get secondary magnitude for "+ListTarget.Target); }

                            ListTarget.NoCalculation = record.NoCalculation;
                            ListTarget.Separation = record.GaiaSep;
                            ListTarget.Completed_nights = record.Completed_nights;
                            ListTarget.Filter = record.Filter;
                            ListTarget.Priority = record.Priority;
                            ListTarget.Rotation = record.Rotation;
                            ListTarget.GetRef = record.GetRef;
                            ListTargets.Add(ListTarget);
                            RaisePropertyChanged("ListTargetCount");
                        }
                        Logger.Debug("Loaded " + ListTargets.Count + " ListTargets");

                        ListTargets = new AsyncObservableCollection<ListTarget>(
                            ListTargets.Where(i => i.ImageTime != null).GroupBy(i => i.ImageTime)
                            .SelectMany(g => g.OrderByDescending(n => n.Priority).ThenByDescending(i => i.ImageTimeAlt).ToList())
                            .OrderBy(i => i.ImageTime).ThenBy(i => i.ImageTimeAlt)); 
                    }
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
            var clone = new TargetListContainer(profileService, sequenceMediator, nighttimeCalculator, applicationStatusMediator, cameraMediator, framingAssistantVM, applicationMediator) {
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