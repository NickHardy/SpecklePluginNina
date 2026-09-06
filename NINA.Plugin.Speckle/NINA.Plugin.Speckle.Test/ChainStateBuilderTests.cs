using NINA.Plugin.Speckle.Dockables;
using NINA.Plugin.Speckle.Workflow;
using System.Collections.ObjectModel;
using System.Linq;
using Xunit;

namespace NINA.Plugin.Speckle.Test {

    public class ChainStateBuilderTests {

        private static (ObservableCollection<ChainStepVM> Target, ObservableCollection<ChainStepVM> Reference) Built() {
            var targetSteps = new ObservableCollection<ChainStepVM>();
            var referenceSteps = new ObservableCollection<ChainStepVM>();
            ChainStateBuilder.Build(targetSteps, referenceSteps);
            return (targetSteps, referenceSteps);
        }

        private static void Update(ObservableCollection<ChainStepVM> targetSteps, ObservableCollection<ChainStepVM> referenceSteps, WorkflowPhase phase) {
            ChainStateBuilder.Update(targetSteps, referenceSteps, phase, false, true, false, false);
        }

        [Theory]
        [InlineData(WorkflowPhase.SwitchingToWide)]
        [InlineData(WorkflowPhase.SwitchingToScience)]
        public void ALightPathSwitchKeepsTheChainOnTheSlewStep(WorkflowPhase phase) {
            var steps = Built();

            Update(steps.Target, steps.Reference, phase);

            var slew = steps.Target.Single(step => step.Phase == WorkflowPhase.AwaitingSlewConfirmation);
            Assert.Equal(ChainStepState.Active, slew.State);
        }

        [Theory]
        [InlineData(WorkflowPhase.SwitchingToWide)]
        [InlineData(WorkflowPhase.SwitchingToScience)]
        public void ALightPathSwitchDoesNotResetTheWholeChainToPending(WorkflowPhase phase) {
            var steps = Built();

            Update(steps.Target, steps.Reference, phase);

            Assert.Contains(steps.Target, step => step.State != ChainStepState.Pending);
        }

        [Fact]
        public void SlewingAndCenteringAlsoSitOnTheSlewStep() {
            var steps = Built();

            Update(steps.Target, steps.Reference, WorkflowPhase.Slewing);
            Assert.Equal(ChainStepState.Active, steps.Target.Single(step => step.Phase == WorkflowPhase.AwaitingSlewConfirmation).State);

            Update(steps.Target, steps.Reference, WorkflowPhase.Centering);
            Assert.Equal(ChainStepState.Active, steps.Target.Single(step => step.Phase == WorkflowPhase.AwaitingSlewConfirmation).State);
        }

        [Fact]
        public void CaptureFollowsTheStepsBeforeIt() {
            var steps = Built();

            Update(steps.Target, steps.Reference, WorkflowPhase.RunningVideoExposures);

            Assert.Equal(ChainStepState.Active, steps.Target.Single(step => step.Phase == WorkflowPhase.RunningVideoExposures).State);
            Assert.All(steps.Target.Where(step => step.Phase != WorkflowPhase.RunningVideoExposures),
                step => Assert.Equal(ChainStepState.Done, step.State));
        }
    }
}
