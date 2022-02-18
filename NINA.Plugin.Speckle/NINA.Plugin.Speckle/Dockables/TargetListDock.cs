using CsvHelper;
using NINA.Astrometry;
using NINA.Core.Model;
using NINA.Core.Utility;
using NINA.Core.Utility.Notification;
using NINA.Equipment.Equipment;
using NINA.Equipment.Equipment.MyCamera;
using NINA.Equipment.Equipment.MyTelescope;
using NINA.Equipment.Interfaces;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Equipment.Interfaces.ViewModel;
using NINA.Plugin.Speckle.Model;
using NINA.Profile.Interfaces;
using NINA.Sequencer.Container;
using NINA.Sequencer.Interfaces.Mediator;
using NINA.WPF.Base.Interfaces.Mediator;
using NINA.WPF.Base.ViewModel;
using NINA.Plugin.Speckle.Sequencer.Container;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Input;
using NINA.Sequencer;
using NINA.Sequencer.Serialization;
using Newtonsoft.Json;
using System.Windows.Data;
using System.ComponentModel;
using Newtonsoft.Json.Linq;
using NINA.Plugin.Speckle.Sequencer.Utility;
using System.Reflection;
using NINA.Astrometry.Interfaces;
using NINA.WPF.Base.Interfaces.ViewModel;
using NINA.Plugin.Speckle.Sequencer.SequenceItem;
using NINA.Core.Enum;

namespace NINA.Plugin.Speckle.Dockables {
    [Export(typeof(IDockableVM))]
    public class TargetListDock : DockableVM {
        private ITelescopeMediator telescopeMediator;
        private IGuiderMediator guiderMediator;
        private IApplicationStatusMediator applicationStatusMediator;
        private ISequenceMediator sequenceMediator;
        private ICameraMediator cameraMediator;
        private INighttimeCalculator nighttimeCalculator;
        private IFramingAssistantVM framingAssistantVM;
        private IApplicationMediator applicationMediator;
        private IPlanetariumFactory planetariumFactory;

        private CancellationTokenSource executeCTS;

        private Speckle speckle;

        [ImportingConstructor]
        public TargetListDock(IProfileService profileService, 
            IApplicationStatusMediator applicationStatusMediator,
            ISequenceMediator sequenceMediator,
            ITelescopeMediator telescopeMediator, 
            IGuiderMediator guiderMediator,
            ICameraMediator cameraMediator,
            INighttimeCalculator nighttimeCalculator,
            IFramingAssistantVM framingAssistantVM,
            IApplicationMediator applicationMediator,
            IPlanetariumFactory planetariumFactory) : base(profileService) {
            Title = "Speckle targetlist";
            ImageGeometry = (System.Windows.Media.GeometryGroup)System.Windows.Application.Current.Resources["GridSVG"];
            this.profileService = profileService;
            this.applicationStatusMediator = applicationStatusMediator;
            this.sequenceMediator = sequenceMediator;
            this.telescopeMediator = telescopeMediator;
            this.guiderMediator = guiderMediator;
            this.cameraMediator = cameraMediator;
            this.nighttimeCalculator = nighttimeCalculator;
            this.framingAssistantVM = framingAssistantVM;
            this.applicationMediator = applicationMediator;
            this.planetariumFactory = planetariumFactory;

            speckle = new Speckle();

            SpeckleTargets = new AsyncObservableCollection<SpeckleTarget>();
            OpenFileCommand = new GalaSoft.MvvmLight.Command.RelayCommand<bool>(async (o) => { using (executeCTS = new CancellationTokenSource()) { OpenFile(new Progress<ApplicationStatus>(p => Status = p), executeCTS.Token); } });
            AddTargetsCommand = new GalaSoft.MvvmLight.Command.RelayCommand<bool>(async (o) => { using (executeCTS = new CancellationTokenSource()) { await AddTargetStars(new Progress<ApplicationStatus>(p => Status = p), executeCTS.Token); } });
            SlewToCommand = new GalaSoft.MvvmLight.Command.RelayCommand<bool>(async (o) => { using (executeCTS = new CancellationTokenSource()) { await SlewToTarget(new Progress<ApplicationStatus>(p => Status = p), executeCTS.Token); } });
            SlewToClusterCommand = new GalaSoft.MvvmLight.Command.RelayCommand<bool>(async (o) => { using (executeCTS = new CancellationTokenSource()) { await SlewToStarCluster(new Progress<ApplicationStatus>(p => Status = p), executeCTS.Token); } });
            SlewToReferenceStarCommand = new GalaSoft.MvvmLight.Command.RelayCommand<bool>(async (o) => { using (executeCTS = new CancellationTokenSource()) { await SlewToReferenceStar(new Progress<ApplicationStatus>(p => Status = p), executeCTS.Token); } });
            AddTargetSequenceCommand = new GalaSoft.MvvmLight.Command.RelayCommand<bool>(async (o) => { using (executeCTS = new CancellationTokenSource()) { await AddTargetSequence(new Progress<ApplicationStatus>(p => Status = p), executeCTS.Token); } });
            StartSequencesCommand = new GalaSoft.MvvmLight.Command.RelayCommand<bool>(async (o) => { using (executeCTS = new CancellationTokenSource()) { await StartSequences(new Progress<ApplicationStatus>(p => Status = p), executeCTS.Token); } });
            CancelExecuteCommand = new GalaSoft.MvvmLight.Command.RelayCommand<bool>(async (o) => { try { executeCTS?.Cancel(); } catch (Exception) { } });

        }

        public ICommand CancelExecuteCommand { get; }
        public ICommand OpenFileCommand { get; private set; }
        public ICommand AddTargetsCommand { get; private set; }
        public ICommand SlewToCommand { get; private set; }
        public ICommand SlewToClusterCommand { get; private set; }
        public ICommand SlewToReferenceStarCommand { get; private set; }
        public ICommand SynchMountCommand { get; private set; }
        public ICommand AddTargetSequenceCommand { get; private set; }
        public ICommand StartSequencesCommand { get; private set; }

        public override bool IsTool => true;
        public void Teardown() {
            executeCTS?.Cancel();
        }

        private SpeckleTarget _speckleTarget;

        public SpeckleTarget SpeckleTarget {
            get => _speckleTarget;
            set {
                _speckleTarget = value;
                RaisePropertyChanged();
            }
        }

        private AsyncObservableCollection<SpeckleTarget> _speckleTargets;

        public AsyncObservableCollection<SpeckleTarget> SpeckleTargets {
            get => _speckleTargets;
            set {
                _speckleTargets = value;
                RaisePropertyChanged();
            }
        }

        private class Dp {
            public DateTime datetime { get; set; }
            public double alt { get; set; }

            public Dp(Double alt, DateTime datetime) {
                this.alt = alt;
                this.datetime = datetime;
            }
        }

        private DateTime GetMeridianTime(Coordinates coords) {
            var start = new DateTime();
            var siderealTime = AstroUtil.GetLocalSiderealTime(start, profileService.ActiveProfile.AstrometrySettings.Longitude);
            var hourAngle = AstroUtil.GetHourAngle(siderealTime, coords.RA);

            List<Dp> altList = new List<Dp>();
            for (double angle = hourAngle; angle < hourAngle + 24; angle += 0.1) {
                var degAngle = AstroUtil.HoursToDegrees(angle);
                var altitude = AstroUtil.GetAltitude(degAngle, profileService.ActiveProfile.AstrometrySettings.Latitude, coords.Dec);
                //var azimuth = AstroUtil.GetAzimuth(degAngle, altitude, profileService.ActiveProfile.AstrometrySettings.Latitude, coords.Dec);
                // Run the whole thing and get the top value
                altList.Add(new Dp(altitude, start));
                start = start.AddHours(0.1);
            }
            return altList.OrderByDescending((x) => x.alt).FirstOrDefault().datetime;
        }

        private void OpenFile(IProgress<ApplicationStatus> externalProgress, CancellationToken token) {
            OpenFileDialog fileDialog = new OpenFileDialog();
            fileDialog.DefaultExt = ".csv"; // Required file extension 
            fileDialog.Filter = "Csv documents (.csv)|*.csv"; // Optional file extensions

            if (fileDialog.ShowDialog() == DialogResult.OK) {
                _ = LoadTargetsAsync(externalProgress, token, fileDialog.FileName);
            }
        }

        private async Task LoadTargetsAsync(IProgress<ApplicationStatus> externalProgress, CancellationToken token, string file) {
            _ = await RetrieveTemplates(externalProgress, token);
            SpeckleTargets = new AsyncObservableCollection<SpeckleTarget>();
            using (var reader = new StreamReader(file))
            using (var csv = new CsvReader(reader, CultureInfo.InvariantCulture)) {
                // Do any configuration to `CsvReader` before creating CsvDataReader.
                using (var dr = new CsvDataReader(csv)) {
                    csv.Context.RegisterClassMap<SpeckleTargetMap>();
                    var records = csv.GetRecords<SpeckleTarget>();
                    foreach (SpeckleTarget record in records.ToList()) {
                        record.Meridian = GetMeridianTime(record.Coordinates());
                        if (record.Template != "") {
                            record.SpeckleTemplate = SpeckleTemplates.FirstOrDefault(x => x.Name == SpeckleTarget.Template);
                        } else {
                            record.SpeckleTemplate = SpeckleTemplates.FirstOrDefault(x => x.Name == speckle.DefaultTemplate);
                        }
                        SpeckleTargets.Add(record);
                    }
                    SpeckleTargets = new AsyncObservableCollection<SpeckleTarget>(SpeckleTargets.OrderBy(i => i.Meridian).ThenByDescending(n => n.Priority));
                }
            }
        }

        public async Task<bool> SlewToTarget(IProgress<ApplicationStatus> externalProgress, CancellationToken token) {
            if (!telescopeMediator.GetInfo().Connected) {
                Notification.ShowWarning("Telescope not connected!");
                return false;
            }
            try {
                using (var localCTS = CancellationTokenSource.CreateLinkedTokenSource(token)) {
                    var stoppedGuiding = await guiderMediator.StopGuiding(localCTS.Token);
                    await telescopeMediator.SlewToCoordinatesAsync(SpeckleTarget.Coordinates(), localCTS.Token);
                    if (stoppedGuiding) {
                        await guiderMediator.StartGuiding(false, externalProgress, localCTS.Token);
                    }
                }
            } catch (OperationCanceledException) {
            } catch (Exception ex) {
                Logger.Error(ex);
                Notification.ShowError(ex.Message);
            } finally {
                externalProgress?.Report(GetStatus(string.Empty));
            }
            return true;
        }

        public async Task<bool> SlewToStarCluster(IProgress<ApplicationStatus> externalProgress, CancellationToken token) {
            try {
                using (var localCTS = CancellationTokenSource.CreateLinkedTokenSource(token)) {
                    await RetrieveStarClusters(externalProgress, token);

                    if (SpeckleTarget.StarCluster != null && telescopeMediator.GetInfo().Connected) {
                        var stoppedGuiding = await guiderMediator.StopGuiding(localCTS.Token);
                        await telescopeMediator.SlewToCoordinatesAsync(SpeckleTarget.StarCluster.Coordinates(), localCTS.Token);
                        if (stoppedGuiding) {
                            await guiderMediator.StartGuiding(false, externalProgress, localCTS.Token);
                        }
                    }
                }
            } catch (OperationCanceledException) {
            } catch (Exception ex) {
                Logger.Error(ex);
                Notification.ShowError(ex.Message);
            } finally {
                externalProgress?.Report(GetStatus(string.Empty));
            }
            return true;
        }

        private async Task RetrieveStarClusters(IProgress<ApplicationStatus> externalProgress, CancellationToken token) {
            if (SpeckleTarget.StarClusterList == null || !SpeckleTarget.StarClusterList.Any()) {
                SimbadUtils simUtils = new SimbadUtils();
                SpeckleTarget.StarClusterList = await simUtils.FindSimbadStarClusters(externalProgress, token, SpeckleTarget.Coordinates(), speckle.MDistance);
                SpeckleTarget.StarCluster = SpeckleTarget.StarClusterList.FirstOrDefault();
                RaiseAllPropertiesChanged();
            }
        }

        public async Task<bool> SlewToReferenceStar(IProgress<ApplicationStatus> externalProgress, CancellationToken token) {
            try {
                using (var localCTS = CancellationTokenSource.CreateLinkedTokenSource(token)) {
                    await RetrieveReferenceStars(externalProgress, token);

                    if (SpeckleTarget.ReferenceStar != null && telescopeMediator.GetInfo().Connected) {
                        var stoppedGuiding = await guiderMediator.StopGuiding(localCTS.Token);
                        await telescopeMediator.SlewToCoordinatesAsync(SpeckleTarget.ReferenceStar.Coordinates(), localCTS.Token);
                        if (stoppedGuiding) {
                            await guiderMediator.StartGuiding(false, externalProgress, localCTS.Token);
                        }
                    }
                }
            } catch (OperationCanceledException) {
            } catch (Exception ex) {
                Logger.Error(ex);
                Notification.ShowError(ex.Message);
            } finally {
                externalProgress?.Report(GetStatus(string.Empty));
            }
            return true;
        }

        private async Task RetrieveReferenceStars(IProgress<ApplicationStatus> externalProgress, CancellationToken token) {
            if (SpeckleTarget.ReferenceStarList == null || !SpeckleTarget.ReferenceStarList.Any()) {
                SimbadUtils simUtils = new SimbadUtils();
                SpeckleTarget.ReferenceStarList = await simUtils.FindSimbadSaoStars(externalProgress, token, SpeckleTarget.Coordinates(), speckle.MDistance);
                SpeckleTarget.ReferenceStar = SpeckleTarget.ReferenceStarList.FirstOrDefault();
                RaiseAllPropertiesChanged();
            }
        }

        public async Task AddTargetStars(IProgress<ApplicationStatus> externalProgress, CancellationToken token) {
            if (!telescopeMediator.GetInfo().Connected) {
                Notification.ShowWarning("Telescope not connected!");
                return;
            }
            SimbadUtils simUtils = new SimbadUtils();
            List<SimbadBinaryStar> targets = await simUtils.FindSimbadBinaryStars(externalProgress, token, telescopeMediator.GetCurrentPosition(), speckle.MDistance);
            foreach (SimbadBinaryStar target in targets) {
                SpeckleTarget speckleTarget = new SpeckleTarget();
                speckleTarget.Target = target.main_id;
                speckleTarget.Ra = target.ra;
                speckleTarget.Dec = target.dec;
                speckleTarget.Cycles = speckle.Cycles;
                speckleTarget.Priority = speckle.Priority;
                if (speckle.DefaultTemplate != "") {
                    speckleTarget.Template = speckle.DefaultTemplate;
                    speckleTarget.SpeckleTemplate = SpeckleTemplates.FirstOrDefault(x => x.Name == speckle.DefaultTemplate);
                }
                speckleTarget.Meridian = GetMeridianTime(speckleTarget.Coordinates());
                SpeckleTargets.Add(speckleTarget);
            }
            RaiseAllPropertiesChanged();
        }

        private AsyncObservableCollection<SpeckleTargetContainer> _speckleTemplates = new AsyncObservableCollection<SpeckleTargetContainer>();

        public AsyncObservableCollection<SpeckleTargetContainer> SpeckleTemplates {
            get => _speckleTemplates;
            set {
                _speckleTemplates = value;
                RaisePropertyChanged();
            }
        }

        public async Task<bool> RetrieveTemplates(IProgress<ApplicationStatus> externalProgress, CancellationToken token) {
            try {
                using (var localCTS = CancellationTokenSource.CreateLinkedTokenSource(token)) {
                    SpeckleTemplates.Clear();
                    var templates = sequenceMediator.GetDeepSkyObjectContainerTemplates();
                    foreach (var template in templates) {
                        var speckleTemplate = template as SpeckleTargetContainer;
                        if (speckleTemplate != null)
                            SpeckleTemplates.Add(speckleTemplate);
                    }
                }
            } catch (OperationCanceledException) {
            } catch (Exception ex) {
                Logger.Error(ex);
                Notification.ShowError(ex.Message);
            } finally {
                externalProgress?.Report(GetStatus(string.Empty));
            }
            return true;
        }

        public async Task<bool> AddTargetSequence(IProgress<ApplicationStatus> externalProgress, CancellationToken token) {
            try {
                using (var localCTS = CancellationTokenSource.CreateLinkedTokenSource(token)) {
                    if (SpeckleTarget.SpeckleTemplate == null) {
                        Notification.ShowError("You must select a template.");
                        return false;
                    }

                    for (int i = 1; i <= SpeckleTarget.Cycles; i++) {
                        // Set target
                        SpeckleTargetContainer speckleTargetContainer = (SpeckleTargetContainer)SpeckleTarget.SpeckleTemplate.Clone();
                        speckleTargetContainer.Target = new InputTarget(Angle.ByDegree(profileService.ActiveProfile.AstrometrySettings.Latitude), Angle.ByDegree(profileService.ActiveProfile.AstrometrySettings.Longitude), profileService.ActiveProfile.AstrometrySettings.Horizon) {
                            TargetName = SpeckleTarget.Target + "_" + i,
                            InputCoordinates = new InputCoordinates() {
                                Coordinates = SpeckleTarget.Coordinates()
                            }
                        };
                        speckleTargetContainer.Title = SpeckleTarget.User;
                        speckleTargetContainer.Name = SpeckleTarget.User + "_" + SpeckleTarget.Target + "_" + i;
                        TakeRoiExposures takeRoiExposures = (TakeRoiExposures)speckleTargetContainer.Items.First(x => x.Name == "TakeRoiExposures");
                        takeRoiExposures.ExposureTime = SpeckleTarget.ExposureTime;
                        takeRoiExposures.TotalExposureCount = SpeckleTarget.Exposures;
                        speckleTargetContainer.IsExpanded = false;
                        sequenceMediator.AddAdvancedTarget(speckleTargetContainer);

                        // Set Reference
                        await RetrieveReferenceStars(externalProgress, token);
                        SpeckleTargetContainer speckleTargetContainerRef = (SpeckleTargetContainer)SpeckleTarget.SpeckleTemplate.Clone();
                        speckleTargetContainerRef.Target = new InputTarget(Angle.ByDegree(profileService.ActiveProfile.AstrometrySettings.Latitude), Angle.ByDegree(profileService.ActiveProfile.AstrometrySettings.Longitude), profileService.ActiveProfile.AstrometrySettings.Horizon) {
                            TargetName = SpeckleTarget.Target + "_" + i + "_ref_" + SpeckleTarget.ReferenceStar.main_id,
                            InputCoordinates = new InputCoordinates() {
                                Coordinates = SpeckleTarget.ReferenceStar.Coordinates()
                            }
                        };
                        speckleTargetContainerRef.Title = SpeckleTarget.User;
                        speckleTargetContainerRef.Name = SpeckleTarget.User + "_" + SpeckleTarget.Target + "_" + i + "_ref_" + SpeckleTarget.ReferenceStar.main_id;
                        TakeRoiExposures takeRoiExposuresRef = (TakeRoiExposures)speckleTargetContainerRef.Items.First(x => x.Name == "TakeRoiExposures");
                        takeRoiExposuresRef.ExposureTime = SpeckleTarget.ExposureTime;
                        takeRoiExposuresRef.TotalExposureCount = SpeckleTarget.Exposures;
                        speckleTargetContainerRef.IsExpanded = false;
                        sequenceMediator.AddAdvancedTarget(speckleTargetContainerRef);

                        Logger.Debug("Sequence added starting sequence.");
                    }
                }
            } catch (OperationCanceledException) {
            } catch (Exception ex) {
                Logger.Error(ex);
                Notification.ShowError(ex.Message);
            } finally {
                externalProgress?.Report(GetStatus(string.Empty));
            }
            return true;
        }

        public int TargetIndex { get; set; }

        // Run all sequences
        public async Task<bool> StartSequences(IProgress<ApplicationStatus> externalProgress, CancellationToken token) {
            
                try {
                    foreach (SpeckleTarget target in SpeckleTargets) {
                        SpeckleTarget = target;
                        _ = await AddTargetSequence(externalProgress, token);
                    }

                    /*                    while (!token.IsCancellationRequested) {
                                            IList<IDeepSkyObjectContainer> currentTargets = sequenceMediator.GetAllTargetsInAdvancedSequence();
                                            var finishedTarget = currentTargets.FirstOrDefault(t => t.Status == SequenceEntityStatus.FINISHED);
                                            if (finishedTarget != null) {
                                                var starget = (SpeckleTarget)SpeckleTargets.FirstOrDefault(t => finishedTarget.Name.StartsWith(t.User) && finishedTarget.Name.Contains(t.Target) && finishedTarget.Name.Contains("_ref"));
                                                if (starget != null)
                                                    starget.Cycles--;
                                                finishedTarget.Detach();
                                            }

                                            if (currentTargets.Count() < 4) {
                                                // Add next target
                                                SpeckleTarget = SpeckleTargets.ElementAt(TargetIndex);
                                                if (SpeckleTarget != null) {
                                                    _ = await AddTargetSequence(externalProgress, token);
                                                    TargetIndex++;
                                                } else {
                                                    break;
                                                }
                                            } else {
                                                Thread.Sleep(10 * 1000); // Wait 10 seconds before looping
                                            }
                                            if (currentTargets.Count() > 0 && !sequenceMediator.IsAdvancedSequenceRunning())
                                                sequenceMediator.StartAdvancedSequence();
                                            if (TargetIndex >= 2 && !sequenceMediator.IsAdvancedSequenceRunning())
                                                break;
                                        }*/
                } catch (OperationCanceledException) {
                } catch (Exception ex) {
                    Logger.Error(ex);
                    Notification.ShowError(ex.Message);
                } finally {
                    externalProgress?.Report(GetStatus(string.Empty));
                }
                return true;
        }

        private ApplicationStatus _status;

        public ApplicationStatus Status {
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
        
        private ApplicationStatus GetStatus(string status) {
            return new ApplicationStatus { Source = "Speckle", Status = status };
        }
    }
}
