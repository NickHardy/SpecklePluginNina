using NINA.Plugin.Speckle.Properties;
using NINA.Core.Utility;
using NINA.Plugin;
using NINA.Plugin.Interfaces;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using NINA.Core.Model;
using NINA.WPF.Base.Interfaces.ViewModel;
using NINA.WPF.Base.Interfaces.Mediator;
using NINA.Image.ImageData;
using NINA.Plugin.Speckle.Model;
using Newtonsoft.Json;
using NINA.Profile.Interfaces;
using CsvHelper.Configuration;
using CsvHelper;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Windows.Input;
using System.Windows.Forms;

namespace NINA.Plugin.Speckle {
    /// <summary>
    /// This class exports the IPluginManifest interface and will be used for the general plugin information and options
    /// The base class "PluginBase" will populate all the necessary Manifest Meta Data out of the AssemblyInfo attributes. Please fill these accoringly
    /// 
    /// An instance of this class will be created and set as datacontext on the plugin options tab in N.I.N.A. to be able to configure global plugin settings
    /// The user interface for the settings will be defined by a DataTemplate with the key having the naming convention "<Speckle.Name>_Options" where Speckle.Name corresponds to the AssemblyTitle - In this template example it is found in the Options.xaml
    /// </summary>
    [Export(typeof(IPluginManifest))]
    public class Speckle : PluginBase {

        private readonly IProfileService _profileService;
        public ImagePattern notePattern = new ImagePattern("$$NOTE$$", "Possible note about target", "Speckle");
        private CancellationTokenSource executeCTS;

        [ImportingConstructor]
        public Speckle(IProfileService profileService, IOptionsVM options, IImageSaveMediator imageSaveMediator) {
            if (Settings.Default.UpdateSettings) {
                Settings.Default.Upgrade();
                Settings.Default.UpdateSettings = false;
                CoreUtil.SaveSettings(Settings.Default);
            }
            _profileService = profileService;
            notePattern.Value = "";
            options.AddImagePattern(notePattern);

            OpenFileCommand = new GalaSoft.MvvmLight.Command.RelayCommand<bool>((o) => { using (executeCTS = new CancellationTokenSource()) { OpenFile(); } });

            imageSaveMediator.BeforeFinalizeImageSaved += ImageSaveMediator_BeforeFinalizeImageSaved;
            if (string.IsNullOrWhiteSpace(Settings.Default.Telescope)) {
                Telescope = new Telescope("PW1000", 1000, 470, 6000);
            } else {
                Telescope = JsonConvert.DeserializeObject<Telescope>(Settings.Default.Telescope);
            }
            if (string.IsNullOrWhiteSpace(Settings.Default.Barlow)) {
                Barlow = new Barlow("2x Barlow", 2);
            } else {
                Barlow = JsonConvert.DeserializeObject<Barlow>(Settings.Default.Barlow);
            }
            if (string.IsNullOrWhiteSpace(FilterTransmission)) {
                FilterTransmissionList = new AsyncObservableCollection<Filter>();
                var filters = _profileService.ActiveProfile.FilterWheelSettings.FilterWheelFilters;
                foreach (var filter in filters) {
                    FilterTransmissionList.Add(new Filter(filter.Name, new double[] { 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1 }));
                }
            }

            if (!string.IsNullOrWhiteSpace(ReferenceStarListLocation))
                _ = LoadReferenceStarList();
        }

        public Speckle() {
            if (Settings.Default.UpdateSettings) {
                Settings.Default.Upgrade();
                Settings.Default.UpdateSettings = false;
                CoreUtil.SaveSettings(Settings.Default);
            }
            if (string.IsNullOrWhiteSpace(Settings.Default.Telescope)) {
                Telescope = new Telescope("PW1000", 1000, 470, 6000);
            }
            else {
                Telescope = JsonConvert.DeserializeObject<Telescope>(Settings.Default.Telescope);
            }
            if (string.IsNullOrWhiteSpace(Settings.Default.Barlow)) {
                Barlow = new Barlow("2x Barlow", 2);
            }
            else {
                Barlow = JsonConvert.DeserializeObject<Barlow>(Settings.Default.Barlow);
            }
        }

        public ICommand OpenFileCommand { get; private set; }

        public string ReferenceStarListLocation {
            get => Settings.Default.ReferenceStarListLocation;
            set {
                Settings.Default.ReferenceStarListLocation = value;
                CoreUtil.SaveSettings(Properties.Settings.Default);
                RaisePropertyChanged();
            }
        }
        private void OpenFile() {
            OpenFileDialog fileDialog = new OpenFileDialog();
            fileDialog.DefaultExt = ".csv"; // Required file extension 
            fileDialog.Filter = "Csv documents (.csv)|*.csv"; // Optional file extensions

            if (fileDialog.ShowDialog() == DialogResult.OK) {
                ReferenceStarListLocation = fileDialog.FileName;
                _ = LoadReferenceStarList();
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

        private bool _LoadingReferenceStars = false;

        public bool LoadingReferenceStars {
            get { return _LoadingReferenceStars; }
            set {
                _LoadingReferenceStars = value;
                RaisePropertyChanged();
            }
        }

        public async Task LoadReferenceStarList() {
            if (string.IsNullOrWhiteSpace(ReferenceStarListLocation) || LoadingReferenceStars) {
                Logger.Debug("No path to reference star list.");
                return;
            }
            LoadingReferenceStars = true;
            var config = new CsvConfiguration(CultureInfo.InvariantCulture);
            config.MissingFieldFound = null;
            using (var reader = new StreamReader(ReferenceStarListLocation))
            using (var csv = new CsvReader(reader, config)) {
                // Do any configuration to `CsvReader` before creating CsvDataReader.
                using (var dr = new CsvDataReader(csv)) {
                    csv.Context.RegisterClassMap<StarMap>();
                    var records = csv.GetRecords<ReferenceStar>();
                    ReferenceStarList = new AsyncObservableCollection<ReferenceStar>(records.ToList());
                }
            }
            LoadingReferenceStars = false;
        }

        public double MDistance {
            get => Settings.Default.MDistance;
            set {
                Settings.Default.MDistance = value;
                CoreUtil.SaveSettings(Properties.Settings.Default);
                RaisePropertyChanged();
            }
        }

        public double MoonDistance {
            get => Settings.Default.MoonDistance;
            set {
                Settings.Default.MoonDistance = value;
                CoreUtil.SaveSettings(Properties.Settings.Default);
                RaisePropertyChanged();
            }
        }

        public double SearchRadius {
            get => Settings.Default.SearchRadius;
            set {
                Settings.Default.SearchRadius = value;
                CoreUtil.SaveSettings(Properties.Settings.Default);
                RaisePropertyChanged();
            }
        }

        public double AltitudeMin {
            get => Settings.Default.AltitudeMin;
            set {
                Settings.Default.AltitudeMin = value;
                CoreUtil.SaveSettings(Properties.Settings.Default);
                RaisePropertyChanged();
            }
        }

        public double AltitudeMax {
            get => Settings.Default.AltitudeMax;
            set {
                Settings.Default.AltitudeMax = value;
                CoreUtil.SaveSettings(Properties.Settings.Default);
                RaisePropertyChanged();
            }
        }

        public int Nights {
            get => Settings.Default.Nights;
            set {
                Settings.Default.Nights = value;
                CoreUtil.SaveSettings(Properties.Settings.Default);
                RaisePropertyChanged();
            }
        }

        public int Cycles {
            get => Settings.Default.Cycles;
            set {
                Settings.Default.Cycles = value;
                CoreUtil.SaveSettings(Properties.Settings.Default);
                RaisePropertyChanged();
            }
        }

        public int Priority {
            get => Settings.Default.Priority;
            set {
                Settings.Default.Priority = value;
                CoreUtil.SaveSettings(Properties.Settings.Default);
                RaisePropertyChanged();
            }
        }

        public double MinMag {
            get => Settings.Default.MinMag;
            set {
                Settings.Default.MinMag = value;
                CoreUtil.SaveSettings(Properties.Settings.Default);
                RaisePropertyChanged();
            }
        }

        public double MaxMag {
            get => Settings.Default.MaxMag;
            set {
                Settings.Default.MaxMag = value;
                CoreUtil.SaveSettings(Properties.Settings.Default);
                RaisePropertyChanged();
            }
        }

        public double MinSep {
            get => Settings.Default.MinSep;
            set {
                Settings.Default.MinSep = value;
                CoreUtil.SaveSettings(Properties.Settings.Default);
                RaisePropertyChanged();
            }
        }

        public double MaxSep {
            get => Settings.Default.MaxSep;
            set {
                Settings.Default.MaxSep = value;
                CoreUtil.SaveSettings(Properties.Settings.Default);
                RaisePropertyChanged();
            }
        }

        public bool DomePositionLock {
            get => Settings.Default.DomePositionLock;
            set {
                Settings.Default.DomePositionLock = value;
                CoreUtil.SaveSettings(Properties.Settings.Default);
                RaisePropertyChanged();
            }
        }

        public double DomePosition {
            get => Settings.Default.DomePosition;
            set {
                Settings.Default.DomePosition = value;
                CoreUtil.SaveSettings(Properties.Settings.Default);
                RaisePropertyChanged();
            }
        }

        public double DomeSlitWidth {
            get => Settings.Default.DomeSlitWidth;
            set {
                Settings.Default.DomeSlitWidth = value;
                CoreUtil.SaveSettings(Properties.Settings.Default);
                RaisePropertyChanged();
            }
        }

        public string DefaultTemplate {
            get => Settings.Default.DefaultTemplate;
            set {
                Settings.Default.DefaultTemplate = value;
                CoreUtil.SaveSettings(Properties.Settings.Default);
                RaisePropertyChanged();
            }
        }

        public string DefaultRefTemplate {
            get => Settings.Default.DefaultRefTemplate;
            set {
                Settings.Default.DefaultRefTemplate = value;
                CoreUtil.SaveSettings(Properties.Settings.Default);
                RaisePropertyChanged();
            }
        }

        public int Exposures {
            get => Settings.Default.Exposures;
            set {
                Settings.Default.Exposures = value;
                CoreUtil.SaveSettings(Properties.Settings.Default);
                RaisePropertyChanged();
            }
        }

        public double ExposureTime {
            get => Settings.Default.ExposureTime;
            set {
                Settings.Default.ExposureTime = value;
                CoreUtil.SaveSettings(Properties.Settings.Default);
                RaisePropertyChanged();
            }
        }

        public string User {
            get => Settings.Default.User;
            set {
                Settings.Default.User = value;
                CoreUtil.SaveSettings(Properties.Settings.Default);
                RaisePropertyChanged();
            }
        }

        public int ShowEveryNthImage {
            get => Settings.Default.ShowEveryNthImage;
            set {
                Settings.Default.ShowEveryNthImage = value;
                CoreUtil.SaveSettings(Properties.Settings.Default);
                RaisePropertyChanged();
            }
        }

        public int CheckImageTimeWithinMinutes {
            get => Settings.Default.CheckImageTimeWithinMinutes;
            set {
                Settings.Default.CheckImageTimeWithinMinutes = value;
                CoreUtil.SaveSettings(Properties.Settings.Default);
                RaisePropertyChanged();
            }
        }

        public double MaxReferenceMag {
            get => Settings.Default.MaxReferenceMag;
            set {
                Settings.Default.MaxReferenceMag = value;
                CoreUtil.SaveSettings(Properties.Settings.Default);
                RaisePropertyChanged();
            }
        }

        public double MinReferenceMag {
            get => Settings.Default.MinReferenceMag;
            set {
                Settings.Default.MinReferenceMag = value;
                CoreUtil.SaveSettings(Properties.Settings.Default);
                RaisePropertyChanged();
            }
        }

        public int ReferenceExposures {
            get => Settings.Default.ReferenceExposures;
            set {
                Settings.Default.ReferenceExposures = value;
                CoreUtil.SaveSettings(Properties.Settings.Default);
                RaisePropertyChanged();
            }
        }

        public bool UseReferenceStarList {
            get => Settings.Default.UseReferenceStarList;
            set {
                Settings.Default.UseReferenceStarList = value;
                CoreUtil.SaveSettings(Properties.Settings.Default);
                RaisePropertyChanged();
            }
        }

        public bool UseSimbadRefStars {
            get => Settings.Default.UseSimbadRefStars;
            set {
                Settings.Default.UseSimbadRefStars = value;
                CoreUtil.SaveSettings(Properties.Settings.Default);
                RaisePropertyChanged();
            }
        }

        public bool UseUSNOSingleStarList {
            get => Settings.Default.UseUSNOSingleStarList;
            set {
                Settings.Default.UseUSNOSingleStarList = value;
                CoreUtil.SaveSettings(Properties.Settings.Default);
                RaisePropertyChanged();
            }
        }

        public bool GetGalaxyFillins {
            get => Settings.Default.GetGalaxyFillins;
            set {
                Settings.Default.GetGalaxyFillins = value;
                CoreUtil.SaveSettings(Properties.Settings.Default);
                RaisePropertyChanged();
            }
        }
        public double MaxGalaxyMag {
            get => Settings.Default.MaxGalaxyMag;
            set {
                Settings.Default.MaxGalaxyMag = value;
                CoreUtil.SaveSettings(Properties.Settings.Default);
                RaisePropertyChanged();
            }
        }
        public string GalaxyTemplate {
            get => Settings.Default.GalaxyTemplate;
            set {
                Settings.Default.GalaxyTemplate = value;
                CoreUtil.SaveSettings(Properties.Settings.Default);
                RaisePropertyChanged();
            }
        }

        private Telescope _telescope;
        public Telescope Telescope {
            get => _telescope;
            set {
                _telescope = value;
                RaisePropertyChanged(); 
            }
        }

        private Barlow _barlow;
        public Barlow Barlow {
            get => _barlow;
            set {
                _barlow = value;
                RaisePropertyChanged(); 
            }
        }

        public string FilterTransmission {
            get => Settings.Default.FilterTransmission;
            set {
                Settings.Default.FilterTransmission = value;
                CoreUtil.SaveSettings(Properties.Settings.Default);
                RaisePropertyChanged();
            }
        }

        private AsyncObservableCollection<Filter> _filterTransmissionList;
        public AsyncObservableCollection<Filter> FilterTransmissionList {
            get {
                if (_filterTransmissionList == null)
                    _filterTransmissionList = JsonConvert.DeserializeObject<AsyncObservableCollection<Filter>>(FilterTransmission);
                return _filterTransmissionList;
            }
            set {
                _filterTransmissionList = value;
                RaisePropertyChanged();
                FilterTransmission = JsonConvert.SerializeObject(_filterTransmissionList);
            }
        }

        private Task ImageSaveMediator_BeforeFinalizeImageSaved(object sender, BeforeFinalizeImageSavedEventArgs e) {
            var headers = e.Image.RawImageData.MetaData.GenericHeaders;
            var noteHeader = (StringMetaDataHeader) headers.Where(h => h.Key == "NOTE").FirstOrDefault();
            e.AddImagePattern(new ImagePattern(notePattern.Key, notePattern.Description, notePattern.Category) {
                Value = noteHeader?.Value ?? string.Empty
            });
            return Task.CompletedTask;
        }

        public static string GetVersion() {
            System.Reflection.Assembly assembly = System.Reflection.Assembly.GetExecutingAssembly();
            System.Diagnostics.FileVersionInfo fvi = System.Diagnostics.FileVersionInfo.GetVersionInfo(assembly.Location);
            return fvi.FileVersion;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected void RaisePropertyChanged([CallerMemberName] string propertyName = null) {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
