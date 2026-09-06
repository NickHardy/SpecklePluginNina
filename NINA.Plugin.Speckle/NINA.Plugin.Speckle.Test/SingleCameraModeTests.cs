using NINA.Plugin.Speckle.Workflow;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace NINA.Plugin.Speckle.Test {

    public class SingleCameraModeTests {

        [Fact]
        public async Task SingleCamera_FullAuto_NeverTouchesTheLightPath() {
            var harness = new Harness { AutoRun = true };
            harness.Scheduler.Picks.Enqueue(harness.MakeTarget("T1", 0));

            await Harness.AwaitAsync(harness.Coordinator.RunAsync(harness.MakeOptions(dualCamera: false), null, CancellationToken.None));

            Assert.Empty(harness.Recorder.Entries.Where(entry => entry.StartsWith("light:")));
            Assert.DoesNotContain(WorkflowPhase.SwitchingToWide, harness.Phases);
            Assert.DoesNotContain(WorkflowPhase.SwitchingToScience, harness.Phases);
            Assert.Equal(1, harness.Coordinator.Session.TargetsImaged);
        }

        [Fact]
        public async Task SingleCamera_FullAuto_RunsSlewThenRoiThenCapture() {
            var harness = new Harness { AutoRun = true };
            harness.Scheduler.Picks.Enqueue(harness.MakeTarget("T1", 0));

            await Harness.AwaitAsync(harness.Coordinator.RunAsync(harness.MakeOptions(dualCamera: false), null, CancellationToken.None));

            var slew = harness.Recorder.IndexOf("slew");
            var roi = harness.Recorder.IndexOf("roi:T1");
            var video = harness.Recorder.IndexOf("video:start");
            Assert.True(slew >= 0, "The mount must be slewed");
            Assert.True(roi > slew, "Region of interest positioning must follow the slew");
            Assert.True(video > roi, "The capture must follow region of interest positioning");

            Harness.AssertSubsequence(harness.Phases,
                WorkflowPhase.AwaitingSlewConfirmation,
                WorkflowPhase.Slewing,
                WorkflowPhase.PositioningRoi,
                WorkflowPhase.RunningVideoExposures);
            Assert.Equal(WorkflowPhase.ListLoaded, harness.Coordinator.Session.Phase);
        }

        [Fact]
        public async Task SingleCamera_SemiAutonomous_PreviewRunsAtTheSlewGateAndStopsBeforeTheRoi() {
            var harness = new Harness { AutoRun = false };
            harness.Scheduler.Picks.Enqueue(harness.MakeTarget("T1", 0));

            var runTask = harness.Coordinator.RunAsync(harness.MakeOptions(dualCamera: false), null, CancellationToken.None);

            await Harness.WaitUntilAsync(() => harness.Coordinator.Gate.Pending is { Kind: GateKind.SlewConfirmation });
            await Harness.WaitUntilAsync(() => harness.Recorder.Count("preview:start") == 1);
            Assert.Empty(harness.Recorder.Entries.Where(entry => entry.StartsWith("light:")));
            harness.Coordinator.Confirm();

            await Harness.WaitUntilAsync(() => harness.Coordinator.Gate.Pending is { Kind: GateKind.ImageConfirmation });
            var previewStop = harness.Recorder.IndexOf("preview:stop");
            var roi = harness.Recorder.IndexOf("roi:T1");
            Assert.True(previewStop >= 0 && previewStop < roi, "The centring preview must stop before region of interest positioning");
            harness.Coordinator.Confirm();

            await Harness.AwaitAsync(runTask);
            Assert.Empty(harness.Recorder.Entries.Where(entry => entry.StartsWith("light:")));
            Assert.Equal(1, harness.Coordinator.Session.TargetsImaged);
        }

        [Fact]
        public async Task SingleCamera_ImagesTheReferenceLegToo() {
            var harness = new Harness { AutoRun = true };
            var target = harness.MakeTarget("T1");
            harness.Scheduler.Picks.Enqueue(target);

            await Harness.AwaitAsync(harness.Coordinator.RunAsync(harness.MakeOptions(dualCamera: false), null, CancellationToken.None));

            Assert.Equal(2, harness.Recorder.Count("video:start"));
            Assert.Empty(harness.Recorder.Entries.Where(entry => entry.StartsWith("light:")));
            Assert.Equal(1, harness.Coordinator.Session.TargetsImaged);
        }

        [Fact]
        public async Task SingleCamera_CaptureModeFallsBackWhenNothingReadsItLive() {
            var harness = new Harness { AutoRun = true };
            harness.Scheduler.Picks.Enqueue(harness.MakeTarget("T1", 0));
            var options = harness.MakeOptions(dualCamera: false);
            Assert.Null(options.ReadVideoExposuresMode);

            await Harness.AwaitAsync(harness.Coordinator.RunAsync(options, null, CancellationToken.None));

            Assert.All(harness.Acquisition.Requests, request => Assert.Equal(options.VideoExposuresMode, request.Mode));
            Assert.Equal(1, harness.Coordinator.Session.TargetsImaged);
        }
    }
}
