using NINA.Plugin.Speckle.Services;
using System;

namespace NINA.Plugin.Speckle.Workflow {

    public class WorkflowPhaseChangedEventArgs : EventArgs {
        public WorkflowPhase Phase { get; init; }
        public bool IsReferenceLeg { get; init; }
        public TargetPlan Plan { get; init; }
    }

    public class RoiSeriesProgress : EventArgs {
        public int FrameNumber { get; init; }
        public int Total { get; init; }
        public double Fps { get; init; }
    }
}
