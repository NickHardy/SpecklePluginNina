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

        ImagePattern roiXPattern = new ImagePattern("$$ROIX$$", "X-position of the ROI", "Speckle");
        ImagePattern roiYPattern = new ImagePattern("$$ROIY$$", "Y-position of the ROI", "Speckle");

        [ImportingConstructor]
        public Speckle(IOptionsVM options) {
            if (Settings.Default.UpdateSettings) {
                Settings.Default.Upgrade();
                Settings.Default.UpdateSettings = false;
                CoreUtil.SaveSettings(Settings.Default);
            }
            roiXPattern.Value = "0";
            roiYPattern.Value = "0";

            options.AddImagePattern(roiXPattern);
            options.AddImagePattern(roiYPattern);
        }

        public Speckle() {
            if (Settings.Default.UpdateSettings) {
                Settings.Default.Upgrade();
                Settings.Default.UpdateSettings = false;
                CoreUtil.SaveSettings(Settings.Default);
            }
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

        public string DefaultTemplate {
            get => Settings.Default.DefaultTemplate;
            set {
                Settings.Default.DefaultTemplate = value;
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

        public int ReferenceExposures {
            get => Settings.Default.ReferenceExposures;
            set {
                Settings.Default.ReferenceExposures = value;
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
