using NINA.Equipment.Equipment.MyCamera;
using NINA.Plugin.Speckle.Services;
using NINA.Plugin.Speckle.Workflow;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace NINA.Plugin.Speckle.Test {

    public class SpeckleWorkflowCoordinatorTests {

        [Fact]
        public async Task FullAutoHappyNight_TwoTargetsWithReferences() {
            var harness = new Harness { AutoRun = true };
            var first = harness.MakeTarget("T1");
            var second = harness.MakeTarget("T2");
            harness.Scheduler.Picks.Enqueue(first);
            harness.Scheduler.Picks.Enqueue(second);

            await Harness.AwaitAsync(harness.Coordinator.RunAsync(harness.MakeOptions(), null, CancellationToken.None));

            Assert.Equal(WorkflowPhase.ListLoaded, harness.Coordinator.Session.Phase);
            Assert.Equal(2, harness.Coordinator.Session.TargetsImaged);
            Assert.Equal(0, harness.Coordinator.Session.TargetsSkipped);
            Assert.Equal(1, first.Completed_cycles);
            Assert.Equal(1, first.Completed_ref_cycles);
            Assert.Equal(1, second.Completed_cycles);
            Assert.Equal(1, second.Completed_ref_cycles);
            Assert.Equal(2, harness.Recorder.Count("resolve:"));
            Assert.Equal(4, harness.Recorder.Count("video:start"));
            Assert.Equal(4, harness.Recorder.Count("slew"));
            Assert.Equal(4, harness.TargetList.SnapshotCount);

            var firstPrimaryVideoDone = harness.Recorder.IndexOf("video:done");
            var mark1 = harness.Recorder.IndexOf("mark:T1");
            var secondVideoStart = harness.Recorder.IndexOf("video:start", 2);
            Assert.True(firstPrimaryVideoDone < mark1, "Bookkeeping must follow the primary video exposures");
            Assert.True(mark1 < secondVideoStart, "Bookkeeping must precede the reference leg");

            Harness.AssertSubsequence(harness.Phases,
                WorkflowPhase.PickingTarget,
                WorkflowPhase.AwaitingSlewConfirmation,
                WorkflowPhase.Slewing,
                WorkflowPhase.PositioningRoi,
                WorkflowPhase.CalibratingExposure,
                WorkflowPhase.AwaitingImageConfirmation,
                WorkflowPhase.RunningVideoExposures,
                WorkflowPhase.AwaitingSlewConfirmation,
                WorkflowPhase.RunningVideoExposures,
                WorkflowPhase.TargetComplete,
                WorkflowPhase.PickingTarget,
                WorkflowPhase.TargetComplete,
                WorkflowPhase.ListLoaded);
        }

        [Fact]
        public async Task SemiAutonomous_GateSequence_BothGatesPerLeg() {
            var harness = new Harness { AutoRun = false };
            var first = harness.MakeTarget("T1");
            harness.Scheduler.Picks.Enqueue(first);

            var runTask = harness.Coordinator.RunAsync(harness.MakeOptions(), null, CancellationToken.None);

            await Harness.WaitUntilAsync(() => harness.Coordinator.Gate.Pending is { Kind: GateKind.SlewConfirmation, IsReference: false });
            Assert.Equal(WorkflowPhase.AwaitingSlewConfirmation, harness.Coordinator.Session.Phase);
            harness.Coordinator.Confirm();

            await Harness.WaitUntilAsync(() => harness.Coordinator.Gate.Pending is { Kind: GateKind.ImageConfirmation, IsReference: false });
            harness.Coordinator.Confirm();

            await Harness.WaitUntilAsync(() => harness.Coordinator.Gate.Pending is { Kind: GateKind.SlewConfirmation, IsReference: true });
            harness.Coordinator.Confirm();

            await Harness.WaitUntilAsync(() => harness.Coordinator.Gate.Pending is { Kind: GateKind.ImageConfirmation, IsReference: true });
            harness.Coordinator.Confirm();

            await Harness.AwaitAsync(runTask);

            Assert.Equal(WorkflowPhase.ListLoaded, harness.Coordinator.Session.Phase);
            Assert.Equal(0, harness.Recorder.Count("slew"));
            Assert.Equal(2, harness.Recorder.Count("video:start"));
            Assert.Equal(1, first.Completed_cycles);
            Assert.Equal(1, first.Completed_ref_cycles);
        }

        [Fact]
        public async Task SkipReference_MidVideoExposures_EndsReferenceLegOnly() {
            var harness = new Harness { AutoRun = true };
            var first = harness.MakeTarget("T1");
            harness.Scheduler.Picks.Enqueue(first);
            var refVideoStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var calls = 0;
            harness.Acquisition.OnRunRoiSeries = async (request, ct) => {
                if (Interlocked.Increment(ref calls) == 2) {
                    refVideoStarted.TrySetResult(true);
                    await Task.Delay(Timeout.Infinite, ct);
                }
                return new ExposureRunResult { FramesCaptured = request.TotalExposureCount, Fps = 10, PendingSaves = Task.CompletedTask };
            };

            var runTask = harness.Coordinator.RunAsync(harness.MakeOptions(), null, CancellationToken.None);
            await Harness.AwaitAsync(refVideoStarted.Task);
            await Harness.WaitUntilAsync(() => harness.Coordinator.Session.ImagingReference);

            harness.Coordinator.SkipReference();

            await Harness.AwaitAsync(runTask);
            Assert.Equal(WorkflowPhase.ListLoaded, harness.Coordinator.Session.Phase);
            Assert.Equal(1, harness.Coordinator.Session.TargetsImaged);
            Assert.Equal(1, first.Completed_cycles);
            Assert.Equal(0, first.Completed_ref_cycles);
        }

        [Fact]
        public async Task Abort_MidVideoExposures_AwaitsPendingSaves() {
            var harness = new Harness { AutoRun = true };
            harness.Scheduler.Picks.Enqueue(harness.MakeTarget("T1", 0));
            var videoStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var savesGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var savesFlushed = false;
            harness.Acquisition.OnRunRoiSeries = async (request, ct) => {
                videoStarted.TrySetResult(true);
                try {
                    await Task.Delay(Timeout.Infinite, ct);
                } catch (OperationCanceledException) {
                    await savesGate.Task;
                    savesFlushed = true;
                    throw;
                }
                return null;
            };

            var runTask = harness.Coordinator.RunAsync(harness.MakeOptions(), null, CancellationToken.None);
            await Harness.AwaitAsync(videoStarted.Task);

            harness.Coordinator.RequestStop();
            await Task.Delay(200);
            Assert.False(runTask.IsCompleted, "Run must wait for pending saves to flush");

            savesGate.TrySetResult(true);
            await Harness.AwaitAsync(runTask);

            Assert.True(savesFlushed);
            Assert.Equal(WorkflowPhase.Stopped, harness.Coordinator.Session.Phase);
            Assert.False(harness.Coordinator.IsRunning);
        }

        [Fact]
        public async Task FaultLadder_FullAuto_RetriesOnceThenSkipsTarget() {
            var harness = new Harness { AutoRun = true };
            var first = harness.MakeTarget("T1", 0);
            var second = harness.MakeTarget("T2", 0);
            harness.Scheduler.Picks.Enqueue(first);
            harness.Scheduler.Picks.Enqueue(second);
            harness.Roi.OnLocate = request => {
                if (request.SpeckleTarget?.Name1 == "T1") {
                    throw new InvalidOperationException("solve failed");
                }
                return new RoiPositionResult { RoiX = 10, RoiY = 10, PlatesolveSucceeded = true };
            };

            await Harness.AwaitAsync(harness.Coordinator.RunAsync(harness.MakeOptions(), null, CancellationToken.None));

            Assert.Equal(WorkflowPhase.ListLoaded, harness.Coordinator.Session.Phase);
            Assert.Equal(2, harness.Recorder.Count("roi:T1"));
            Assert.Equal(1, harness.Recorder.Count("roi:T2"));
            Assert.Equal(1, harness.Coordinator.Session.TargetsSkipped);
            Assert.Equal(1, harness.Coordinator.Session.TargetsImaged);
            Assert.Equal("skipped-after-error", first.Note2);
            Assert.False(first.ImageTarget);
            Assert.Equal(0, first.Completed_cycles);
            Assert.Equal(1, second.Completed_cycles);
        }

        [Fact]
        public async Task FaultLadder_FullAuto_SkippableStepFallsBackToDefaultExposure() {
            var harness = new Harness { AutoRun = true };
            var first = harness.MakeTarget("T1", 0);
            harness.Scheduler.Picks.Enqueue(first);
            harness.Calibration.OnCalibrate = request => throw new InvalidOperationException("calibration failed");
            var options = harness.MakeOptions();

            await Harness.AwaitAsync(harness.Coordinator.RunAsync(options, null, CancellationToken.None));

            Assert.Equal(WorkflowPhase.ListLoaded, harness.Coordinator.Session.Phase);
            Assert.Equal(2, harness.Calibration.CallCount);
            Assert.Equal(options.DefaultExposureTime, harness.Coordinator.Session.CalculatedExposureTime);
            Assert.Equal(1, harness.Recorder.Count("video:start"));
            Assert.Equal(1, harness.Coordinator.Session.TargetsImaged);
        }

        [Fact]
        public async Task Bicam_FullAuto_TransitionOrdering() {
            var harness = new Harness { AutoRun = true };
            harness.Scheduler.Picks.Enqueue(harness.MakeTarget("T1", 0));

            await Harness.AwaitAsync(harness.Coordinator.RunAsync(harness.MakeOptions(dualCamera: true), null, CancellationToken.None));

            Assert.Equal(WorkflowPhase.ListLoaded, harness.Coordinator.Session.Phase);
            var wide = harness.Recorder.IndexOf("light:Wide");
            var slew = harness.Recorder.IndexOf("slew");
            var science = harness.Recorder.IndexOf("light:Science");
            var roi = harness.Recorder.IndexOf("roi:T1");
            var video = harness.Recorder.IndexOf("video:start");
            Assert.True(wide >= 0 && slew > wide, "Wide switch must precede the slew");
            Assert.True(science > slew, "Science switch must follow the slew");
            Assert.True(roi > science, "ROI positioning must follow the science switch");
            Assert.True(video > roi, "Video exposures must follow ROI positioning");
            Assert.Equal(0, harness.Recorder.Count("preview:start"));

            Harness.AssertSubsequence(harness.Phases,
                WorkflowPhase.SwitchingToWide,
                WorkflowPhase.AwaitingSlewConfirmation,
                WorkflowPhase.Slewing,
                WorkflowPhase.SwitchingToScience,
                WorkflowPhase.PositioningRoi,
                WorkflowPhase.RunningVideoExposures);
        }

        [Fact]
        public async Task Bicam_SemiAutonomous_PreviewRunsDuringSlewGate() {
            var harness = new Harness { AutoRun = false };
            harness.Scheduler.Picks.Enqueue(harness.MakeTarget("T1", 0));

            var runTask = harness.Coordinator.RunAsync(harness.MakeOptions(dualCamera: true), null, CancellationToken.None);

            await Harness.WaitUntilAsync(() => harness.Coordinator.Gate.Pending is { Kind: GateKind.SlewConfirmation });
            await Harness.WaitUntilAsync(() => harness.Recorder.Count("preview:start") == 1);
            Assert.True(harness.Recorder.IndexOf("light:Wide") < harness.Recorder.IndexOf("preview:start"));
            harness.Coordinator.Confirm();

            await Harness.WaitUntilAsync(() => harness.Coordinator.Gate.Pending is { Kind: GateKind.ImageConfirmation });
            var previewStop = harness.Recorder.IndexOf("preview:stop");
            var science = harness.Recorder.IndexOf("light:Science");
            var roi = harness.Recorder.IndexOf("roi:T1");
            Assert.True(previewStop >= 0 && previewStop < science, "Preview must stop before the science switch");
            Assert.True(science < roi);
            harness.Coordinator.Confirm();

            await Harness.AwaitAsync(runTask);
            Assert.Equal(WorkflowPhase.ListLoaded, harness.Coordinator.Session.Phase);
        }

        [Fact]
        public async Task SingleCamera_SemiAutonomous_PreviewRunsDuringSlewGate() {
            var harness = new Harness { AutoRun = false };
            harness.Scheduler.Picks.Enqueue(harness.MakeTarget("T1", 0));

            var runTask = harness.Coordinator.RunAsync(harness.MakeOptions(), null, CancellationToken.None);

            await Harness.WaitUntilAsync(() => harness.Coordinator.Gate.Pending is { Kind: GateKind.SlewConfirmation });
            await Harness.WaitUntilAsync(() => harness.Recorder.Count("preview:start") == 1);
            Assert.Equal(0, harness.Recorder.Count("light:Wide"));
            harness.Coordinator.Confirm();

            await Harness.WaitUntilAsync(() => harness.Coordinator.Gate.Pending is { Kind: GateKind.ImageConfirmation });
            var previewStop = harness.Recorder.IndexOf("preview:stop");
            var roi = harness.Recorder.IndexOf("roi:T1");
            Assert.True(previewStop >= 0 && previewStop < roi, "Preview must stop before ROI positioning");
            harness.Coordinator.Confirm();

            await Harness.AwaitAsync(runTask);
            Assert.Equal(WorkflowPhase.ListLoaded, harness.Coordinator.Session.Phase);
        }

        [Fact]
        public async Task SingleCamera_SemiAutonomous_NoPreviewWithoutCamera() {
            var harness = new Harness { AutoRun = false };
            harness.Camera.Info = new CameraInfo { Connected = false, BitDepth = 16 };
            harness.Scheduler.Picks.Enqueue(harness.MakeTarget("T1", 0));

            var runTask = harness.Coordinator.RunAsync(harness.MakeOptions(), null, CancellationToken.None);

            await Harness.WaitUntilAsync(() => harness.Coordinator.Gate.Pending is { Kind: GateKind.SlewConfirmation });
            harness.Coordinator.Confirm();

            await Harness.WaitUntilAsync(() => harness.Coordinator.Gate.Pending is { Kind: GateKind.ImageConfirmation });
            harness.Coordinator.Confirm();

            await Harness.AwaitAsync(runTask);
            Assert.Equal(0, harness.Recorder.Count("preview:start"));
        }

        [Fact]
        public async Task RoiPositioning_CarriesPlateSolvingOption() {
            Assert.True(await CapturePlatesolveFlagAsync(true));
            Assert.False(await CapturePlatesolveFlagAsync(false));
        }

        [Fact]
        public async Task TemplateValidation_ReportedAtLoadAndClearedWithTargets() {
            var harness = new Harness();
            var first = harness.MakeTarget("T1", 0);
            harness.TargetList.TargetsToLoad = new[] { first };
            harness.Templates.OnValidate = list => list
                .Select(target => new TemplateIssue { Target = target, TargetName = target.Name, TemplateName = "Speckle Wide" })
                .ToList();

            await harness.Coordinator.LoadListAsync("list.csv", CancellationToken.None);

            var issue = Assert.Single(harness.Coordinator.TemplateIssues);
            Assert.Same(first, issue.Target);
            Assert.Contains("Speckle Wide", issue.Message);
            Assert.Contains("not a saved sequence template", issue.Message);
            Assert.Equal(1, harness.Recorder.Count("templates:validate"));

            harness.Coordinator.ClearTargets();
            Assert.Empty(harness.Coordinator.TemplateIssues);
        }

        [Fact]
        public async Task LoadList_SeveralFiles_KeepsEveryListLoaded() {
            var harness = new Harness();

            harness.TargetList.TargetsToLoad = new[] { harness.MakeTarget("Alice1", 0), harness.MakeTarget("Alice2", 0) };
            await harness.Coordinator.LoadListAsync("alice.csv", CancellationToken.None);
            harness.TargetList.TargetsToLoad = new[] { harness.MakeTarget("Bob1", 0) };
            await harness.Coordinator.LoadListAsync("bob.csv", CancellationToken.None);
            harness.TargetList.TargetsToLoad = new[] { harness.MakeTarget("Carol1", 0), harness.MakeTarget("Carol2", 0) };
            await harness.Coordinator.LoadListAsync("carol.csv", CancellationToken.None);

            Assert.Equal(5, harness.Coordinator.Targets.Count);
            Assert.Equal(5, harness.Coordinator.Session.TargetCount);
            Assert.Equal(new[] { "alice.csv", "bob.csv", "carol.csv" }, harness.Coordinator.LoadedLists);
            Assert.Equal(new[] { "Alice1_Alice1", "Alice2_Alice2", "Bob1_Bob1", "Carol1_Carol1", "Carol2_Carol2" },
                         harness.Coordinator.Targets.Select(target => target.Name));

            harness.Coordinator.ClearTargets();
            Assert.Empty(harness.Coordinator.LoadedLists);
        }

        [Fact]
        public async Task LoadList_SameFileAgain_ReplacesOnlyThatList() {
            var harness = new Harness();

            harness.TargetList.TargetsToLoad = new[] { harness.MakeTarget("Alice1", 0), harness.MakeTarget("Alice2", 0) };
            await harness.Coordinator.LoadListAsync("alice.csv", CancellationToken.None);
            harness.TargetList.TargetsToLoad = new[] { harness.MakeTarget("Bob1", 0) };
            await harness.Coordinator.LoadListAsync("bob.csv", CancellationToken.None);

            harness.TargetList.TargetsToLoad = new[] { harness.MakeTarget("Alice1", 0) };
            await harness.Coordinator.LoadListAsync("ALICE.csv", CancellationToken.None);

            Assert.Equal(2, harness.Coordinator.Targets.Count);
            Assert.Equal(new[] { "Bob1_Bob1", "Alice1_Alice1" }, harness.Coordinator.Targets.Select(target => target.Name));
            Assert.Equal(2, harness.Coordinator.LoadedLists.Count);
        }

        [Fact]
        public async Task Capture_StopsAtAGateWhenTheFramesCannotBeSaved() {
            var harness = new Harness { AutoRun = true };
            harness.Scheduler.Picks.Enqueue(harness.MakeTarget("T1", 0));
            harness.Acquisition.OnEnsureFramesCanBeSaved = () => throw new Exception("The frames cannot be written to I:\\Speckle");

            await Harness.AwaitAsync(harness.Coordinator.RunAsync(harness.MakeOptions(), null, CancellationToken.None));

            Assert.Equal(0, harness.Recorder.Count("video:start"));
            Assert.Equal(1, harness.Coordinator.Session.TargetsSkipped);
        }

        [Fact]
        public async Task RemoveList_TakesOutOnlyThatListsTargets() {
            var harness = new Harness();
            harness.TargetList.TargetsToLoad = new[] { harness.MakeTarget("Alice1", 0), harness.MakeTarget("Alice2", 0) };
            await harness.Coordinator.LoadListAsync("alice.csv", CancellationToken.None);
            harness.TargetList.TargetsToLoad = new[] { harness.MakeTarget("Bob1", 0) };
            await harness.Coordinator.LoadListAsync("bob.csv", CancellationToken.None);

            harness.Coordinator.RemoveList("alice.csv");

            Assert.Equal(new[] { "Bob1_Bob1" }, harness.Coordinator.Targets.Select(target => target.Name));
            Assert.Equal(new[] { "bob.csv" }, harness.Coordinator.LoadedLists);
            Assert.Equal(1, harness.Coordinator.Session.TargetCount);
        }

        [Fact]
        public async Task LoadList_WhileImaging_LeavesTheRunAloneAndKeepsProgress() {
            var harness = new Harness { AutoRun = true };
            var first = harness.MakeTarget("T1", 0);
            harness.TargetList.TargetsToLoad = new[] { first };
            await harness.Coordinator.LoadListAsync("alice.csv", CancellationToken.None);
            harness.Scheduler.Picks.Enqueue(first);
            var videoStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            harness.Acquisition.OnRunRoiSeries = async (request, ct) => {
                videoStarted.TrySetResult(true);
                await release.Task;
                return new ExposureRunResult { FramesCaptured = request.TotalExposureCount, Fps = 10, PendingSaves = Task.CompletedTask };
            };

            var runTask = harness.Coordinator.RunAsync(harness.MakeOptions(), null, CancellationToken.None);
            await Harness.AwaitAsync(videoStarted.Task);

            harness.TargetList.TargetsToLoad = new[] { harness.MakeTarget("Bob1", 0) };
            await harness.Coordinator.LoadListAsync("bob.csv", CancellationToken.None);
            harness.TargetList.TargetsToLoad = new[] { harness.MakeTarget("T1", 0) };
            await harness.Coordinator.LoadListAsync("alice.csv", CancellationToken.None);

            Assert.Equal(WorkflowPhase.RunningVideoExposures, harness.Coordinator.Session.Phase);
            Assert.Equal(2, harness.Coordinator.Targets.Count);
            Assert.Contains(first, harness.Coordinator.Targets);
            Assert.Same(first, harness.Coordinator.Session.CurrentTarget);

            release.TrySetResult(true);
            await Harness.AwaitAsync(runTask);
            Assert.Equal(1, harness.Coordinator.Session.TargetsImaged);
            Assert.Equal(1, first.Completed_cycles);
        }

        [Fact]
        public async Task RemoveList_OfTheTargetBeingImaged_StopsThatTarget() {
            var harness = new Harness { AutoRun = true };
            var first = harness.MakeTarget("T1", 0);
            harness.TargetList.TargetsToLoad = new[] { first };
            await harness.Coordinator.LoadListAsync("alice.csv", CancellationToken.None);
            harness.Scheduler.Picks.Enqueue(first);
            var videoStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            harness.Acquisition.OnRunRoiSeries = async (request, ct) => {
                videoStarted.TrySetResult(true);
                await Task.Delay(Timeout.Infinite, ct);
                return new ExposureRunResult { FramesCaptured = request.TotalExposureCount, Fps = 10, PendingSaves = Task.CompletedTask };
            };

            var runTask = harness.Coordinator.RunAsync(harness.MakeOptions(), null, CancellationToken.None);
            await Harness.AwaitAsync(videoStarted.Task);

            harness.Coordinator.RemoveList("alice.csv");

            await Harness.AwaitAsync(runTask);
            Assert.Empty(harness.Coordinator.Targets);
            Assert.Equal(1, harness.Coordinator.Session.TargetsSkipped);
            Assert.Equal(0, first.Completed_cycles);
            Assert.Equal("list removed", first.Note2);
        }

        [Fact]
        public async Task LoadList_DropsQueuedTargetsThatAreNoLongerLoaded() {
            var harness = new Harness();
            var alice = harness.MakeTarget("Alice1", 0);
            harness.TargetList.TargetsToLoad = new[] { alice };
            await harness.Coordinator.LoadListAsync("alice.csv", CancellationToken.None);
            harness.Coordinator.QueueNext(alice);
            Assert.Single(harness.Coordinator.PinQueue);

            harness.TargetList.TargetsToLoad = new[] { harness.MakeTarget("Alice2", 0) };
            await harness.Coordinator.LoadListAsync("alice.csv", CancellationToken.None);

            Assert.Empty(harness.Coordinator.PinQueue);
        }

        [Fact]
        public async Task TemplateValidation_InspectedPerLeg() {
            var harness = new Harness { AutoRun = true };
            harness.Scheduler.Picks.Enqueue(harness.MakeTarget("T1", 0));

            await Harness.AwaitAsync(harness.Coordinator.RunAsync(harness.MakeOptions(), null, CancellationToken.None));

            Assert.Equal(1, harness.Recorder.Count("templates:inspect:T1"));
        }

        private static async Task<bool> CapturePlatesolveFlagAsync(bool usePlateSolving) {
            var harness = new Harness { AutoRun = true };
            harness.Scheduler.Picks.Enqueue(harness.MakeTarget("T1", 0));
            var flag = false;
            harness.Roi.OnLocate = request => {
                flag = request.PlatesolveFirst;
                return new RoiPositionResult { RoiX = 10, RoiY = 10 };
            };

            await Harness.AwaitAsync(harness.Coordinator.RunAsync(harness.MakeOptions(platesolveRoi: usePlateSolving), null, CancellationToken.None));
            return flag;
        }

        [Fact]
        public async Task StopAfterCurrentTarget_FinishesTargetThenStops() {
            var harness = new Harness { AutoRun = true };
            var first = harness.MakeTarget("T1", 0);
            var second = harness.MakeTarget("T2", 0);
            harness.Scheduler.Picks.Enqueue(first);
            harness.Scheduler.Picks.Enqueue(second);
            var videoStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            harness.Acquisition.OnRunRoiSeries = async (request, ct) => {
                videoStarted.TrySetResult(true);
                await release.Task;
                return new ExposureRunResult { FramesCaptured = request.TotalExposureCount, Fps = 10, PendingSaves = Task.CompletedTask };
            };

            var runTask = harness.Coordinator.RunAsync(harness.MakeOptions(), null, CancellationToken.None);
            await Harness.AwaitAsync(videoStarted.Task);

            harness.Coordinator.StopAfterCurrentTarget = true;
            release.TrySetResult(true);

            await Harness.AwaitAsync(runTask);
            Assert.Equal(WorkflowPhase.ListLoaded, harness.Coordinator.Session.Phase);
            Assert.Equal(1, harness.Coordinator.Session.TargetsImaged);
            Assert.Equal(1, harness.Scheduler.PickCount);
            Assert.Equal(1, first.Completed_cycles);
            Assert.Equal(0, second.Completed_cycles);
            Assert.False(harness.Coordinator.IsRunning);
        }

        [Fact]
        public async Task WaitingForWindow_HonoredAndImageNowOverrides() {
            var harness = new Harness { AutoRun = true };
            var first = harness.MakeTarget("T1", 0);
            first.ImageTime = harness.Now.AddMinutes(10);
            harness.Scheduler.Picks.Enqueue(first);
            TimeSpan? requestedDelay = null;
            var waiting = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var options = harness.MakeOptions(delay: async (wait, ct) => {
                requestedDelay = wait;
                waiting.TrySetResult(true);
                var released = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                using (ct.Register(() => released.TrySetResult(true))) {
                    await released.Task;
                }
                ct.ThrowIfCancellationRequested();
            });

            var runTask = harness.Coordinator.RunAsync(options, null, CancellationToken.None);
            await Harness.AwaitAsync(waiting.Task);

            Assert.Equal(WorkflowPhase.WaitingForWindow, harness.Coordinator.Session.Phase);
            Assert.Equal(first.ImageTime, harness.Coordinator.Session.WindowOpensAt);
            Assert.Equal(TimeSpan.FromMinutes(9), requestedDelay);

            harness.Coordinator.ImageNow();

            await Harness.AwaitAsync(runTask);
            Assert.Equal(WorkflowPhase.ListLoaded, harness.Coordinator.Session.Phase);
            Assert.Equal(1, harness.Coordinator.Session.TargetsImaged);
            Harness.AssertSubsequence(harness.Phases, WorkflowPhase.WaitingForWindow, WorkflowPhase.RunningVideoExposures, WorkflowPhase.ListLoaded);
        }

        [Fact]
        public async Task PinQueue_ConsumedBeforeScheduler() {
            var harness = new Harness { AutoRun = true };
            var first = harness.MakeTarget("T1", 0);
            var second = harness.MakeTarget("T2", 0);
            var third = harness.MakeTarget("T3", 0);
            harness.TargetList.TargetsToLoad = new[] { first, second, third };
            await harness.Coordinator.LoadListAsync("list.csv", CancellationToken.None);
            Assert.Equal(WorkflowPhase.ListLoaded, harness.Coordinator.Session.Phase);

            harness.Coordinator.QueueNext(third);
            harness.Coordinator.QueueNext(first);
            Assert.Equal(new[] { third, first }, harness.Coordinator.PinQueue);

            await Harness.AwaitAsync(harness.Coordinator.RunAsync(harness.MakeOptions(), null, CancellationToken.None));

            Assert.Equal(WorkflowPhase.ListLoaded, harness.Coordinator.Session.Phase);
            Assert.Equal(2, harness.Coordinator.Session.TargetsImaged);
            Assert.True(harness.Recorder.IndexOf("mark:T3") < harness.Recorder.IndexOf("mark:T1"), "Queued order must be honored");
            Assert.Equal(1, harness.Scheduler.PickCount);
            Assert.Equal(0, second.Completed_cycles);
            Assert.Empty(harness.Coordinator.PinQueue);
        }

        [Fact]
        public async Task PinQueue_SkipsIneligibleEntries() {
            var harness = new Harness { AutoRun = true };
            var first = harness.MakeTarget("T1", 0);
            first.Completed_cycles = 1;
            harness.TargetList.TargetsToLoad = new[] { first };
            await harness.Coordinator.LoadListAsync("list.csv", CancellationToken.None);
            harness.Coordinator.QueueNext(first);

            await Harness.AwaitAsync(harness.Coordinator.RunAsync(harness.MakeOptions(), null, CancellationToken.None));

            Assert.Equal(WorkflowPhase.ListLoaded, harness.Coordinator.Session.Phase);
            Assert.Equal(0, harness.Coordinator.Session.TargetsImaged);
            Assert.Equal(1, harness.Scheduler.PickCount);
        }

        [Fact]
        public async Task Pause_WhileGateWaits_StopsThePreviewLoopAndEngages() {
            var harness = new Harness { AutoRun = false };
            harness.Scheduler.Picks.Enqueue(harness.MakeTarget("T1", 0));

            var runTask = harness.Coordinator.RunAsync(harness.MakeOptions(), null, CancellationToken.None);
            await Harness.WaitUntilAsync(() => harness.Coordinator.Gate.Pending is { Kind: GateKind.SlewConfirmation });
            await Harness.WaitUntilAsync(() => harness.Recorder.Count("preview:start") == 1);

            await Harness.AwaitAsync(harness.Coordinator.PauseAsync(), 5000);
            Assert.True(harness.Coordinator.Session.IsHoldRequested);
            Assert.Equal(1, harness.Recorder.Count("preview:stop"));
            await Harness.WaitUntilAsync(() => harness.Coordinator.Session.IsHolding);
            await Task.Delay(200);
            Assert.Equal(1, harness.Recorder.Count("preview:start"));

            harness.Coordinator.Resume();
            await Harness.WaitUntilAsync(() => harness.Recorder.Count("preview:start") == 2);
            Assert.False(harness.Coordinator.Session.IsHoldRequested);

            harness.Coordinator.Confirm();
            await Harness.WaitUntilAsync(() => harness.Coordinator.Gate.Pending is { Kind: GateKind.ImageConfirmation });
            harness.Coordinator.Confirm();

            await Harness.AwaitAsync(runTask);
            Assert.Equal(WorkflowPhase.ListLoaded, harness.Coordinator.Session.Phase);
        }

        [Fact]
        public async Task CentringPreview_UsesTheExposureTheOperatorSet() {
            var harness = new Harness { AutoRun = false };
            harness.Camera.Info.Connected = true;
            harness.Scheduler.Picks.Enqueue(harness.MakeTarget("T1", 0));

            var runTask = harness.Coordinator.RunAsync(harness.MakeOptions(previewExposureTime: 2.5), null, CancellationToken.None);
            await Harness.WaitUntilAsync(() => harness.Recorder.Count("preview:start") == 1);

            Assert.Equal(new[] { 2.5 }, harness.Acquisition.PreviewExposureTimes);

            harness.Coordinator.RequestStop();
            await Harness.AwaitAsync(runTask);
        }

        [Fact]
        public async Task Pause_WhileGateWaits_KeepsTheRunPausedAtTheNextStep() {
            var harness = new Harness { AutoRun = false };
            harness.Scheduler.Picks.Enqueue(harness.MakeTarget("T1", 0));

            var runTask = harness.Coordinator.RunAsync(harness.MakeOptions(), null, CancellationToken.None);
            await Harness.WaitUntilAsync(() => harness.Coordinator.Gate.Pending is { Kind: GateKind.SlewConfirmation });

            await Harness.AwaitAsync(harness.Coordinator.PauseAsync(), 5000);
            harness.Coordinator.Confirm();
            await Task.Delay(300);
            Assert.Equal(0, harness.Roi.CallCount);

            harness.Coordinator.Resume();
            await Harness.WaitUntilAsync(() => harness.Coordinator.Gate.Pending is { Kind: GateKind.ImageConfirmation });
            harness.Coordinator.Confirm();

            await Harness.AwaitAsync(runTask);
            Assert.Equal(1, harness.Roi.CallCount);
            Assert.Equal(WorkflowPhase.ListLoaded, harness.Coordinator.Session.Phase);
        }

        [Fact]
        public async Task Pause_EngagesBetweenVideoFrames() {
            var harness = new Harness { AutoRun = true };
            var first = harness.MakeTarget("T1", 0);
            first.NExp = 50;
            harness.Scheduler.Picks.Enqueue(first);
            harness.Acquisition.OnRunRoiSeries = async (request, ct) => {
                for (var i = 1; i <= request.TotalExposureCount; i++) {
                    if (request.Pause != null) {
                        await request.Pause.WaitWhilePausedAsync(ct);
                    }
                    request.OnFrame?.Invoke(i);
                    await Task.Delay(10, ct);
                }
                return new ExposureRunResult { FramesCaptured = request.TotalExposureCount, Fps = 10, PendingSaves = Task.CompletedTask };
            };

            var runTask = harness.Coordinator.RunAsync(harness.MakeOptions(), null, CancellationToken.None);
            await Harness.WaitUntilAsync(() => harness.Coordinator.Session.FrameCount >= 3);

            await Harness.AwaitAsync(harness.Coordinator.PauseAsync());
            Assert.True(harness.Coordinator.Session.IsHolding);
            var frozenAt = harness.Coordinator.Session.FrameCount;
            await Task.Delay(200);
            Assert.InRange(harness.Coordinator.Session.FrameCount, frozenAt, frozenAt + 1);

            harness.Coordinator.Resume();
            await Harness.AwaitAsync(runTask);
            Assert.Equal(WorkflowPhase.ListLoaded, harness.Coordinator.Session.Phase);
            Assert.Equal(50, harness.Coordinator.Session.FrameCount);
        }

        [Fact]
        public async Task SkipReference_ArmedDuringPrimaryLeg_BypassesReferenceLeg() {
            var harness = new Harness { AutoRun = true };
            var first = harness.MakeTarget("T1");
            harness.Scheduler.Picks.Enqueue(first);
            var videoStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            harness.Acquisition.OnRunRoiSeries = async (request, ct) => {
                videoStarted.TrySetResult(true);
                await release.Task;
                return new ExposureRunResult { FramesCaptured = request.TotalExposureCount, Fps = 10, PendingSaves = Task.CompletedTask };
            };

            var runTask = harness.Coordinator.RunAsync(harness.MakeOptions(), null, CancellationToken.None);
            await Harness.AwaitAsync(videoStarted.Task);

            harness.Coordinator.SkipReference();
            release.TrySetResult(true);

            await Harness.AwaitAsync(runTask);
            Assert.Equal(WorkflowPhase.ListLoaded, harness.Coordinator.Session.Phase);
            Assert.Equal(1, harness.Recorder.Count("video:start"));
            Assert.Equal(1, first.Completed_cycles);
            Assert.Equal(0, first.Completed_ref_cycles);
        }

        [Fact]
        public async Task JumpToReference_DuringPrimaryVideoExposures_LeavesTheTargetLeg() {
            var harness = new Harness { AutoRun = true };
            var first = harness.MakeTarget("T1");
            harness.Scheduler.Picks.Enqueue(first);
            var targetVideoStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var calls = 0;
            harness.Acquisition.OnRunRoiSeries = async (request, ct) => {
                if (Interlocked.Increment(ref calls) == 1) {
                    targetVideoStarted.TrySetResult(true);
                    await Task.Delay(Timeout.Infinite, ct);
                }
                return new ExposureRunResult { FramesCaptured = request.TotalExposureCount, Fps = 10, PendingSaves = Task.CompletedTask };
            };

            var runTask = harness.Coordinator.RunAsync(harness.MakeOptions(), null, CancellationToken.None);
            await Harness.AwaitAsync(targetVideoStarted.Task);

            harness.Coordinator.JumpToStage(WorkflowPhase.AwaitingSlewConfirmation, true);

            await Harness.AwaitAsync(runTask);
            Assert.Equal(WorkflowPhase.ListLoaded, harness.Coordinator.Session.Phase);
            Assert.Equal(2, harness.Recorder.Count("video:start"));
            Assert.Equal(0, first.Completed_cycles);
            Assert.Equal(1, first.Completed_ref_cycles);
        }

        [Fact]
        public async Task JumpToTarget_DuringReferenceLeg_ImagesTheTargetAgainThenTheReference() {
            var harness = new Harness { AutoRun = true };
            var first = harness.MakeTarget("T1");
            harness.Scheduler.Picks.Enqueue(first);
            var referenceVideoStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var calls = 0;
            harness.Acquisition.OnRunRoiSeries = async (request, ct) => {
                if (Interlocked.Increment(ref calls) == 2) {
                    referenceVideoStarted.TrySetResult(true);
                    await Task.Delay(Timeout.Infinite, ct);
                }
                return new ExposureRunResult { FramesCaptured = request.TotalExposureCount, Fps = 10, PendingSaves = Task.CompletedTask };
            };

            var runTask = harness.Coordinator.RunAsync(harness.MakeOptions(), null, CancellationToken.None);
            await Harness.AwaitAsync(referenceVideoStarted.Task);

            harness.Coordinator.JumpToStage(WorkflowPhase.AwaitingSlewConfirmation, false);

            await Harness.AwaitAsync(runTask);
            Assert.Equal(WorkflowPhase.ListLoaded, harness.Coordinator.Session.Phase);
            Assert.Equal(1, harness.Coordinator.Session.TargetsImaged);
            Assert.Equal(4, harness.Recorder.Count("video:start"));
            Assert.Equal(1, first.Completed_cycles);
            Assert.Equal(1, first.Completed_ref_cycles);
            Assert.Equal(new[] { false, true, false, true }, harness.Acquisition.Requests.Select(request => request.IsReference));
            Assert.Equal(new[] { 1, 1, 2, 2 }, harness.Acquisition.Requests.Select(request => request.SpeckleRun));
        }

        [Fact]
        public async Task JumpToStage_WithoutAReferenceStar_DoesNotLeaveTheTargetLeg() {
            var harness = new Harness { AutoRun = true };
            var first = harness.MakeTarget("T1", 0);
            harness.Scheduler.Picks.Enqueue(first);
            var targetVideoStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            harness.Acquisition.OnRunRoiSeries = async (request, ct) => {
                targetVideoStarted.TrySetResult(true);
                await release.Task;
                return new ExposureRunResult { FramesCaptured = request.TotalExposureCount, Fps = 10, PendingSaves = Task.CompletedTask };
            };

            var runTask = harness.Coordinator.RunAsync(harness.MakeOptions(), null, CancellationToken.None);
            await Harness.AwaitAsync(targetVideoStarted.Task);

            harness.Coordinator.JumpToStage(WorkflowPhase.AwaitingSlewConfirmation, true);
            release.TrySetResult(true);

            await Harness.AwaitAsync(runTask);
            Assert.Equal(WorkflowPhase.ListLoaded, harness.Coordinator.Session.Phase);
            Assert.Equal(1, harness.Recorder.Count("video:start"));
            Assert.Equal(1, first.Completed_cycles);
            Assert.Equal(0, first.Completed_ref_cycles);
        }

        [Fact]
        public async Task FailedReferenceResolution_FullAuto_SkipsReferenceLegOnly() {
            var harness = new Harness { AutoRun = true };
            var first = harness.MakeTarget("T1");
            harness.Scheduler.Picks.Enqueue(first);
            harness.ReferenceStars.OnResolve = target => throw new InvalidOperationException("simbad down");

            await Harness.AwaitAsync(harness.Coordinator.RunAsync(harness.MakeOptions(), null, CancellationToken.None));

            Assert.Equal(WorkflowPhase.ListLoaded, harness.Coordinator.Session.Phase);
            Assert.Equal(1, harness.Coordinator.Session.TargetsImaged);
            Assert.Equal(1, harness.Recorder.Count("video:start"));
            Assert.Equal(1, first.Completed_cycles);
            Assert.Equal(0, first.Completed_ref_cycles);
        }
    }
}
