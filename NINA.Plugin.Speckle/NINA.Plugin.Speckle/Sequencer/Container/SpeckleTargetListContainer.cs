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
            SpeckleTargets = new AsyncObservableCollection<SpeckleTarget>();
            GdsTargets = new AsyncObservableCollection<GdsTarget>();
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

        private AsyncObservableCollection<GdsTarget> _gdsTargets;

        [JsonProperty]
        public AsyncObservableCollection<GdsTarget> GdsTargets {
            get => _gdsTargets;
            set {
                _gdsTargets = value;
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
                    var speckleTemplate = template as SpeckleTargetContainer;
                    if (speckleTemplate != null)
                        SpeckleTemplates.Add(speckleTemplate);
                }
            }
        }

        public void LoadNewTarget() {
            RegisterStatusCurrentTarget();
            SpeckleTarget = GetNextTarget();
            if (SpeckleTarget == null)
                return;
            CurrentSpeckleTarget = SpeckleTarget;
            TargetNr++;

            foreach (ISequenceItem item in Items) {
                _dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() => { item.Detach(); item.AfterParentChanged(); }));
            }

            var templates = sequenceMediator.GetDeepSkyObjectContainerTemplates();

            // Set target
            var template = templates.FirstOrDefault(x => x.Name == (SpeckleTarget.Template != "" ? SpeckleTarget.Template : speckle.DefaultTemplate));
            if (template == null) {
                Notification.ShowError("No template found. Check the selected template: " + SpeckleTarget.Template);
                return;
            }
            SpeckleTargetContainer speckleTargetContainer = (SpeckleTargetContainer)template.Clone();
            speckleTargetContainer.Target = new InputTarget(Angle.ByDegree(profileService.ActiveProfile.AstrometrySettings.Latitude), Angle.ByDegree(profileService.ActiveProfile.AstrometrySettings.Longitude), profileService.ActiveProfile.AstrometrySettings.Horizon) {
                TargetName = SpeckleTarget.Target + "_c" + (SpeckleTarget.Completed_cycles + 1),
                InputCoordinates = new InputCoordinates() {
                    Coordinates = SpeckleTarget.Coordinates()
                }
            };
            speckleTargetContainer.Title = SpeckleTarget.User;
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
                if (x is CalculateRoiExposureTime calculateRoiExposureTime) {
                    calculateRoiExposureTime.ExposureTime = SpeckleTarget.ExposureTime;
                    calculateRoiExposureTime.ExposureTimeMax = SpeckleTarget.ExposureTime;
                }
                if (x is WaitForTime waitForTime) {
                    waitForTime.Hours = SpeckleTarget.ImageTime.Hour;
                    waitForTime.Minutes = SpeckleTarget.ImageTime.Minute;
                    waitForTime.Seconds = SpeckleTarget.ImageTime.Second;
                }
                if (x is SwitchFilter switchFilter && SpeckleTarget.Filter != null && SpeckleTarget.Filter.Length > 0) {
                    switchFilter.Filter = Filters?.FirstOrDefault(f => f.Name == SpeckleTarget.Filter);
                }
            });

            _dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() => { Items.Add(speckleTargetContainer); speckleTargetContainer.AttachNewParent(this); speckleTargetContainer.AfterParentChanged(); }));

            // Set Reference
            using (executeCTS = new CancellationTokenSource()) {
                // RetrieveReferenceStars(new Progress<ApplicationStatus>(p => AppStatus = p), executeCTS.Token);
            }
            if (SpeckleTarget.ReferenceStar != null && SpeckleTarget.ReferenceStar.main_id != "") {
                SpeckleTargetContainer speckleTargetContainerRef = (SpeckleTargetContainer)template.Clone();
                speckleTargetContainerRef.Target = new InputTarget(Angle.ByDegree(profileService.ActiveProfile.AstrometrySettings.Latitude), Angle.ByDegree(profileService.ActiveProfile.AstrometrySettings.Longitude), profileService.ActiveProfile.AstrometrySettings.Horizon) {
                    TargetName = SpeckleTarget.Target + "_c" + (SpeckleTarget.Completed_cycles + 1) + "_ref_" + SpeckleTarget.ReferenceStar.main_id,
                    InputCoordinates = new InputCoordinates() {
                        Coordinates = SpeckleTarget.ReferenceStar.Coordinates()
                    }
                };
                speckleTargetContainerRef.Title = SpeckleTarget.User;
                speckleTargetContainerRef.Name = SpeckleTarget.User + "_" + SpeckleTarget.Target + "_" + (SpeckleTarget.Completed_cycles + 1) + "_ref_" + SpeckleTarget.ReferenceStar.main_id;
                speckleTargetContainerRef.Items.ToList().ForEach(x => {
                    if (x is TakeRoiExposures takeRoiExposures) {
                        takeRoiExposures.ExposureTime = SpeckleTarget.ExposureTime;
                        takeRoiExposures.TotalExposureCount = speckle.ReferenceExposures;
                    }
                    if (x is TakeLiveExposures takeLiveExposures) {
                        takeLiveExposures.ExposureTime = SpeckleTarget.ExposureTime;
                        takeLiveExposures.TotalExposureCount = speckle.ReferenceExposures;
                    }
                    if (x is CalculateRoiExposureTime calculateRoiExposureTime) {
                        calculateRoiExposureTime.ExposureTime = SpeckleTarget.ExposureTime;
                        calculateRoiExposureTime.ExposureTimeMax = SpeckleTarget.ExposureTime;
                    }
                    if (x is WaitForTime waitForTime) {
                        waitForTime.Hours = SpeckleTarget.ImageTime.Hour;
                        waitForTime.Minutes = SpeckleTarget.ImageTime.Minute;
                        waitForTime.Seconds = SpeckleTarget.ImageTime.Second;
                    }
                    if (x is SwitchFilter switchFilter && SpeckleTarget.Filter != null && SpeckleTarget.Filter.Length > 0) {
                        switchFilter.Filter = Filters?.FirstOrDefault(f => f.Name == SpeckleTarget.Filter);
                    }
                });

                _dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() => { Items.Add(speckleTargetContainerRef); speckleTargetContainerRef.AttachNewParent(this); speckleTargetContainerRef.AfterParentChanged(); }));
            }
            Thread.Sleep(200);
            RaiseAllPropertiesChanged();

        }

        private SpeckleTarget GetNextTarget() {
            Logger.Debug("Getting next target from list: " + SpeckleTargets.Count + " targets total.");
            if (SpeckleTargets.Where(t => t.Completed_nights == 0).ToList().Count == 0) {
                Logger.Debug("Total targets: " + SpeckleTargets.Count + ", Targets completed: " + SpeckleTargets.Where(t => t.Completed_nights == 1).ToList().Count);
                return null;
            }

            /*            SpeckleTarget = SpeckleTargets.ElementAt(TargetNr);
                        Logger.Debug("Loaded new target: " + SpeckleTarget.Target);
                        TimeSpan span = DateTime.Now.Subtract(SpeckleTarget.ImageTime);
                        if (span.Minutes > speckle.CheckImageTimeWithinMinutes) {
                            // Reorder targets to see what's best again !
                            Logger.Debug("SpeckleTarget to far past imaging time. Recalculating imaging time for targets.");
                            foreach (SpeckleTarget speckleTarget in SpeckleTargets) {
                                AltTime imageTo = speckleTarget.ImageTo(NighttimeData, speckle.AltitudeMax, speckle.MDistance);
                                speckleTarget.ImageTime = imageTo.datetime;
                                speckleTarget.ImageTimeAlt = imageTo.alt;
                            }
                            SpeckleTargets = new AsyncObservableCollection<SpeckleTarget>(SpeckleTargets.Where(i => i.ImageTime != null && i.Completed_nights == 0).OrderBy(i => i.ImageTime).ThenByDescending(n => n.Priority).ThenBy(i => i.ImageTimeAlt));
                            if (SpeckleTargets.Count > 0) {
                                Logger.Debug("Targets left after reordering:" + SpeckleTargets.Count);
                                TargetNr = 0;
                                SpeckleTarget = SpeckleTargets.ElementAt(TargetNr);
                                Logger.Debug("Loaded new target: " + SpeckleTarget.Target);
                            } else {
                                SpeckleTarget = null;
                                Logger.Debug("No more speckle targets left.");
                            }
                        }*/
            
            // First get the next target with an imageTime in the future
            DateTime maxImageTime = DateTime.Now;
            SpeckleTarget = SpeckleTargets.Where(t => t.Completed_nights == 0)
                .Where(t => t.Cycles > t.Completed_cycles)
                .Where(t => t.ImageTime > maxImageTime)
                .OrderBy(t => t.Completed_cycles)
                .ThenBy(t => t.ImageTime)
                .FirstOrDefault();
            Logger.Debug("Getting next target " + SpeckleTarget.Target);

            // When the target is more than 6 minutes away, check for earlier targets that can be used as a fill in.
            if (SpeckleTarget.ImageTime > DateTime.Now.AddMinutes(6)) {
                maxImageTime = SpeckleTarget.ImageTime;
                var fillinTarget = SpeckleTargets.Where(t => t.Completed_nights == 0)
                    .Where(t => t.Cycles > t.Completed_cycles)
                    .Where(t => t.ImageTime < maxImageTime)
                    .Where(t => t.getCurrentAltTime(speckle.AltitudeMax, speckle.MDistance) != null && t.getCurrentAltTime(speckle.AltitudeMax, speckle.MDistance).alt > speckle.AltitudeMin)
                    .OrderBy(t => t.Completed_cycles)
                    .ThenByDescending(t => t.getCurrentAltTime(speckle.AltitudeMax, speckle.MDistance).alt)
                    .FirstOrDefault();
                if (fillinTarget != null) {
                    Logger.Debug("Getting fillin target " + fillinTarget.Target);
                    SpeckleTarget = fillinTarget;
                }
            }

            return SpeckleTarget;
        }

        private void RegisterStatusCurrentTarget() {
            if (CurrentSpeckleTarget == null) return;
            var nights = false;
            foreach (ISequenceItem item in Items) {
                var container = item as SpeckleTargetContainer;
                if (container != null && !container.Target.TargetName.Contains("_ref_")) {
                    List<ISequenceItem> nonFinishedItems = container.Items.Where(x => x.Status != SequenceEntityStatus.FINISHED).Cast<ISequenceItem>().ToList();
                    if (nonFinishedItems.Count == 0) {
                        CurrentSpeckleTarget.Completed_cycles += 1;
                    } else {
                        var exposures = nonFinishedItems.Where(x => x is TakeLiveExposures || x is TakeRoiExposures).FirstOrDefault();
                        if (exposures != null) {
                            CurrentSpeckleTarget.Completed_cycles += 1; // TODO maybe add an error marker or something
                        }
                    }
                    if (nights == false && CurrentSpeckleTarget.Completed_cycles == CurrentSpeckleTarget.Cycles) {
                        CurrentSpeckleTarget.Completed_nights += 1;
                        nights = true;
                    }
                }
                if (container != null && container.Target.TargetName.Contains("_ref_")) {
                    List<ISequenceItem> nonFinishedItems = container.Items.Where(x => x.Status != SequenceEntityStatus.FINISHED).Cast<ISequenceItem>().ToList();
                    if (nonFinishedItems.Count == 0) {
                        CurrentSpeckleTarget.Completed_ref_cycles += 1;
                    } else {
                        var exposures = nonFinishedItems.Where(x => x is TakeLiveExposures || x is TakeRoiExposures).FirstOrDefault();
                        if (exposures == null) {
                            CurrentSpeckleTarget.Completed_ref_cycles += 1; // TODO maybe add an error marker or something
                        }
                    }
                }
            }

            // Write data to json textfile. TODO Clean up this ugly code
            string path = Path.Combine(profileService.ActiveProfile.ImageFileSettings.FilePath, Guid.NewGuid().ToString());
            string uniquePath = CoreUtil.GetUniqueFilePath(path + ".json");
            File.WriteAllText(uniquePath, JsonConvert.SerializeObject(CurrentSpeckleTarget));
            ImageMetaData metaData = new ImageMetaData();
            metaData.Target.Name = CurrentSpeckleTarget.Target + "_c" + SpeckleTarget.Completed_cycles;
            metaData.Sequence.Title = CurrentSpeckleTarget.User;
            metaData.Image.ExposureStart = DateTime.Now;
            BaseImageData baseImageData = new BaseImageData(new ushort[0], 0, 0, 0, false, metaData, profileService, new StarDetection(), null);
            baseImageData.FinalizeSave(uniquePath, profileService.ActiveProfile.ImageFileSettings.FilePattern.Replace("$$FRAMENR$$", ""));
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
            if (SpeckleTarget.ReferenceStarList == null || !SpeckleTarget.ReferenceStarList.Any()) {
                SimbadUtils simUtils = new SimbadUtils();
                double magnitude = SpeckleTarget.Magnitude > 1 ? SpeckleTarget.Magnitude - 1 : 8d;
                SpeckleTarget.ReferenceStarList = simUtils.FindSimbadSaoStars(externalProgress, token, SpeckleTarget.Coordinates(), speckle.SearchRadius, magnitude, speckle.MaxReferenceMag).Result;
                SpeckleTarget.ReferenceStar = SpeckleTarget.ReferenceStarList.FirstOrDefault();
                if (SpeckleTarget.ReferenceStar == null) {
                    Logger.Debug("Couldn't find reference SAO star for SpeckleTarget within " + speckle.SearchRadius + " degrees and magnitudes: " + magnitude + " and " + speckle.MaxReferenceMag);
                }
                RaiseAllPropertiesChanged();
            }
        }

        private List<AltTime> GetAltList(Coordinates coords) {
            var start = NighttimeData.NauticalTwilightRiseAndSet.Set ?? DateTime.Now;
            var siderealTime = AstroUtil.GetLocalSiderealTime(start, profileService.ActiveProfile.AstrometrySettings.Longitude);
            var hourAngle = AstroUtil.GetHourAngle(siderealTime, coords.RA);
            var end = NighttimeData.NauticalTwilightRiseAndSet.Rise ?? DateTime.Now.AddHours(24);
            var siderealEndTime = AstroUtil.GetLocalSiderealTime(end, profileService.ActiveProfile.AstrometrySettings.Longitude);
            var hourEndAngle = AstroUtil.GetHourAngle(siderealEndTime, coords.RA);
            if (hourEndAngle < hourAngle) {
                hourEndAngle += 24;
            }

            List<AltTime> altList = new List<AltTime>();
            for (double angle = hourAngle; angle < hourEndAngle; angle += 0.05) {
                var degAngle = AstroUtil.HoursToDegrees(angle);
                var altitude = AstroUtil.GetAltitude(degAngle, profileService.ActiveProfile.AstrometrySettings.Latitude, coords.Dec);
                //var azimuth = AstroUtil.GetAzimuth(degAngle, altitude, profileService.ActiveProfile.AstrometrySettings.Latitude, coords.Dec);
                // Run the whole thing and get the top value
                altList.Add(new AltTime(altitude, degAngle, start));
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
                if (NighttimeData == null)
                    NighttimeData = nighttimeCalculator.Calculate();
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
                            if (record.Gmag0 > 0 && (record.Gmag0 < speckle.MinMag || record.Gmag0 > speckle.MaxMag)) {
                                Logger.Debug("Magnitude not within limits. Skipping target." + record.Number);
                                continue;
                            }
                            if (record.GaiaSep > 0 && (record.GaiaSep < speckle.MinSep || record.GaiaSep > speckle.MaxSep)) {
                                Logger.Debug("Seperation not within limits. Skipping target." + record.Number);
                                continue;
                            }
                            SpeckleTarget speckleTarget = new SpeckleTarget();
                            speckleTarget.Ra = record.RA0.Trim();
                            speckleTarget.Dec = record.Decl0.Trim();
                            speckleTarget.AltList = GetAltList(speckleTarget.Coordinates());
                            var imageTo = speckleTarget.ImageTo(NighttimeData, speckle.AltitudeMax, speckle.MDistance);
                            if (imageTo != null && imageTo.alt > speckle.AltitudeMin && imageTo.datetime >= NighttimeData.TwilightRiseAndSet.Set && imageTo.datetime <= NighttimeData.TwilightRiseAndSet.Rise) {
                                speckleTarget.ImageTime = RoundUp(imageTo.datetime, TimeSpan.FromMinutes(5));
                                speckleTarget.ImageTimeAlt = imageTo.alt;
                            } else {
                                Logger.Debug("Image time not within limits. Skipping target." + record.Number);
                                continue;
                            }
                            if (CalculateSeparation(speckleTarget.ImageTime, speckleTarget.Coordinates()) < speckle.MoonDistance) {
                                Logger.Debug("Target too close to moon. Skipping target." + record.Number);
                                continue;
                            }
                            speckleTarget.Template = Template != "" ? Template : speckle.DefaultTemplate;
                            speckleTarget.User = record.User.Trim() != "" ? record.User.Trim() : User.Trim() != "" ? User.Trim() : speckle.User;
                            speckleTarget.Target = record.Target.Trim() != "" ? record.Target.Trim() : record.WDSName != null && record.WDSName.Trim() != "" ? record.WDSName.Trim() + "_" + record.DD.Trim() :
                                "Ra" + speckleTarget.Coordinates().RAString.Replace(":", "_") + "_Dec" + speckleTarget.Coordinates().DecString.Replace(" ", "_");
                            speckleTarget.Exposures = record.Exposures > 0 ? record.Exposures : Exposures;
                            speckleTarget.ExposureTime = record.ExposureTime > 0 ? record.ExposureTime : ExposureTime;
                            speckleTarget.Magnitude = record.Gmag0;
                            speckleTarget.Separation = record.GaiaSep;
                            speckleTarget.Cycles = record.Cycles > 0 ? record.Cycles : Cycles;
                            speckleTarget.Filter = record.Filter;
                            speckleTarget.Priority = record.Priority;
                            SpeckleTargets.Add(speckleTarget);
                        }
                        Logger.Debug("Loaded " + SpeckleTargets.Count + " speckletargets");
                        /*                        SpeckleTargets = new AsyncObservableCollection<SpeckleTarget>(
                                                    SpeckleTargets.Where(i => i.ImageTime != null).GroupBy(i => i.ImageTime)
                                                    .SelectMany(g => g.OrderByDescending(n => n.Priority).ThenByDescending(i => i.ImageTimeAlt).Take(2).ToList())
                                                    .OrderBy(i => i.ImageTime).ThenBy(i => i.ImageTimeAlt) );
                        */
                        SpeckleTargets = new AsyncObservableCollection<SpeckleTarget>(
                            SpeckleTargets.Where(i => i.ImageTime != null).GroupBy(i => i.ImageTime)
                            .SelectMany(g => g.OrderByDescending(n => n.Priority).ThenByDescending(i => i.ImageTimeAlt).ToList())
                            .OrderBy(i => i.ImageTime).ThenBy(i => i.ImageTimeAlt));
                        Logger.Debug("Filtered some targets that had the same imaging time. Now " + SpeckleTargets.Count + " speckletargets");
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
            var clone = new SpeckleTargetListContainer(profileService, sequenceMediator, nighttimeCalculator, applicationStatusMediator, cameraMediator, framingAssistantVM, applicationMediator) {
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