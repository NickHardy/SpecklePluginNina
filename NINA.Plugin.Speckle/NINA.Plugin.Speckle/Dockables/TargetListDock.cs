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
using Speckle.Photometry.Sequencer.Container;
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

namespace NINA.Plugin.Speckle.Dockables {
    [Export(typeof(IDockableVM))]
    public class TargetListDock : DockableVM {
        private ITelescopeMediator telescopeMediator;
        private IGuiderMediator guiderMediator;
        private IApplicationStatusMediator applicationStatusMediator;
        private IFilterWheelMediator filterWheelMediator;
        private CameraInfo cameraInfo;

        private ICameraMediator cameraMediator;
        private IImagingMediator imagingMediator;

        private TelescopeInfo telescopeInfo;
        private IDomeMediator domeMediator;
        private IDomeFollower domeFollower;

        private CancellationTokenSource executeCTS;

        [ImportingConstructor]
        public TargetListDock(IProfileService profileService, 
            IApplicationStatusMediator applicationStatusMediator, 
            ITelescopeMediator telescopeMediator, 
            IGuiderMediator guiderMediator,
            ICameraMediator cameraMediator,
            ISequenceMediator sequenceMediator) : base(profileService) {
            Title = "Speckle targetlist";
            this.profileService = profileService;
            this.applicationStatusMediator = applicationStatusMediator;
            this.telescopeMediator = telescopeMediator;
            this.guiderMediator = guiderMediator;
            this.applicationStatusMediator = applicationStatusMediator;

            OpenFileCommand = new RelayCommand((object o) => OpenFile());
            SpeckleTargets = new AsyncObservableCollection<SpeckleTarget>();
            SlewToCommand = new AsyncCommand<bool>(async () => { using (executeCTS = new CancellationTokenSource()) { return await SlewToTarget(new Progress<ApplicationStatus>(p => Status = p), executeCTS.Token); } });
            CancelExecuteCommand = new RelayCommand((object o) => { try { executeCTS?.Cancel(); } catch (Exception) { } });

            GetDSOTemplatesCommand = new RelayCommand((object o) => {
                DSOTemplates = new List<IDeepSkyObjectContainer>();
                foreach (var container in sequenceMediator.GetDeepSkyObjectContainerTemplates()) {
                    var speckleContainer = container as SpeckleTargetContainer;
                    if (speckleContainer == null)
                        continue;
                    DSOTemplates.Add(speckleContainer);
                }
                RaisePropertyChanged(nameof(DSOTemplates));
            }, (object o) => sequenceMediator.Initialized);

            SetSequencerTargetCommand = new RelayCommand((object o) => {
                // applicationMediator.ChangeTab(ApplicationTab.SEQUENCE);

                var template = o as IDeepSkyObjectContainer;
                foreach (var container in GetDSOContainerListFromFraming(template)) {
                    Logger.Info($"Adding target to advanced sequencer: {container.Target.DeepSkyObject.Name} - {container.Target.DeepSkyObject.Coordinates}");
                    sequenceMediator.AddAdvancedTarget(container);
                }
            }, (object o) => sequenceMediator.Initialized);

        }

        public ICommand CancelExecuteCommand { get; }
        public ICommand OpenFileCommand { get; private set; }
        public ICommand SlewToCommand { get; private set; }
        public ICommand TakeImageCommand { get; private set; }
        public ICommand PlateSolveCommand { get; private set; }
        public ICommand SlewToClusterCommand { get; private set; }
        public ICommand SynchMountCommand { get; private set; }
        public ICommand GetDSOTemplatesCommand { get; private set; }
        public ICommand SetSequencerTargetCommand { get; private set; }

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
                    SpeckleTargets = new AsyncObservableCollection<SpeckleTarget>(SpeckleTargets.OrderByDescending(i => i.Meridian).Reverse());
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

        public IList<IDeepSkyObjectContainer> DSOTemplates { get; private set; }

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
