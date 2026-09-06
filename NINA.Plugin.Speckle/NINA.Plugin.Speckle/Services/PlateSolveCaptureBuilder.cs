using NINA.Core.Model.Equipment;
using NINA.Equipment.Model;
using NINA.Profile.Interfaces;

namespace NINA.Plugin.Speckle.Services {

    public static class PlateSolveCaptureBuilder {

        public static CaptureSequence Build(IProfileService profileService, string imageType) {
            var settings = profileService.ActiveProfile.PlateSolveSettings;
            return new CaptureSequence(
                settings.ExposureTime,
                imageType,
                settings.Filter,
                new BinningMode(settings.Binning, settings.Binning),
                1
            );
        }
    }
}
