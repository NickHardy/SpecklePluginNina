using Newtonsoft.Json;
using NINA.Core.Model;
using NINA.Core.Utility;
using NINA.Image.ImageData;
using NINA.Plugin.Interfaces;
using NINA.Plugin.Speckle.Model;
using NINA.Profile;
using NINA.Profile.Interfaces;
using NINA.WPF.Base.Interfaces.Mediator;
using NINA.WPF.Base.Interfaces.ViewModel;
using System;
using System.ComponentModel.Composition;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Settings = NINA.Plugin.Speckle.Properties.Settings;

namespace NINA.Plugin.Speckle {
    /// <summary>
    /// This class exports the IPluginManifest interface and will be used for the general plugin information and options
    /// The base class "PluginBase" will populate all the necessary Manifest Meta Data out of the AssemblyInfo attributes. Please fill these accoringly
    /// 
    /// An instance of this class will be created and set as datacontext on the plugin options tab in N.I.N.A. to be able to configure global plugin settings
    /// The user interface for the settings will be defined by a DataTemplate with the key having the naming convention "<Speckle.Name>_Options" where Speckle.Name corresponds to the AssemblyTitle - In this template example it is found in the Options.xaml
    /// </summary>
    [Export(typeof(IPluginManifest))]
    public class Speckle : PluginBase, INotifyPropertyChanged {

        private readonly IProfileService _profileService;
        private readonly PluginOptionsAccessor _pluginOptionsAccessor;

        public ImagePattern notePattern = new("$$NOTE$$", "Possible note about target", "Speckle");

        [ImportingConstructor]
        public Speckle(IProfileService profileService, IOptionsVM options, IImageSaveMediator imageSaveMediator) {
            _profileService = profileService;
            _profileService.ProfileChanged += ProfileService_ProfileChanged;

            Guid? guid = PluginOptionsAccessor.GetAssemblyGuid(typeof(Speckle)) ?? throw new Exception($"GUID was not found in assembly metadata");
            _pluginOptionsAccessor = new PluginOptionsAccessor(_profileService, guid.Value);

            if (!SpeckleSettingsMigrated) {
                Logger.Info($"Migrating app settings to NINA profile {_profileService.ActiveProfile.Name} ({_profileService.ActiveProfile.Id})");
                MigrateSettingsToProfile();
                SpeckleSettingsMigrated = true;
            }

            notePattern.Value = string.Empty;
            options.AddImagePattern(notePattern);

            imageSaveMediator.BeforeFinalizeImageSaved += ImageSaveMediator_BeforeFinalizeImageSaved;
        }

        public Speckle(IProfileService profileService) {
            _profileService = profileService;
            _profileService.ProfileChanged += ProfileService_ProfileChanged;

            Guid? guid = PluginOptionsAccessor.GetAssemblyGuid(typeof(Speckle)) ?? throw new Exception($"GUID was not found in assembly metadata");
            _pluginOptionsAccessor = new PluginOptionsAccessor(_profileService, guid.Value);
        }

        public override Task Teardown() {
            _profileService.ProfileChanged -= ProfileService_ProfileChanged;
            return base.Teardown();
        }

        public double MDistance {
            get => _pluginOptionsAccessor.GetValueDouble(nameof(MDistance), 5d);
            set {
                _pluginOptionsAccessor.SetValueDouble(nameof(MDistance), value);
                RaisePropertyChanged();
            }
        }

        public double MoonDistance {
            get => _pluginOptionsAccessor.GetValueDouble(nameof(MoonDistance), 20d);
            set {
                _pluginOptionsAccessor.SetValueDouble(nameof(MoonDistance), value);
                RaisePropertyChanged();
            }
        }

        public double SearchRadius {
            get => _pluginOptionsAccessor.GetValueDouble(nameof(SearchRadius), 5d);
            set {
                _pluginOptionsAccessor.SetValueDouble(nameof(SearchRadius), value);
                RaisePropertyChanged();
            }
        }

        public double AltitudeMin {
            get => _pluginOptionsAccessor.GetValueDouble(nameof(AltitudeMin), 40d);
            set {
                _pluginOptionsAccessor.SetValueDouble(nameof(AltitudeMin), value);
                RaisePropertyChanged();
            }
        }

        public double AltitudeMax {
            get => _pluginOptionsAccessor.GetValueDouble(nameof(AltitudeMax), 80d);
            set {
                _pluginOptionsAccessor.SetValueDouble(nameof(AltitudeMax), value);
                RaisePropertyChanged();
            }
        }

        public int Nights {
            get => _pluginOptionsAccessor.GetValueInt32(nameof(Nights), 2);
            set {
                _pluginOptionsAccessor.SetValueInt32(nameof(Nights), value);
                RaisePropertyChanged();
            }
        }

        public int Cycles {
            get => _pluginOptionsAccessor.GetValueInt32(nameof(Cycles), 1);
            set {
                _pluginOptionsAccessor.SetValueInt32(nameof(Cycles), value);
                RaisePropertyChanged();
            }
        }

        public int Priority {
            get => _pluginOptionsAccessor.GetValueInt32(nameof(Priority), 1);
            set {
                _pluginOptionsAccessor.SetValueInt32(nameof(Priority), value);
                RaisePropertyChanged();
            }
        }

        public double MinMag {
            get => _pluginOptionsAccessor.GetValueDouble(nameof(MinMag), 5d);
            set {
                _pluginOptionsAccessor.SetValueDouble(nameof(MinMag), value);
                RaisePropertyChanged();
            }
        }

        public double MaxMag {
            get => _pluginOptionsAccessor.GetValueDouble(nameof(MaxMag), 12d);
            set {
                _pluginOptionsAccessor.SetValueDouble(nameof(MaxMag), value);
                RaisePropertyChanged();
            }
        }

        public double MinSep {
            get => _pluginOptionsAccessor.GetValueDouble(nameof(MinSep), 0d);
            set {
                _pluginOptionsAccessor.SetValueDouble(nameof(MinSep), value);
                RaisePropertyChanged();
            }
        }

        public double MaxSep {
            get => _pluginOptionsAccessor.GetValueDouble(nameof(MaxSep), 10d);
            set {
                _pluginOptionsAccessor.SetValueDouble(nameof(MaxSep), value);
                RaisePropertyChanged();
            }
        }

        public bool DomePositionLock {
            get => _pluginOptionsAccessor.GetValueBoolean(nameof(DomePositionLock), false);
            set {
                _pluginOptionsAccessor.SetValueBoolean(nameof(DomePositionLock), value);
                RaisePropertyChanged();
            }
        }

        public double DomePosition {
            get => _pluginOptionsAccessor.GetValueDouble(nameof(DomePosition), 0d);
            set {
                _pluginOptionsAccessor.SetValueDouble(nameof(DomePosition), value);
                RaisePropertyChanged();
            }
        }

        public double DomeSlitWidth {
            get => _pluginOptionsAccessor.GetValueDouble(nameof(DomeSlitWidth), 0d);
            set {
                _pluginOptionsAccessor.SetValueDouble(nameof(DomeSlitWidth), value);
                RaisePropertyChanged();
            }
        }

        public string DefaultTemplate {
            get => _pluginOptionsAccessor.GetValueString(nameof(DefaultTemplate), string.Empty);
            set {
                _pluginOptionsAccessor.SetValueString(nameof(DefaultTemplate), value);
                RaisePropertyChanged();
            }
        }

        public string DefaultRefTemplate {
            get => _pluginOptionsAccessor.GetValueString(nameof(DefaultRefTemplate), string.Empty);
            set {
                _pluginOptionsAccessor.SetValueString(nameof(DefaultRefTemplate), value);
                RaisePropertyChanged();
            }
        }

        public int Exposures {
            get => _pluginOptionsAccessor.GetValueInt32(nameof(Exposures), 1000);
            set {
                _pluginOptionsAccessor.SetValueInt32(nameof(Exposures), value);
                RaisePropertyChanged();
            }
        }

        public double ExposureTime {
            get => _pluginOptionsAccessor.GetValueDouble(nameof(ExposureTime), 1d);
            set {
                _pluginOptionsAccessor.SetValueDouble(nameof(ExposureTime), value);
                RaisePropertyChanged();
            }
        }

        public string User {
            get => _pluginOptionsAccessor.GetValueString(nameof(User), string.Empty);
            set {
                _pluginOptionsAccessor.SetValueString(nameof(User), value);
                RaisePropertyChanged();
            }
        }

        public int ShowEveryNthImage {
            get => _pluginOptionsAccessor.GetValueInt32(nameof(ShowEveryNthImage), 10);
            set {
                _pluginOptionsAccessor.SetValueInt32(nameof(ShowEveryNthImage), value);
                RaisePropertyChanged();
            }
        }

        public int CheckImageTimeWithinMinutes {
            get => _pluginOptionsAccessor.GetValueInt32(nameof(CheckImageTimeWithinMinutes), 15);
            set {
                _pluginOptionsAccessor.SetValueInt32(nameof(CheckImageTimeWithinMinutes), value);
                RaisePropertyChanged();
            }
        }

        public double MaxReferenceMag {
            get => _pluginOptionsAccessor.GetValueDouble(nameof(MaxReferenceMag), 8d);
            set {
                _pluginOptionsAccessor.SetValueDouble(nameof(MaxReferenceMag), value);
                RaisePropertyChanged();
            }
        }

        public double MinReferenceMag {
            get => _pluginOptionsAccessor.GetValueDouble(nameof(MinReferenceMag), 0d);
            set {
                _pluginOptionsAccessor.SetValueDouble(nameof(MinReferenceMag), value);
                RaisePropertyChanged();
            }
        }

        public int ReferenceExposures {
            get => _pluginOptionsAccessor.GetValueInt32(nameof(ReferenceExposures), 300);
            set {
                _pluginOptionsAccessor.SetValueInt32(nameof(ReferenceExposures), value);
                RaisePropertyChanged();
            }
        }

        public bool UseSimbadRefStars {
            get => _pluginOptionsAccessor.GetValueBoolean(nameof(UseSimbadRefStars), true);
            set {
                _pluginOptionsAccessor.SetValueBoolean(nameof(UseSimbadRefStars), value);
                RaisePropertyChanged();
            }
        }

        public bool UseUSNOSingleStarList {
            get => _pluginOptionsAccessor.GetValueBoolean(nameof(UseUSNOSingleStarList), true);
            set {
                _pluginOptionsAccessor.SetValueBoolean(nameof(UseUSNOSingleStarList), value);
                RaisePropertyChanged();
            }
        }

        public bool GetGalaxyFillins {
            get => _pluginOptionsAccessor.GetValueBoolean(nameof(GetGalaxyFillins), false);
            set {
                _pluginOptionsAccessor.SetValueBoolean(nameof(GetGalaxyFillins), value);
                RaisePropertyChanged();
            }
        }

        public double MaxGalaxyMag {
            get => _pluginOptionsAccessor.GetValueDouble(nameof(MaxGalaxyMag), 16d);
            set {
                _pluginOptionsAccessor.SetValueDouble(nameof(MaxGalaxyMag), value);
                RaisePropertyChanged();
            }
        }

        public string GalaxyTemplate {
            get => _pluginOptionsAccessor.GetValueString(nameof(GalaxyTemplate), string.Empty);
            set {
                _pluginOptionsAccessor.SetValueString(nameof(GalaxyTemplate), value);
                RaisePropertyChanged();
            }
        }

        public string TelescopeName {
            get => Telescope.TelescopeName;
            set {
                var telescope = Telescope;
                telescope.TelescopeName = value;
                Telescope = telescope;
                RaisePropertyChanged();
            }
        }

        public double ApertureD {
            get => Telescope.ApertureD;
            set {
                var telescope = Telescope;
                telescope.ApertureD = value;
                Telescope = telescope;
                RaisePropertyChanged();
            }
        }

        public double ObstructionD {
            get => Telescope.ObstructionD;
            set {
                var telescope = Telescope;
                telescope.ObstructionD = value;
                Telescope = telescope;
                RaisePropertyChanged();
            }
        }

        public double Focallength {
            get => Telescope.Focallength;
            set {
                var telescope = Telescope;
                telescope.Focallength = value;
                Telescope = telescope;
                RaisePropertyChanged();
            }
        }

        public Telescope Telescope {
            get => JsonConvert.DeserializeObject<Telescope>(_pluginOptionsAccessor.GetValueString(nameof(Telescope), DefaultTelescope()));
            set {
                _pluginOptionsAccessor.SetValueString(nameof(Telescope), JsonConvert.SerializeObject(value));
                RaisePropertyChanged();
            }
        }

        public string BarlowName {
            get => Barlow.BarlowName;
            set {
                var barlow = Barlow;
                barlow.BarlowName = value;
                Barlow = barlow;
                RaisePropertyChanged();
            }
        }

        public double BarlowFactor {
            get => Barlow.BarlowFactor;
            set {
                var barlow = Barlow;
                barlow.BarlowFactor = value;
                Barlow = barlow;
                RaisePropertyChanged();
            }
        }

        public Barlow Barlow {
            get => JsonConvert.DeserializeObject<Barlow>(_pluginOptionsAccessor.GetValueString(nameof(Barlow), DefaultBarlow()));
            set {
                _pluginOptionsAccessor.SetValueString(nameof(Barlow), JsonConvert.SerializeObject(value));
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

        private void ProfileService_ProfileChanged(object sender, EventArgs e) {
            RaiseAllPropertiesChanged();
        }

        protected void RaisePropertyChanged([CallerMemberName] string propertyName = null) {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected void RaiseAllPropertiesChanged() {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
        }

        private static string DefaultTelescope() {
            return JsonConvert.SerializeObject(new Telescope("PW1000", 1000, 470, 6000));
        }

        private static string DefaultBarlow() {
            return JsonConvert.SerializeObject(new Barlow("2x Barlow", 2));
        }

        private bool SpeckleSettingsMigrated {
            get => _pluginOptionsAccessor.GetValueBoolean(nameof(SpeckleSettingsMigrated), false);
            set {
                _pluginOptionsAccessor.SetValueBoolean(nameof(SpeckleSettingsMigrated), value);
            }
        }

        // Migrate settings from Settings to Profile
        // This is a one-time operation that pertains only to settings that existed before the use of profiles for storing settings.
        // Newer settings should not be put in here.
        private void MigrateSettingsToProfile() {
            AltitudeMax = Settings.Default.AltitudeMax;
            AltitudeMin = Settings.Default.AltitudeMin;
            CheckImageTimeWithinMinutes = Settings.Default.CheckImageTimeWithinMinutes;
            Cycles = Settings.Default.Cycles;
            DefaultRefTemplate = Settings.Default.DefaultRefTemplate;
            DefaultTemplate = Settings.Default.DefaultTemplate;
            DomePosition = Settings.Default.DomePosition;
            DomePositionLock = Settings.Default.DomePositionLock;
            DomeSlitWidth = Settings.Default.DomeSlitWidth;
            Exposures = Settings.Default.Exposures;
            ExposureTime = Settings.Default.ExposureTime;
            GalaxyTemplate = Settings.Default.GalaxyTemplate;
            GetGalaxyFillins = Settings.Default.GetGalaxyFillins;
            MaxGalaxyMag = Settings.Default.MaxGalaxyMag;
            MaxMag = Settings.Default.MaxMag;
            MaxReferenceMag = Settings.Default.MaxReferenceMag;
            MaxSep = Settings.Default.MaxSep;
            MDistance = Settings.Default.MDistance;
            MinMag = Settings.Default.MinMag;
            MinReferenceMag = Settings.Default.MinReferenceMag;
            MinSep = Settings.Default.MinSep;
            MoonDistance = Settings.Default.MoonDistance;
            Nights = Settings.Default.Nights;
            Priority = Settings.Default.Priority;
            ReferenceExposures = Settings.Default.ReferenceExposures;
            ShowEveryNthImage = Settings.Default.ShowEveryNthImage;
            User = Settings.Default.User;
            UseSimbadRefStars = Settings.Default.UseSimbadRefStars;
            UseUSNOSingleStarList = Settings.Default.UseUSNOSingleStarList;

            Barlow = JsonConvert.DeserializeObject<Barlow>(Settings.Default.Barlow);
            Telescope = JsonConvert.DeserializeObject<Telescope>(Settings.Default.Telescope);
        }
    }
}
