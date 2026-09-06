using NINA.Plugin.Speckle.Model;

namespace NINA.Plugin.Speckle.Services {

    public interface ITargetPlanner {
        TargetPlan BuildPlan(SpeckleTarget target, bool isReferenceLeg, Speckle options);

        TargetPlan BuildPlan(SpeckleTarget target, bool isReferenceLeg, TargetPlanDefaults defaults);
    }
}
