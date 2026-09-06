using NINA.Plugin.Speckle.Model;

namespace NINA.Plugin.Speckle.Services {

    public static class SchedulingRules {

        public static bool HasCyclesLeft(SpeckleTarget target) {
            return target.Nights > target.Completed_nights && target.Cycles > target.Completed_cycles;
        }

        public static bool IsImagedType(SpeckleTarget target) {
            return target.Type == "M" || target.Type == "C" || target.Type == "G";
        }
    }
}
