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

            LoadUserTemplates();

            OpenFileCommand = new RelayCommand((object o) => OpenFile());
            SpeckleTargets = new AsyncObservableCollection<SpeckleTarget>();
            SlewToCommand = new GalaSoft.MvvmLight.Command.RelayCommand<bool>(async (o) => { using (executeCTS = new CancellationTokenSource()) { await SlewToTarget(new Progress<ApplicationStatus>(p => Status = p), executeCTS.Token); } });
            SlewToClusterCommand = new GalaSoft.MvvmLight.Command.RelayCommand<bool>(async (o) => { using (executeCTS = new CancellationTokenSource()) { await SlewToStarCluster(new Progress<ApplicationStatus>(p => Status = p), executeCTS.Token); } });
            SlewToReferenceStarCommand = new GalaSoft.MvvmLight.Command.RelayCommand<bool>(async (o) => { using (executeCTS = new CancellationTokenSource()) { await SlewToReferenceStar(new Progress<ApplicationStatus>(p => Status = p), executeCTS.Token); } });
            StartTargetSequenceCommand = new GalaSoft.MvvmLight.Command.RelayCommand<bool>(async (o) => { using (executeCTS = new CancellationTokenSource()) { await StartTargetSequence(new Progress<ApplicationStatus>(p => Status = p), executeCTS.Token); } });
            CancelExecuteCommand = new GalaSoft.MvvmLight.Command.RelayCommand<bool>(async (o) => { try { executeCTS?.Cancel(); } catch (Exception) { } });

        }

        public ICommand CancelExecuteCommand { get; }
        public ICommand OpenFileCommand { get; private set; }
        public ICommand SlewToCommand { get; private set; }
        public ICommand SlewToClusterCommand { get; private set; }
        public ICommand SlewToReferenceStarCommand { get; private set; }
        public ICommand SynchMountCommand { get; private set; }
        public ICommand StartTargetSequenceCommand { get; private set; }

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

        public IList<IDeepSkyObjectContainer> DSOTemplates { get; private set; }

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

        private void OpenFile() {
            OpenFileDialog fileDialog = new OpenFileDialog();
            fileDialog.DefaultExt = ".csv"; // Required file extension 
            fileDialog.Filter = "Csv documents (.csv)|*.csv"; // Optional file extensions

            if (fileDialog.ShowDialog() == DialogResult.OK) {
                LoadTargets(fileDialog.FileName);
            }
        }

        private void LoadTargets(string file) {
            SpeckleTargets = new AsyncObservableCollection<SpeckleTarget>();
            using (var reader = new StreamReader(file))
            using (var csv = new CsvReader(reader, CultureInfo.InvariantCulture)) {
                // Do any configuration to `CsvReader` before creating CsvDataReader.
                using (var dr = new CsvDataReader(csv)) {
                    csv.Context.RegisterClassMap<SpeckleTargetMap>();
                    var records = csv.GetRecords<SpeckleTarget>();
                    foreach (SpeckleTarget record in records.ToList()) {
                        record.Meridian = GetMeridianTime(record.Coordinates());
                        SpeckleTargets.Add(record);
                    }
                    SpeckleTargets = new AsyncObservableCollection<SpeckleTarget>(SpeckleTargets.OrderBy(i => i.Meridian).ThenByDescending(n => n.Priority));
                }
            }
        }

        public async Task<bool> SlewToTarget(IProgress<ApplicationStatus> externalProgress, CancellationToken token) {
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

                    if (SpeckleTarget.StarCluster != null) {
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
                SpeckleTarget.StarClusterList = await simUtils.FindSimbadStarClusters(externalProgress, token, SpeckleTarget.Coordinates());
                SpeckleTarget.StarCluster = SpeckleTarget.StarClusterList.FirstOrDefault();
                RaiseAllPropertiesChanged();
            }
        }

        public async Task<bool> SlewToReferenceStar(IProgress<ApplicationStatus> externalProgress, CancellationToken token) {
            try {
                using (var localCTS = CancellationTokenSource.CreateLinkedTokenSource(token)) {
                    await RetrieveReferenceStars(externalProgress, token);

                    if (SpeckleTarget.ReferenceStar != null) {
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
                SpeckleTarget.ReferenceStarList = await simUtils.FindSimbadSaoStars(externalProgress, token, SpeckleTarget.Coordinates());
                SpeckleTarget.ReferenceStar = SpeckleTarget.ReferenceStarList.FirstOrDefault();
                RaiseAllPropertiesChanged();
            }
        }

        private AsyncObservableCollection<string> _templates = new AsyncObservableCollection<string>();

        public AsyncObservableCollection<string> Templates {
            get => _templates;
            set {
                _templates = value;
                RaisePropertyChanged();
            }
        }

        public async Task<bool> RetrieveTemplates(IProgress<ApplicationStatus> externalProgress, CancellationToken token) {
            try {
                using (var localCTS = CancellationTokenSource.CreateLinkedTokenSource(token)) {
                    // todo: use token
                    await LoadUserTemplates();
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

        private Task LoadUserTemplates() {
            return Task.Run(() => {
                try {
                    Templates = new AsyncObservableCollection<string>();
                    var userTemplatePath = profileService.ActiveProfile.SequenceSettings.SequencerTemplatesFolder;

                    if (!Directory.Exists(userTemplatePath)) {
                        Notification.ShowError("No template directory");
                    }

                    foreach (var file in Directory.GetFiles(userTemplatePath, "*" + TemplateController.TemplateFileExtension, SearchOption.AllDirectories)) {
                        try {
                            var container = JObject.Parse(File.ReadAllText(file));
                            if (!container.Value<string>("$type").Contains("SpeckleTargetContainer")) continue;
                            Templates.Add(container.Value<string>("Name"));
                        } catch (Exception ex) {
                            Logger.Error("Invalid template JSON", ex);
                        }
                    }
                } catch (Exception ex) {
                    Logger.Error(ex);
                    Notification.ShowError("Error loading templates");
                }
            });
        }

        public async Task<bool> StartTargetSequence(IProgress<ApplicationStatus> externalProgress, CancellationToken token) {
            try {
                using (var localCTS = CancellationTokenSource.CreateLinkedTokenSource(token)) {
                    if (SpeckleTarget.Template == null || SpeckleTarget.Template == "") {
                        Notification.ShowError("You must select a template.");
                        return true;
                    }
                    DSOTemplates = sequenceMediator.GetDeepSkyObjectContainerTemplates();
                    
                    for (int i = 1; i <= SpeckleTarget.Cycles; i++) {
                        // Set target
                        SpeckleTargetContainer template = (SpeckleTargetContainer) DSOTemplates.FirstOrDefault(x => x.Name == SpeckleTarget.Template);
                        SpeckleTargetContainer speckleTargetContainer = (SpeckleTargetContainer) template.Clone();
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
                        sequenceMediator.AddAdvancedTarget(speckleTargetContainer);

                        // Set Reference
                        await RetrieveReferenceStars(externalProgress, token);
                        SpeckleTargetContainer speckleTargetContainerRef = (SpeckleTargetContainer)template.Clone();
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
                        sequenceMediator.AddAdvancedTarget(speckleTargetContainerRef);
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
