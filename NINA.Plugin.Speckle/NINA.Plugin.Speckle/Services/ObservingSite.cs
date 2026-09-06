using NINA.Astrometry;
using NINA.Profile.Interfaces;

namespace NINA.Plugin.Speckle.Services {

    public static class ObservingSite {

        public static double Mod360(double degrees) {
            var wrapped = degrees % 360d;
            return wrapped < 0d ? wrapped + 360d : wrapped;
        }

        public static ObserverInfo Of(IProfileService profileService) {
            var astrometry = profileService.ActiveProfile.AstrometrySettings;
            return new ObserverInfo {
                Latitude = astrometry.Latitude,
                Longitude = astrometry.Longitude,
                Elevation = astrometry.Elevation
            };
        }
    }
}
