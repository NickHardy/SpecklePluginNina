using NINA.Plugin.Speckle.Workflow;
using System.Collections.ObjectModel;

namespace NINA.Plugin.Speckle.Dockables {

    public static class ChainStateBuilder {

        public static void Build(ObservableCollection<ChainStepVM> targetSteps, ObservableCollection<ChainStepVM> referenceSteps) {
            targetSteps.Clear();
            targetSteps.Add(new ChainStepVM("Target", WorkflowPhase.PickingTarget, false));
            targetSteps.Add(new ChainStepVM("Slew", WorkflowPhase.AwaitingSlewConfirmation, false));
            targetSteps.Add(new ChainStepVM("Frame", WorkflowPhase.PositioningRoi, false));
            targetSteps.Add(new ChainStepVM("Exposure", WorkflowPhase.CalibratingExposure, false));
            targetSteps.Add(new ChainStepVM("Capture", WorkflowPhase.RunningVideoExposures, false));

            referenceSteps.Clear();
            referenceSteps.Add(new ChainStepVM("Slew", WorkflowPhase.AwaitingSlewConfirmation, true));
            referenceSteps.Add(new ChainStepVM("Frame", WorkflowPhase.PositioningRoi, true));
            referenceSteps.Add(new ChainStepVM("Exposure", WorkflowPhase.CalibratingExposure, true));
            referenceSteps.Add(new ChainStepVM("Capture", WorkflowPhase.RunningVideoExposures, true));

            targetSteps[0].IsFirst = true;
            referenceSteps[0].IsFirst = true;
        }

        public static void Update(ObservableCollection<ChainStepVM> targetSteps, ObservableCollection<ChainStepVM> referenceSteps,
                                  WorkflowPhase phase, bool isReferenceLeg, bool isRunning, bool faulted, bool referenceLegVisible) {
            if (!isRunning) {
                SetAll(targetSteps, ChainStepState.Pending);
                SetAll(referenceSteps, ChainStepState.Pending);
                return;
            }
            if (phase == WorkflowPhase.Stopped || phase == WorkflowPhase.ListLoaded) {
                SetAll(targetSteps, ChainStepState.Pending);
                SetAll(referenceSteps, ChainStepState.Pending);
                return;
            }
            if (phase == WorkflowPhase.TargetComplete) {
                SetAll(targetSteps, ChainStepState.Done);
                SetAll(referenceSteps, referenceLegVisible ? ChainStepState.Done : ChainStepState.Pending);
                return;
            }

            var mapped = MapPhase(phase);
            if (isReferenceLeg) {
                SetAll(targetSteps, ChainStepState.Done);
                ApplyLeg(referenceSteps, mapped, faulted);
            } else {
                SetAll(referenceSteps, ChainStepState.Pending);
                ApplyLeg(targetSteps, mapped, faulted);
            }
        }

        private static void ApplyLeg(ObservableCollection<ChainStepVM> steps, WorkflowPhase mapped, bool faulted) {
            var activeIndex = -1;
            for (var i = 0; i < steps.Count; i++) {
                if (steps[i].Phase == mapped) {
                    activeIndex = i;
                    break;
                }
            }
            if (activeIndex < 0) {
                SetAll(steps, ChainStepState.Pending);
                return;
            }
            for (var i = 0; i < steps.Count; i++) {
                if (i < activeIndex) {
                    steps[i].State = ChainStepState.Done;
                } else if (i == activeIndex) {
                    steps[i].State = faulted ? ChainStepState.Error : ChainStepState.Active;
                } else {
                    steps[i].State = ChainStepState.Pending;
                }
            }
        }

        private static void SetAll(ObservableCollection<ChainStepVM> steps, ChainStepState state) {
            foreach (var step in steps) {
                step.State = state;
            }
        }

        private static WorkflowPhase MapPhase(WorkflowPhase phase) {
            switch (phase) {
                case WorkflowPhase.Slewing:
                case WorkflowPhase.Centering:
                case WorkflowPhase.SwitchingToWide:
                case WorkflowPhase.SwitchingToScience:
                    return WorkflowPhase.AwaitingSlewConfirmation;
                case WorkflowPhase.AwaitingImageConfirmation:
                    return WorkflowPhase.RunningVideoExposures;
                default:
                    return phase;
            }
        }

    }
}
