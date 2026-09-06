using NINA.Core.Model.Equipment;
using NINA.Plugin.Speckle.Model;
using NINA.Plugin.Speckle.Services;
using NINA.Plugin.Speckle.Workflow;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace NINA.Plugin.Speckle.Test {

    public class CapturePlanTests {

        [Fact]
        public async Task SingleFilterList_RunsExactlyOneSeriesAsBefore() {
            var harness = new Harness { AutoRun = true };
            harness.ConnectFilterWheel();
            var first = MakeTarget(harness, "T1", "G", 0.5, 7);
            harness.Scheduler.Picks.Enqueue(first);

            await Harness.AwaitAsync(harness.Coordinator.RunAsync(harness.MakeOptions(), null, CancellationToken.None));

            var request = Assert.Single(harness.Acquisition.Requests);
            Assert.Equal(0.5, request.ExposureTime);
            Assert.Equal(7, request.TotalExposureCount);
            Assert.Equal(new[] { "G" }, harness.FilterChanges);
            Assert.Equal(0, harness.Calibration.CallCount);
            Assert.Equal(1, harness.Coordinator.Session.CaptureEntryCount);
            Assert.Equal(1, first.Completed_cycles);

            var plan = harness.Coordinator.Session.CurrentPlan;
            var entry = Assert.Single(plan.CaptureEntries);
            Assert.Equal("G", entry.Filter);
            Assert.Equal(0.5, entry.ExposureTime);
            Assert.Equal(7, entry.FrameCount);
            Assert.Equal("G", plan.FilterName);
            Assert.Equal(0.5, plan.Exp);
            Assert.Equal(7, plan.NExp);
        }

        [Fact]
        public async Task ThreeEntryPlan_RunsEverySeriesInOrder() {
            var harness = new Harness { AutoRun = false };
            harness.ConnectFilterWheel();
            var first = MakeTarget(harness, "T1", "G", 0.5, 7);
            harness.Scheduler.Picks.Enqueue(first);

            var runTask = harness.Coordinator.RunAsync(harness.MakeOptions(), null, CancellationToken.None);
            await WaitForGateAsync(harness, GateKind.SlewConfirmation);

            Assert.True(harness.Coordinator.AddCaptureEntry("R", 0.25, 4000));
            Assert.True(harness.Coordinator.AddCaptureEntry("I", 1.5, 300));
            Assert.Equal(3, harness.Coordinator.Session.CaptureEntries.Count);
            harness.Coordinator.Confirm();

            await WaitForGateAsync(harness, GateKind.ImageConfirmation);
            harness.Coordinator.Confirm();

            await Harness.AwaitAsync(runTask);

            var requests = harness.Acquisition.Requests;
            Assert.Equal(3, requests.Count);
            Assert.Equal(new[] { 0.5, 0.25, 1.5 }, requests.Select(request => request.ExposureTime));
            Assert.Equal(new[] { 7, 4000, 300 }, requests.Select(request => request.TotalExposureCount));
            Assert.Equal(new[] { "G", "R", "I" }, harness.FilterChanges);
            Assert.Equal(3, harness.Coordinator.Session.CaptureEntryNumber);
            Assert.Equal(3, harness.Coordinator.Session.CaptureEntryCount);
            Assert.Equal(1, first.Completed_cycles);
            Assert.Equal(4307, harness.Coordinator.Session.TotalFrames);
        }

        [Fact]
        public async Task EditAtImageGate_IsHonoredWhenTheSeriesStarts() {
            var harness = new Harness { AutoRun = false };
            harness.ConnectFilterWheel();
            harness.Scheduler.Picks.Enqueue(MakeTarget(harness, "T1", "G", 0.5, 7));

            var runTask = harness.Coordinator.RunAsync(harness.MakeOptions(), null, CancellationToken.None);
            await WaitForGateAsync(harness, GateKind.SlewConfirmation);
            harness.Coordinator.Confirm();

            await WaitForGateAsync(harness, GateKind.ImageConfirmation);
            var entry = Assert.Single(harness.Coordinator.Session.CaptureEntries);
            Assert.True(harness.Coordinator.SetCaptureEntryFilter(entry, "R"));
            Assert.True(harness.Coordinator.SetCaptureEntryExposureTime(entry, 0.75));
            Assert.True(harness.Coordinator.SetCaptureEntryFrameCount(entry, 1234));
            harness.Coordinator.Confirm();

            await Harness.AwaitAsync(runTask);

            var request = Assert.Single(harness.Acquisition.Requests);
            Assert.Equal(0.75, request.ExposureTime);
            Assert.Equal(1234, request.TotalExposureCount);
            Assert.Equal(new[] { "G", "R" }, harness.FilterChanges);
        }

        [Fact]
        public async Task SkipTarget_MidPlan_AbandonsTheRemainingEntries() {
            var harness = new Harness { AutoRun = false };
            harness.ConnectFilterWheel();
            var first = MakeTarget(harness, "T1", "G", 0.5, 7);
            harness.Scheduler.Picks.Enqueue(first);
            var firstSeriesStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            harness.Acquisition.OnRunRoiSeries = async (request, ct) => {
                firstSeriesStarted.TrySetResult(true);
                await Task.Delay(Timeout.Infinite, ct);
                return null;
            };

            var runTask = harness.Coordinator.RunAsync(harness.MakeOptions(), null, CancellationToken.None);
            await WaitForGateAsync(harness, GateKind.SlewConfirmation);
            harness.Coordinator.AddCaptureEntry("R", 0.25, 4000);
            harness.Coordinator.AddCaptureEntry("I", 1.5, 300);
            harness.Coordinator.Confirm();
            await WaitForGateAsync(harness, GateKind.ImageConfirmation);
            harness.Coordinator.Confirm();
            await Harness.AwaitAsync(firstSeriesStarted.Task);

            Assert.False(harness.Coordinator.AddCaptureEntry("V", 1, 10));
            Assert.True(harness.Coordinator.Session.CapturePlanLocked);

            harness.Coordinator.SkipCurrentTarget();
            await Harness.AwaitAsync(runTask);

            Assert.Equal(WorkflowPhase.ListLoaded, harness.Coordinator.Session.Phase);
            Assert.Single(harness.Acquisition.Requests);
            Assert.Equal(1, harness.Coordinator.Session.TargetsSkipped);
            Assert.Equal(0, harness.Coordinator.Session.TargetsImaged);
            Assert.Equal(0, first.Completed_cycles);
            Assert.Equal("skipped-by-operator", first.Note2);
        }

        [Fact]
        public async Task Pause_InsideAnEntry_StopsBetweenFrames() {
            var harness = new Harness { AutoRun = false };
            harness.ConnectFilterWheel();
            harness.Scheduler.Picks.Enqueue(MakeTarget(harness, "T1", "G", 0.5, 3));
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
            await WaitForGateAsync(harness, GateKind.SlewConfirmation);
            harness.Coordinator.AddCaptureEntry("R", 0.25, 50);
            harness.Coordinator.Confirm();
            await WaitForGateAsync(harness, GateKind.ImageConfirmation);
            harness.Coordinator.Confirm();

            await Harness.WaitUntilAsync(() => harness.Coordinator.Session.CaptureEntryNumber == 2 && harness.Coordinator.Session.FrameCount >= 3);
            await Harness.AwaitAsync(harness.Coordinator.PauseAsync());

            Assert.True(harness.Coordinator.Session.IsHolding);
            var frozenAt = harness.Coordinator.Session.FrameCount;
            await Task.Delay(200);
            Assert.InRange(harness.Coordinator.Session.FrameCount, frozenAt, frozenAt + 1);
            Assert.Equal(2, harness.Acquisition.Requests.Count);

            harness.Coordinator.Resume();
            await Harness.AwaitAsync(runTask);

            Assert.Equal(WorkflowPhase.ListLoaded, harness.Coordinator.Session.Phase);
            Assert.Equal(50, harness.Coordinator.Session.FrameCount);
            Assert.Equal(53, harness.Coordinator.Session.TotalFrames);
        }

        [Fact]
        public async Task FailureInSecondEntry_GoesThroughTheFaultLadder() {
            var harness = new Harness { AutoRun = false };
            harness.ConnectFilterWheel();
            var first = MakeTarget(harness, "T1", "G", 0.5, 7);
            harness.Scheduler.Picks.Enqueue(first);
            harness.Acquisition.OnRunRoiSeries = (request, ct) => {
                if (request.TotalExposureCount == 4000) {
                    throw new InvalidOperationException("camera dropped out");
                }
                return Task.FromResult(new ExposureRunResult { FramesCaptured = request.TotalExposureCount, Fps = 10, PendingSaves = Task.CompletedTask });
            };

            var runTask = harness.Coordinator.RunAsync(harness.MakeOptions(), null, CancellationToken.None);
            await WaitForGateAsync(harness, GateKind.SlewConfirmation);
            harness.Coordinator.AddCaptureEntry("R", 0.25, 4000);
            harness.Coordinator.AddCaptureEntry("I", 1.5, 300);
            harness.Coordinator.Confirm();
            await WaitForGateAsync(harness, GateKind.ImageConfirmation);
            harness.Coordinator.Confirm();

            await WaitForGateAsync(harness, GateKind.Error);
            Assert.Equal(WorkflowPhase.RunningVideoExposures, harness.Coordinator.Gate.Pending.Phase);
            Assert.Equal(WorkflowPhase.Faulted, harness.Coordinator.Session.Phase);
            harness.Coordinator.RetryStep();

            await Harness.WaitUntilAsync(() => harness.Coordinator.Gate.Pending is { Kind: GateKind.Error, Attempt: 1 });
            harness.Coordinator.Gate.SkipTarget();

            await Harness.AwaitAsync(runTask);

            Assert.Equal(WorkflowPhase.ListLoaded, harness.Coordinator.Session.Phase);
            Assert.Equal(3, harness.Acquisition.Requests.Count);
            Assert.Equal(0, harness.Coordinator.Session.TargetsImaged);
            Assert.Equal(1, harness.Coordinator.Session.TargetsSkipped);
            Assert.Equal(0, first.Completed_cycles);
            Assert.Equal(0, harness.TargetList.SnapshotCount);
            Assert.Equal("skipped-after-error", first.Note2);
            Assert.False(first.ImageTarget);
        }

        [Fact]
        public async Task CapturePlanEdits_AreGuardedAndReversible() {
            var harness = new Harness { AutoRun = false };
            harness.ConnectFilterWheel();
            harness.Scheduler.Picks.Enqueue(MakeTarget(harness, "T1", "G", 0.5, 7));
            var changes = 0;
            harness.Coordinator.CapturePlanChanged += (sender, args) => Interlocked.Increment(ref changes);

            var runTask = harness.Coordinator.RunAsync(harness.MakeOptions(), null, CancellationToken.None);
            await WaitForGateAsync(harness, GateKind.SlewConfirmation);

            var entries = harness.Coordinator.Session.CaptureEntries;
            Assert.False(harness.Coordinator.RemoveCaptureEntry(entries[0]));
            Assert.True(harness.Coordinator.AddCaptureEntry("R", 0.25, 4000));
            Assert.True(harness.Coordinator.MoveCaptureEntry(entries[1], 0));
            Assert.Equal(new[] { "R", "G" }, entries.Select(entry => entry.Filter));
            Assert.True(harness.Coordinator.RemoveCaptureEntry(entries[0]));
            Assert.Equal(new[] { "G" }, entries.Select(entry => entry.Filter));
            Assert.True(changes > 0);

            harness.Coordinator.Confirm();
            await WaitForGateAsync(harness, GateKind.ImageConfirmation);
            harness.Coordinator.Confirm();
            await Harness.AwaitAsync(runTask);

            Assert.Single(harness.Acquisition.Requests);
        }

        [Fact]
        public async Task TryingFiltersAtTheImageGate_PutsTheWheelBackBeforeTheCaptureStarts() {
            var harness = new Harness { AutoRun = false };
            harness.ConnectFilterWheel();
            harness.Scheduler.Picks.Enqueue(MakeTarget(harness, "T1", "G", 0.5, 7));
            var filterWhenTheSeriesStarted = new List<string>();
            harness.Acquisition.OnRunRoiSeries = (request, ct) => {
                filterWhenTheSeriesStarted.Add(harness.FilterWheel.Info.SelectedFilter?.Name);
                return Task.FromResult(new ExposureRunResult { FramesCaptured = request.TotalExposureCount, Fps = 10, PendingSaves = Task.CompletedTask });
            };

            var runTask = harness.Coordinator.RunAsync(harness.MakeOptions(), null, CancellationToken.None);
            await WaitForGateAsync(harness, GateKind.SlewConfirmation);
            Assert.True(harness.Coordinator.AddCaptureEntry("R", 0.25, 400));
            harness.Coordinator.Confirm();

            await WaitForGateAsync(harness, GateKind.ImageConfirmation);
            var entries = harness.Coordinator.Session.CaptureEntries;
            Assert.Equal("G", harness.FilterWheel.Info.SelectedFilter?.Name);
            await harness.Coordinator.TryCaptureEntryAsync(entries[1], CancellationToken.None);
            Assert.Equal("R", harness.FilterWheel.Info.SelectedFilter.Name);

            harness.Coordinator.Confirm();
            await Harness.AwaitAsync(runTask);

            Assert.Equal(new[] { "G", "R" }, filterWhenTheSeriesStarted);
            Assert.Equal(new[] { 0.5, 0.25 }, harness.Acquisition.Requests.Select(request => request.ExposureTime));
        }

        [Fact]
        public async Task TryingAFilterThenStopping_StillPutsTheWheelBack() {
            var harness = new Harness { AutoRun = false };
            harness.ConnectFilterWheel();
            harness.Scheduler.Picks.Enqueue(MakeTarget(harness, "T1", "G", 0.5, 7));

            var runTask = harness.Coordinator.RunAsync(harness.MakeOptions(), null, CancellationToken.None);
            await WaitForGateAsync(harness, GateKind.SlewConfirmation);
            Assert.True(harness.Coordinator.AddCaptureEntry("R", 0.25, 400));
            harness.Coordinator.Confirm();

            await WaitForGateAsync(harness, GateKind.ImageConfirmation);
            await harness.Coordinator.TryCaptureEntryAsync(harness.Coordinator.Session.CaptureEntries[1], CancellationToken.None);
            Assert.Equal("R", harness.FilterWheel.Info.SelectedFilter.Name);

            harness.Coordinator.RequestStop();
            await Harness.AwaitAsync(runTask);

            Assert.Equal("G", harness.FilterWheel.Info.SelectedFilter.Name);
            Assert.Empty(harness.Acquisition.Requests);
        }

        [Fact]
        public void WhenTheListNamesNoFilter_TheDefaultFiltersBecomeTheCapturePlan() {
            var target = new SpeckleTarget { Name1 = "T1", Exp = 0.02, NExp = 500 };
            var defaults = new TargetPlanDefaults { DefaultFilters = new[] { "SloanR", "SloanI" } };

            var plan = new TargetPlanner().BuildPlan(target, false, defaults);

            Assert.Equal(new[] { "SloanR", "SloanI" }, plan.CaptureEntries.Select(entry => entry.Filter));
            Assert.All(plan.CaptureEntries, entry => Assert.Equal(0.02, entry.ExposureTime));
            Assert.All(plan.CaptureEntries, entry => Assert.Equal(500, entry.FrameCount));
        }

        [Fact]
        public void AFilterNamedByTheListBeatsTheDefaults() {
            var target = new SpeckleTarget { Name1 = "T1", Filter = "Luminance", Exp = 0.02, NExp = 500 };
            var defaults = new TargetPlanDefaults { DefaultFilters = new[] { "SloanR", "SloanI" } };

            var plan = new TargetPlanner().BuildPlan(target, false, defaults);

            Assert.Equal(new[] { "Luminance" }, plan.CaptureEntries.Select(entry => entry.Filter));
        }

        private static SpeckleTarget MakeTarget(Harness harness, string name, string filter, double exposureTime, int frames) {
            var target = harness.MakeTarget(name, 0);
            target.Filter = filter;
            target.Exp = exposureTime;
            target.NExp = frames;
            return target;
        }

        private static Task WaitForGateAsync(Harness harness, GateKind kind) {
            return Harness.WaitUntilAsync(() => harness.Coordinator.Gate.Pending != null && harness.Coordinator.Gate.Pending.Kind == kind);
        }
    }
}
