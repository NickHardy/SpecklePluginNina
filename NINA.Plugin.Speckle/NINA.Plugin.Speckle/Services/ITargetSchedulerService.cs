using NINA.Plugin.Speckle.Model;
using System;
using System.Collections.Generic;

namespace NINA.Plugin.Speckle.Services {

    public interface ITargetSchedulerService {

        SpeckleTarget PickNextTarget(IReadOnlyList<SpeckleTarget> targets, SchedulingOptions opts, double? currentRa, DateTime now);

        void MarkCycleComplete(SpeckleTarget target, DateTime imagedAt);

        void PromoteTarget(SpeckleTarget target);
    }
}
