using Newtonsoft.Json;
using NINA.Core.Model;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Core.Utility;
using NINA.Core.Utility.Notification;
using NINA.Image.ImageData;
using NINA.Plugin.Interfaces;
using NINA.Plugin.Speckle.Model;
using NINA.Plugin.Speckle.Sequencer.Utility;
using NINA.Plugin.Speckle.Web;
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
using System.Threading;
using System.Windows.Input;
using CsvHelper.Configuration;
using System.Windows.Forms;
using System.Globalization;
using CsvHelper;
using System.IO;
using System.Media;
using System.Reflection;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace NINA.Plugin.Speckle {

    public class SpeckleDocSection {
        public string Title { get; set; }
        public string Content { get; set; }

        public override string ToString() => Title;
    }

    [Export(typeof(IPluginManifest))]
    [Export(typeof(Speckle))]
    [PartCreationPolicy(CreationPolicy.Shared)]
    public class Speckle : PluginBase, INotifyPropertyChanged {

        private readonly IProfileService _profileService;
        private readonly PluginOptionsAccessor _pluginOptionsAccessor;
        private readonly ICameraMediator _cameraMediator;
        private readonly IImageSaveMediator _imageSaveMediator;

        public ImagePattern notePattern = new("$$NOTE$$", "Possible note about target", "Speckle");
        public ImagePattern speckleRunPattern = new ImagePattern("$$SPECKLERUN$$", "Current speckle imaging run for the target", "Speckle");
        public ImagePattern name1Pattern = new("$$NAME1$$", "Target name 1", "Speckle");
        public ImagePattern name2Pattern = new("$$NAME2$$", "Target name 2", "Speckle");
        public ImagePattern projectPattern = new("$$PROJECT$$", "Project for given target", "Speckle");
        public ImagePattern observerPattern = new("$$OBSERVER$$", "Observer for given target", "Speckle");
        public ImagePattern gaiaNumberPattern = new("$$GAIANR$$", "Target Gaia number", "Speckle");

        [ImportingConstructor]
        public Speckle(IProfileService profileService, IOptionsVM options, IImageSaveMediator imageSaveMediator, ICameraMediator cameraMediator) {
            _profileService = profileService;
            _cameraMediator = cameraMediator;
            _profileService.ProfileChanged += ProfileService_ProfileChanged;

            Guid? guid = PluginOptionsAccessor.GetAssemblyGuid(typeof(Speckle)) ?? throw new Exception($"GUID was not found in assembly metadata");
            _pluginOptionsAccessor = new PluginOptionsAccessor(_profileService, guid.Value);

            var scope = _pluginOptionsAccessor.GetValueString(nameof(Telescope), DefaultTelescope());
            if (scope == "null") { _pluginOptionsAccessor.SetValueString(nameof(Telescope), DefaultTelescope()); }
            var barlow = _pluginOptionsAccessor.GetValueString(nameof(Barlow), DefaultBarlow());
            if (barlow == "null") { _pluginOptionsAccessor.SetValueString(nameof(Barlow), DefaultBarlow()); }

            if (!SpeckleSettingsMigrated) {
                Logger.Info($"Migrating app settings to NINA profile {_profileService.ActiveProfile.Name} ({_profileService.ActiveProfile.Id})");
                MigrateSettingsToProfile();
                SpeckleSettingsMigrated = true;
            }

            if (!Services.RigStore.Exists) {
                MigrateRigFromProfile();
                WarnIfRigIncomplete();
            }

            notePattern.Value = string.Empty;
            options.AddImagePattern(notePattern);
            speckleRunPattern.Value = string.Empty;
            options.AddImagePattern(speckleRunPattern);
            name1Pattern.Value = string.Empty;
            options.AddImagePattern(name1Pattern);
            name2Pattern.Value = string.Empty;
            options.AddImagePattern(name2Pattern);
            projectPattern.Value = string.Empty;
            options.AddImagePattern(projectPattern);
            observerPattern.Value = string.Empty;
            options.AddImagePattern(observerPattern);
            gaiaNumberPattern.Value = string.Empty;
            options.AddImagePattern(gaiaNumberPattern);

            OpenFileCommand = new GalaSoft.MvvmLight.Command.RelayCommand<bool>(pickGaiaList => OpenFile(false));
            OpenGaiaFileCommand = new GalaSoft.MvvmLight.Command.RelayCommand<bool>(pickGaiaList => OpenFile(true));

            _imageSaveMediator = imageSaveMediator;
            _imageSaveMediator.BeforeFinalizeImageSaved += ImageSaveMediator_BeforeFinalizeImageSaved;

            OperatorPageHost.Changed += OperatorPage_Changed;
            CopyOperatorPageUrlCommand = new GalaSoft.MvvmLight.Command.RelayCommand(() => CopyToClipboard(OperatorPageUrl));
            CopyOperatorFirewallRuleCommand = new GalaSoft.MvvmLight.Command.RelayCommand(() => CopyToClipboard(OperatorFirewallRule));
            OpenOperatorPageCommand = new GalaSoft.MvvmLight.Command.RelayCommand(OpenOperatorPage);
            ShowFringeDemoCommand = new GalaSoft.MvvmLight.Command.RelayCommand(Services.FringeDemoRequest.Raise);
            RunCameraBenchmarkCommand = new GalaSoft.MvvmLight.Command.RelayCommand(Services.BenchmarkRequest.Raise);
            UseConnectedWideCameraCommand = new GalaSoft.MvvmLight.Command.RelayCommand(() => RememberConnectedCamera(true));
            UseConnectedScienceCameraCommand = new GalaSoft.MvvmLight.Command.RelayCommand(() => RememberConnectedCamera(false));
        }

        public override Task Initialize() {
            try {
                OperatorPageHost.ApplyOptions();
            } catch (Exception ex) {
                Logger.Error("Speckle operator page could not be started during plugin initialization", ex);
            }
            return base.Initialize();
        }

        public override Task Teardown() {
            _profileService.ProfileChanged -= ProfileService_ProfileChanged;
            if (_imageSaveMediator != null) {
                _imageSaveMediator.BeforeFinalizeImageSaved -= ImageSaveMediator_BeforeFinalizeImageSaved;
            }
            try {
                OperatorPageHost.Changed -= OperatorPage_Changed;
                OperatorPageHost.Stop();
            } catch (Exception ex) {
                Logger.Error("Speckle operator page could not be stopped during plugin teardown", ex);
            }
            return base.Teardown();
        }

        public ICommand OpenFileCommand { get; private set; }
        public ICommand OpenGaiaFileCommand { get; private set; }
        public ICommand CopyOperatorPageUrlCommand { get; private set; }
        public ICommand CopyOperatorFirewallRuleCommand { get; private set; }
        public ICommand OpenOperatorPageCommand { get; private set; }
        public ICommand ShowFringeDemoCommand { get; private set; }

        public ICommand RunCameraBenchmarkCommand { get; private set; }

        public ICommand UseConnectedWideCameraCommand { get; private set; }

        public ICommand UseConnectedScienceCameraCommand { get; private set; }

        public bool EnableOperatorPage {
            get => _pluginOptionsAccessor.GetValueBoolean(nameof(EnableOperatorPage), false);
            set {
                _pluginOptionsAccessor.SetValueBoolean(nameof(EnableOperatorPage), value);
                RaisePropertyChanged();
                ApplyOperatorPageOptions();
            }
        }

        public bool OperatorPageLanVisible {
            get => _pluginOptionsAccessor.GetValueBoolean(nameof(OperatorPageLanVisible), false);
            set {
                _pluginOptionsAccessor.SetValueBoolean(nameof(OperatorPageLanVisible), value);
                RaisePropertyChanged();
                ApplyOperatorPageOptions();
            }
        }

        public int OperatorPagePort {
            get => _pluginOptionsAccessor.GetValueInt32(nameof(OperatorPagePort), 32323);
            set {
                _pluginOptionsAccessor.SetValueInt32(nameof(OperatorPagePort), value);
                RaisePropertyChanged();
                ApplyOperatorPageOptions();
            }
        }

        public bool OperatorPageRunning => OperatorPageHost.IsRunning;

        public string OperatorPageUrl {
            get {
                var urls = OperatorPageHost.Urls;
                return urls != null && urls.Count > 0 ? urls[0] : string.Empty;
            }
        }

        public string OperatorPageUrls {
            get {
                var urls = OperatorPageHost.Urls;
                return urls == null || urls.Count == 0 ? "Operator page is not running" : string.Join(Environment.NewLine, urls);
            }
        }

        public string OperatorFirewallRule =>
            "netsh advfirewall firewall add rule name=\"NINA Speckle operator page\" dir=in action=allow protocol=TCP localport="
            + OperatorPagePort.ToString(CultureInfo.InvariantCulture) + " profile=private";

        private void ApplyOperatorPageOptions() {
            try {
                OperatorPageHost.ApplyOptions();
            } catch (Exception ex) {
                Logger.Error("Speckle operator page could not be reconfigured", ex);
            }
            OperatorPage_Changed(this, EventArgs.Empty);
        }

        private void OperatorPage_Changed(object sender, EventArgs e) {
            RaisePropertyChanged(nameof(OperatorPageRunning));
            RaisePropertyChanged(nameof(OperatorPageUrl));
            RaisePropertyChanged(nameof(OperatorPageUrls));
            RaisePropertyChanged(nameof(OperatorFirewallRule));
        }

        private void OpenOperatorPage() {
            var url = OperatorPageUrl;
            if (string.IsNullOrEmpty(url)) {
                return;
            }
            try {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
            } catch (Exception ex) {
                Logger.Error("Speckle operator page could not be opened in a browser", ex);
            }
        }

        private static void CopyToClipboard(string text) {
            if (string.IsNullOrEmpty(text)) {
                return;
            }
            try {
                System.Windows.Clipboard.SetText(text);
            } catch (Exception ex) {
                Logger.Error("Speckle operator page value could not be copied to the clipboard", ex);
            }
        }

        public ICommand PlaySlewSoundCommand { get; } = new GalaSoft.MvvmLight.Command.RelayCommand(PreviewSlewSound);

        private static void PreviewSlewSound() {
            _ = Task.Run(() => {
                try {
                    using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("SpeckleSlew.wav")) {
                        if (stream == null) {
                            return;
                        }
                        using (var player = new SoundPlayer(stream)) {
                            player.PlaySync();
                        }
                    }
                } catch (Exception ex) {
                    Logger.Error(ex);
                }
            });
        }

        public string ReferenceStarListLocation {
            get => GetOption("");
            set => SetOption(value);
        }

        public string GaiaReferenceStarListLocation {
            get => GetOption("");
            set => SetOption(value);
        }

        private void OpenFile(bool gaia) {
            OpenFileDialog fileDialog = new OpenFileDialog();
            fileDialog.DefaultExt = ".csv";
            fileDialog.Filter = "Csv documents (.csv)|*.csv";

            if (fileDialog.ShowDialog() == DialogResult.OK) {
                if (gaia) {
                    GaiaReferenceStarListLocation = fileDialog.FileName;
                }
                else {
                    ReferenceStarListLocation = fileDialog.FileName;
                }
            }
        }

        public bool SaveCsvToFitsHeader {
            get => GetOption(false);
            set => SetOption(value);
        }

        public double MDistance {
            get => GetOption(5d);
            set => SetOption(value);
        }

        public double MoonDistance {
            get => GetOption(20d);
            set => SetOption(value);
        }

        public double SearchRadius {
            get => GetOption(5d);
            set => SetOption(value);
        }

        public double AltitudeMin {
            get => GetOption(40d);
            set => SetOption(value);
        }

        public double AltitudeMax {
            get => GetOption(90d);
            set => SetOption(value);
        }

        public int Nights {
            get => GetOption(2);
            set => SetOption(value);
        }

        public int Cycles {
            get => GetOption(1);
            set => SetOption(value);
        }

        public int Priority {
            get => GetOption(1);
            set => SetOption(value);
        }

        public double MinMag {
            get => GetOption(5d);
            set => SetOption(value);
        }

        public double MaxMag {
            get => GetOption(12d);
            set => SetOption(value);
        }

        public double MinSep {
            get => GetOption(0d);
            set => SetOption(value);
        }

        public double MaxSep {
            get => GetOption(10d);
            set => SetOption(value);
        }

        public bool DomePositionLock {
            get => GetOption(false);
            set => SetOption(value);
        }

        public double DomePosition {
            get => GetOption(0d);
            set => SetOption(value);
        }

        public double DomeSlitWidth {
            get => GetOption(TargetBase.DefaultSlitWidthDegrees);
            set => SetOption(value);
        }


        public bool DomeSlitSouth {
            get => GetOption(true);
            set => SetOption(value);
        }

        public bool AutomaticallyRun {
            get => GetOption(false);
            set => SetOption(value);
        }

        public double ManualSlewToleranceArcmin {
            get => GetOption(2d);
            set => SetOption(value);
        }

        public bool AutoSkipFailedReference {
            get => GetOption(true);
            set => SetOption(value);
        }

        public bool IgnoreVisibilityLimits {
            get => GetOption(false);
            set => SetOption(value);
        }

        public bool UsePlateSolving {
            get => GetOption(true);
            set => SetOption(value);
        }

        public bool PlaySlewSound {
            get => GetOption(true);
            set => SetOption(value);
        }

        public bool ShowDecimalDegrees {
            get => GetOption(false);
            set {
                SetOption(value);
                Model.CoordinateFormat.UseDecimalDegrees = value;
            }
        }

        public string RecentTargetLists {
            get => GetOption("");
            set => SetOption(value);
        }

        // The two camera rig lives outside the profile - see Services/RigStore.
        public bool DualCameraSetup {
            get => Services.RigStore.Current.DualCameraSetup;
            set => SetRig(rig => rig.DualCameraSetup = value);
        }

        public string WideProfileId {
            get => Services.RigStore.Current.WideProfileId;
            set => SetRigUnlessCleared(value, (rig, stored) => rig.WideProfileId = stored);
        }

        public int WideMirrorPosition {
            get => Services.RigStore.Current.WideMirrorPosition;
            set => SetRig(rig => rig.WideMirrorPosition = value);
        }

        public string WideCameraId {
            get => Services.RigStore.Current.WideCameraId;
            set => SetRig(rig => rig.WideCameraId = value);
        }

        public string WideCameraName {
            get => Services.RigStore.Current.WideCameraName;
            set {
                SetRig(rig => rig.WideCameraName = value);
                RaisePropertyChanged(nameof(WideCameraLabel));
            }
        }

        public string ScienceCameraId {
            get => Services.RigStore.Current.ScienceCameraId;
            set => SetRig(rig => rig.ScienceCameraId = value);
        }

        public string ScienceCameraName {
            get => Services.RigStore.Current.ScienceCameraName;
            set {
                SetRig(rig => rig.ScienceCameraName = value);
                RaisePropertyChanged(nameof(ScienceCameraLabel));
            }
        }

        public string WideCameraLabel => string.IsNullOrWhiteSpace(WideCameraName) ? "not set" : WideCameraName;

        public string ScienceCameraLabel => string.IsNullOrWhiteSpace(ScienceCameraName) ? "not set" : ScienceCameraName;

        private void RememberConnectedCamera(bool wide) {
            var camera = _cameraMediator?.GetInfo();
            if (camera == null || !camera.Connected) {
                Notification.ShowWarning("Connect the camera you want to use first, then press this again.");
                return;
            }
            var id = _profileService.ActiveProfile.CameraSettings.Id;
            if (wide) {
                WideCameraId = id;
                WideCameraName = camera.Name;
            } else {
                ScienceCameraId = id;
                ScienceCameraName = camera.Name;
            }
            Logger.Info("The " + (wide ? "wide field" : "science") + " light path is pinned to camera " + camera.Name + " (" + id + ")");
            Notification.ShowSuccess("The " + (wide ? "wide field" : "science") + " light path will always use " + camera.Name);
        }

        public double WideCompassAngle {
            get => Services.RigStore.Current.WideCompassAngle;
            set => SetRig(rig => rig.WideCompassAngle = value);
        }

        public bool WideCompassMirrored {
            get => Services.RigStore.Current.WideCompassMirrored;
            set => SetRig(rig => rig.WideCompassMirrored = value);
        }

        public bool ScienceCompassMirrored {
            get => Services.RigStore.Current.ScienceCompassMirrored;
            set => SetRig(rig => rig.ScienceCompassMirrored = value);
        }

        public double ScienceCompassAngle {
            get => Services.RigStore.Current.ScienceCompassAngle;
            set => SetRig(rig => rig.ScienceCompassAngle = value);
        }

        public string ScienceProfileId {
            get => Services.RigStore.Current.ScienceProfileId;
            set => SetRigUnlessCleared(value, (rig, stored) => rig.ScienceProfileId = stored);
        }

        public int ScienceMirrorPosition {
            get => Services.RigStore.Current.ScienceMirrorPosition;
            set => SetRig(rig => rig.ScienceMirrorPosition = value);
        }

        public bool ShowCrosshair {
            get => GetOption(true);
            set => SetOption(value);
        }

        public string DefaultFilters {
            get => _pluginOptionsAccessor.GetValueString(nameof(DefaultFilters), "");
            set {
                _pluginOptionsAccessor.SetValueString(nameof(DefaultFilters), value ?? "");
                RaisePropertyChanged();
            }
        }

        public double CenteringExposureTime {
            get {
                var stored = _pluginOptionsAccessor.GetValueDouble(nameof(CenteringExposureTime), 1);
                return stored > 0 ? stored : 1;
            }
            set {
                _pluginOptionsAccessor.SetValueDouble(nameof(CenteringExposureTime), value > 0 ? value : 1);
                RaisePropertyChanged();
            }
        }

        public int RoiSize {
            get => GetOption(512);
            set => SetOption(value);
        }

        public IList<int> AvailableRoiSizes => new List<int> { 128, 256, 512, 1024 };

        public string SlewFilter {
            get => GetOption(string.Empty);
            set => SetOptionUnlessCleared(value);
        }

        // All three dropdown sources below are cached and only rebuilt when their contents really
        // change. Returning a fresh collection makes a WPF ComboBox clear SelectedValue, which
        // blanks the displayed selection even though the stored value is untouched.
        private IList<string> availableFilters;

        private string availableFiltersKey;

        public IList<string> AvailableFilters {
            get {
                var names = Services.FilterWheelCatalog.Names(_profileService);
                var key = string.Join("|", names);
                if (availableFilters == null || availableFiltersKey != key) {
                    availableFilters = names.ToList();
                    availableFiltersKey = key;
                }
                return availableFilters;
            }
        }

        private IList<Services.ProfileChoice> wideProfileChoices;

        private string wideProfileChoicesKey;

        private IList<Services.ProfileChoice> scienceProfileChoices;

        private string scienceProfileChoicesKey;

        public IList<Services.ProfileChoice> WideProfileChoices {
            get {
                var key = Services.ProfileCatalog.Signature(_profileService, WideProfileId);
                if (wideProfileChoices == null || wideProfileChoicesKey != key) {
                    wideProfileChoices = Services.ProfileCatalog.Choices(_profileService, WideProfileId).ToList();
                    wideProfileChoicesKey = key;
                }
                return wideProfileChoices;
            }
        }

        public IList<Services.ProfileChoice> ScienceProfileChoices {
            get {
                var key = Services.ProfileCatalog.Signature(_profileService, ScienceProfileId);
                if (scienceProfileChoices == null || scienceProfileChoicesKey != key) {
                    scienceProfileChoices = Services.ProfileCatalog.Choices(_profileService, ScienceProfileId).ToList();
                    scienceProfileChoicesKey = key;
                }
                return scienceProfileChoices;
            }
        }

        public string DefaultTemplate {
            get => GetOption(string.Empty);
            set => SetOption(value);
        }

        public string DefaultRefTemplate {
            get => GetOption(string.Empty);
            set => SetOption(value);
        }

        public int Exposures {
            get => GetOption(1000);
            set => SetOption(value);
        }

        public double ExposureTime {
            get => GetOption(1d);
            set => SetOption(value);
        }

        public double MaxExposureTime {
            get => GetOption(1d);
            set => SetOption(value > 0 ? value : 1d);
        }

        public string User {
            get => GetOption(string.Empty);
            set => SetOption(value);
        }

        public int ShowEveryNthImage {
            get => GetOption(10);
            set => SetOption(value);
        }

        public int VideoExposuresMode {
            get => GetOption(0);
            set => SetOption(value);
        }

        public bool AnalyseFringes {
            get => GetOption(true);
            set => SetOption(value);
        }

        public int FringeFftSize {
            get => GetOption(0);
            set => SetOption(value);
        }

        public int FringeAnalyseEveryNthFrame {
            get => GetOption(50);
            set => SetOption(value);
        }

        public int FringeRefreshMs {
            get => GetOption(300);
            set => SetOption(value);
        }

        public bool FringeSubtractFrameMean {
            get => GetOption(true);
            set => SetOption(value);
        }

        public int FringeApodization {
            get => GetOption((int)Imaging.FringeApodizationMode.Tukey);
            set => SetOption(value);
        }

        public bool FringeRemoveNoiseBias {
            get => GetOption(true);
            set => SetOption(value);
        }

        public double FringeKSpaceRadiusPx {
            get => GetOption(0d);
            set => SetOption(value);
        }

        public double FringeWavelengthNm {
            get => GetOption(620d);
            set => SetOption(value);
        }

        public bool FringeRadialFlatten {
            get => GetOption(true);
            set => SetOption(value);
        }

        public int FringeStretch {
            get => GetOption(0);
            set => SetOption(value);
        }

        public int FringeMaskDcRadiusPx {
            get => GetOption(1);
            set => SetOption(value);
        }

        public int FringePaneOrientation {
            get => GetOption(0);
            set => SetOption(value);
        }

        public int FringePaneMode {
            get => GetOption(0);
            set => SetOption(value);
        }

        public bool FringePaneVisible {
            get => GetOption(true);
            set => SetOption(value);
        }


        public double MaxReferenceMag {
            get => GetOption(8d);
            set => SetOption(value);
        }

        public double MinReferenceMag {
            get => GetOption(0d);
            set => SetOption(value);
        }

        public int ReferenceExposures {
            get => GetOption(300);
            set => SetOption(value);
        }

        public bool UseReferenceStarList {
            get => GetOption(true);
            set => SetOption(value);
        }

        public bool UseGaiaReferenceStarList {
            get => GetOption(true);
            set => SetOption(value);
        }

        public bool UseSimbadRefStars {
            get => GetOption(true);
            set => SetOption(value);
        }

        public bool UseUSNOSingleStarList {
            get => GetOption(true);
            set => SetOption(value);
        }

        public bool LookupMissingReferenceStars {
            get => GetOption(true);
            set => SetOption(value);
        }

        public bool PreferBrighterReferenceStars {
            get => GetOption(true);
            set => SetOption(value);
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
                RaisePropertyChanged();
            }
        }

        private const string DocumentationResourceMarker = ".Documentation.";

        private List<SpeckleDocSection> docSections;
        private SpeckleDocSection selectedDocSection;

        public IReadOnlyList<SpeckleDocSection> DocSections {
            get {
                EnsureDocSectionsLoaded();
                return docSections;
            }
        }

        public SpeckleDocSection SelectedDocSection {
            get {
                EnsureDocSectionsLoaded();
                return selectedDocSection;
            }
            set {
                selectedDocSection = value;
                RaisePropertyChanged();
            }
        }

        private void EnsureDocSectionsLoaded() {
            if (docSections != null) {
                return;
            }
            docSections = LoadDocSections();
            selectedDocSection = docSections.FirstOrDefault();
        }

        private static List<SpeckleDocSection> LoadDocSections() {
            var sections = new List<SpeckleDocSection>();
            try {
                var assembly = Assembly.GetExecutingAssembly();
                var found = new List<(int Order, string Title, SpeckleDocSection Section)>();
                foreach (var resourceName in assembly.GetManifestResourceNames()) {
                    var markerIndex = resourceName.IndexOf(DocumentationResourceMarker, StringComparison.OrdinalIgnoreCase);
                    if (markerIndex < 0 || !resourceName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)) {
                        continue;
                    }

                    var fileName = resourceName.Substring(markerIndex + DocumentationResourceMarker.Length);
                    var baseName = fileName.Substring(0, fileName.Length - 4);
                    var order = int.MaxValue;
                    var separator = baseName.IndexOf('_');
                    if (separator > 0 && int.TryParse(baseName.Substring(0, separator), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedOrder)) {
                        order = parsedOrder;
                        baseName = baseName.Substring(separator + 1);
                    }

                    string content;
                    using (var stream = assembly.GetManifestResourceStream(resourceName)) {
                        if (stream == null) {
                            continue;
                        }
                        using (var reader = new StreamReader(stream)) {
                            content = reader.ReadToEnd();
                        }
                    }

                    var title = SplitPascalCase(baseName);
                    found.Add((Order: order, Title: title, Section: new SpeckleDocSection { Title = title, Content = content }));
                }

                sections.AddRange(found.OrderBy(entry => entry.Order).ThenBy(entry => entry.Title, StringComparer.OrdinalIgnoreCase).Select(entry => entry.Section));
            } catch (Exception ex) {
                Logger.Error(ex);
            }
            return sections;
        }

        private static string SplitPascalCase(string value) {
            if (string.IsNullOrWhiteSpace(value)) {
                return value;
            }
            return Regex.Replace(value, "(?<=[a-z0-9])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])", " ");
        }

        private Task ImageSaveMediator_BeforeFinalizeImageSaved(object sender, BeforeFinalizeImageSavedEventArgs e) {
            var headers = e.Image.RawImageData.MetaData.GenericHeaders;
            e.AddImagePattern(new ImagePattern(notePattern.Key, notePattern.Description, notePattern.Category) {
                Value = ItemUtility.GetHeaderValue(headers, "NOTE")
            });
            e.AddImagePattern(new ImagePattern(speckleRunPattern.Key, speckleRunPattern.Description, speckleRunPattern.Category) {
                Value = ItemUtility.GetHeaderValue(headers, "SPECRUN")
            });
            e.AddImagePattern(new ImagePattern(name1Pattern.Key, name1Pattern.Description, name1Pattern.Category) {
                Value = ItemUtility.GetHeaderValue(headers, "Name1*", "Name1")
            });
            e.AddImagePattern(new ImagePattern(name2Pattern.Key, name2Pattern.Description, name2Pattern.Category) {
                Value = ItemUtility.GetHeaderValue(headers, "Name2*", "Name2")
            });
            e.AddImagePattern(new ImagePattern(projectPattern.Key, projectPattern.Description, projectPattern.Category) {
                Value = ItemUtility.GetHeaderValue(headers, "Proj~", "Proj")
            });
            e.AddImagePattern(new ImagePattern(observerPattern.Key, observerPattern.Description, observerPattern.Category) {
                Value = ItemUtility.GetHeaderValue(headers, "Obs~", "Obs")
            });
            e.AddImagePattern(new ImagePattern(gaiaNumberPattern.Key, gaiaNumberPattern.Description, gaiaNumberPattern.Category) {
                Value = ItemUtility.GetHeaderValue(headers, "GaiaNum~", "GaiaNum")
            });
            return Task.CompletedTask;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void ProfileService_ProfileChanged(object sender, EventArgs e) {
            // Deliberately does NOT drop the dropdown caches. The list of profiles is the same
            // whichever profile is active, and replacing a ComboBox's ItemsSource makes it clear
            // SelectedValue - which left the wide/science profile boxes looking empty after every
            // switch even though the value was still stored. The caches refresh themselves when
            // the profile set really changes; see ProfileCatalog.Signature.
            RaiseAllPropertiesChanged();
        }

        private bool GetOption(bool fallback, [CallerMemberName] string optionName = null) {
            return _pluginOptionsAccessor.GetValueBoolean(optionName, fallback);
        }

        private void SetOption(bool value, [CallerMemberName] string optionName = null) {
            _pluginOptionsAccessor.SetValueBoolean(optionName, value);
            RaisePropertyChanged(optionName);
        }

        private int GetOption(int fallback, [CallerMemberName] string optionName = null) {
            return _pluginOptionsAccessor.GetValueInt32(optionName, fallback);
        }

        private void SetOption(int value, [CallerMemberName] string optionName = null) {
            _pluginOptionsAccessor.SetValueInt32(optionName, value);
            RaisePropertyChanged(optionName);
        }

        private double GetOption(double fallback, [CallerMemberName] string optionName = null) {
            return _pluginOptionsAccessor.GetValueDouble(optionName, fallback);
        }

        private void SetOption(double value, [CallerMemberName] string optionName = null) {
            _pluginOptionsAccessor.SetValueDouble(optionName, value);
            RaisePropertyChanged(optionName);
        }

        private string GetOption(string fallback, [CallerMemberName] string optionName = null) {
            return _pluginOptionsAccessor.GetValueString(optionName, fallback);
        }

        private void SetOption(string value, [CallerMemberName] string optionName = null) {
            _pluginOptionsAccessor.SetValueString(optionName, value);
            RaisePropertyChanged(optionName);
        }

        /// <summary>
        /// Stores a dropdown-backed string option, ignoring a null write. A WPF ComboBox clears
        /// SelectedValue to null when its ItemsSource is replaced - which happens on every profile
        /// switch - and the TwoWay binding would push that null back and erase the stored value.
        /// Choosing the deliberate "none" entry sends an empty string, which is still stored.
        /// </summary>
        private void SetOptionUnlessCleared(string value, [CallerMemberName] string optionName = null) {
            if (value == null) {
                Logger.Debug("Ignoring a null write to " + optionName + "; the dropdown was reset rather than changed by the user");
                RaisePropertyChanged(optionName);
                return;
            }
            SetOption(value, optionName);
        }

        private void SetRig(Action<Services.RigSettings> apply, [CallerMemberName] string optionName = null) {
            apply(Services.RigStore.Current);
            Services.RigStore.Save();
            RaisePropertyChanged(optionName);
        }

        /// <summary>Rig equivalent of <see cref="SetOptionUnlessCleared"/>, for the profile dropdowns.</summary>
        private void SetRigUnlessCleared(string value, Action<Services.RigSettings, string> apply, [CallerMemberName] string optionName = null) {
            if (value == null) {
                Logger.Debug("Ignoring a null write to " + optionName + "; the dropdown was reset rather than changed by the user");
                RaisePropertyChanged(optionName);
                return;
            }
            SetRig(rig => apply(rig, value), optionName);
        }

        /// <summary>
        /// Carries a two camera setup that was stored in the active profile over to the shared rig
        /// file, once, so upgrading users keep the setup they already configured.
        /// </summary>
        private void MigrateRigFromProfile() {
            Services.RigStore.Seed(new Services.RigSettings {
                DualCameraSetup = _pluginOptionsAccessor.GetValueBoolean(nameof(DualCameraSetup), false),
                WideProfileId = _pluginOptionsAccessor.GetValueString(nameof(WideProfileId), string.Empty),
                ScienceProfileId = _pluginOptionsAccessor.GetValueString(nameof(ScienceProfileId), string.Empty),
                WideMirrorPosition = _pluginOptionsAccessor.GetValueInt32(nameof(WideMirrorPosition), 0),
                ScienceMirrorPosition = _pluginOptionsAccessor.GetValueInt32(nameof(ScienceMirrorPosition), 0),
                WideCameraName = _pluginOptionsAccessor.GetValueString(nameof(WideCameraName), string.Empty),
                ScienceCameraName = _pluginOptionsAccessor.GetValueString(nameof(ScienceCameraName), string.Empty),
                WideCameraId = _pluginOptionsAccessor.GetValueString(nameof(WideCameraId), string.Empty),
                ScienceCameraId = _pluginOptionsAccessor.GetValueString(nameof(ScienceCameraId), string.Empty),
                WideCompassAngle = _pluginOptionsAccessor.GetValueDouble(nameof(WideCompassAngle), 0d),
                ScienceCompassAngle = _pluginOptionsAccessor.GetValueDouble(nameof(ScienceCompassAngle), 0d),
                WideCompassMirrored = _pluginOptionsAccessor.GetValueBoolean(nameof(WideCompassMirrored), false),
                ScienceCompassMirrored = _pluginOptionsAccessor.GetValueBoolean(nameof(ScienceCompassMirrored), true)
            });
            Logger.Info("The Speckle two camera rig was carried from profile " + _profileService.ActiveProfile.Name
                + " into " + Services.RigStore.FilePath + "; it is now shared by every profile");
        }

        /// <summary>
        /// The rig can only be carried over from the profile that happens to be active at load, so a
        /// setup that was configured in a different profile arrives incomplete. Say so rather than
        /// leaving a half configured rig to be discovered at the telescope.
        /// </summary>
        private void WarnIfRigIncomplete() {
            var rig = Services.RigStore.Current;
            if (!rig.DualCameraSetup) {
                return;
            }
            if (!string.IsNullOrWhiteSpace(rig.WideProfileId) && !string.IsNullOrWhiteSpace(rig.ScienceProfileId)) {
                return;
            }
            Logger.Warning("The two camera rig was carried over from profile " + _profileService.ActiveProfile.Name
                + " but the wide and science profiles are not both set. If they were configured in another profile,"
                + " pick them again under the dual camera options; the setting is now shared by every profile.");
            Notification.ShowWarning("Speckle: check the wide field and science camera profiles in the plugin options."
                + " They are now shared by every profile and could not be read from the profile that was active at startup.");
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
            return JsonConvert.SerializeObject(new Barlow("None", 1));
        }

        private bool SpeckleSettingsMigrated {
            get => _pluginOptionsAccessor.GetValueBoolean(nameof(SpeckleSettingsMigrated), false);
            set {
                _pluginOptionsAccessor.SetValueBoolean(nameof(SpeckleSettingsMigrated), value);
            }
        }

        private void MigrateSettingsToProfile() {
            AltitudeMax = Settings.Default.AltitudeMax;
            AltitudeMin = Settings.Default.AltitudeMin;
            Cycles = Settings.Default.Cycles;
            DefaultRefTemplate = Settings.Default.DefaultRefTemplate;
            DefaultTemplate = Settings.Default.DefaultTemplate;
            DomePosition = Settings.Default.DomePosition;
            DomePositionLock = Settings.Default.DomePositionLock;
            DomeSlitWidth = Settings.Default.DomeSlitWidth;
            Exposures = Settings.Default.Exposures;
            ExposureTime = Settings.Default.ExposureTime;
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
