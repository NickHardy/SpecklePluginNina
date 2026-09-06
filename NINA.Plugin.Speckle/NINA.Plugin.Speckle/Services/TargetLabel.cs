using NINA.Plugin.Speckle.Model;

namespace NINA.Plugin.Speckle.Services {

    public static class TargetLabel {

        public static string Of(SpeckleTarget target) {
            if (target == null) {
                return "an unknown target";
            }
            if (!string.IsNullOrWhiteSpace(target.Name)) {
                return target.Name;
            }
            return string.IsNullOrWhiteSpace(target.GaiaNum) ? "an unnamed target" : target.GaiaNum;
        }
    }
}
