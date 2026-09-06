using NINA.Core.Model.Equipment;
using NINA.Equipment.Equipment.MyFilterWheel;
using NINA.Plugin.Speckle.Model;
using NINA.Plugin.Speckle.Services;
using NINA.Plugin.Speckle.Workflow;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Plugin.Speckle.Test {

    public class Harness {
        private readonly List<WorkflowPhase> phases = new List<WorkflowPhase>();

        public Harness() {
            Telescope = new FakeTelescopeMediator(Recorder);
            FilterWheel = new FakeFilterWheelMediator(Recorder);
            TargetList = new FakeTargetListService(Recorder);
            Scheduler = new FakeSchedulerService(Recorder);
            ReferenceStars = new FakeReferenceStarService(Recorder);
            Roi = new FakeRoiPositioningService(Recorder);
            Calibration = new FakeExposureCalibrationService(Recorder);
            Acquisition = new FakeAcquisitionService(Recorder);
            LightPath = new FakeLightPathService(Recorder);
            Templates = new FakeTemplateValidationService(Recorder);
            Coordinator = new SpeckleWorkflowCoordinator(
                null,
                Telescope,
                Camera,
                FilterWheel,
                TargetList,
                Scheduler,
                ReferenceStars,
                new TargetPlanner(),
                Roi,
                Calibration,
                Acquisition,
                LightPath,
                Templates,
                Options,
                null,
                null);
            Coordinator.PhaseChanged += (sender, args) => {
                lock (phases) {
                    phases.Add(args.Phase);
                }
            };
        }

        public Recorder Recorder { get; } = new Recorder();
        public FakeTelescopeMediator Telescope { get; }
        public FakeCameraMediator Camera { get; } = new FakeCameraMediator();
        public FakeFilterWheelMediator FilterWheel { get; }
        public FakeTargetListService TargetList { get; }
        public FakeSchedulerService Scheduler { get; }
        public FakeReferenceStarService ReferenceStars { get; }
        public FakeRoiPositioningService Roi { get; }
        public FakeExposureCalibrationService Calibration { get; }
        public FakeAcquisitionService Acquisition { get; }
        public FakeLightPathService LightPath { get; }
        public FakeTemplateValidationService Templates { get; }
        public FakeOptionsProvider Options { get; } = new FakeOptionsProvider();
        public SpeckleWorkflowCoordinator Coordinator { get; }
        public bool AutoRun { get; set; }
        public DateTime Now { get; set; } = new DateTime(2026, 8, 4, 22, 0, 0);

        public IReadOnlyList<WorkflowPhase> Phases {
            get {
                lock (phases) {
                    return phases.ToList();
                }
            }
        }

        public WorkflowRunOptions MakeOptions(
            bool dualCamera = false,
            bool autoSkipFailedReference = true,
            bool platesolveRoi = true,
            double previewExposureTime = 1,
            Func<TimeSpan, CancellationToken, Task> delay = null) {
            return new WorkflowRunOptions {
                PreviewExposureTime = previewExposureTime,
                ReadAutomaticallyRun = () => AutoRun,
                Clock = () => Now,
                SnapshotDirectory = "snapshots",
                DualCameraSetup = dualCamera,
                PlatesolveRoi = platesolveRoi,
                AutoSkipFailedReference = autoSkipFailedReference,
                Delay = delay ?? Task.Delay,
                InputTargetFactory = plan => null,
                ResolveFilter = name => string.IsNullOrWhiteSpace(name) ? null : new FilterInfo(name, 0, 0)
            };
        }

        public void ConnectFilterWheel() {
            FilterWheel.Info = new FilterWheelInfo { Connected = true };
        }

        public IReadOnlyList<string> FilterChanges {
            get {
                return Recorder.Entries
                    .Where(entry => entry.StartsWith("filter:", StringComparison.Ordinal))
                    .Select(entry => entry.Substring("filter:".Length))
                    .ToList();
            }
        }

        public SpeckleTarget MakeTarget(string name, int getRef = 1) {
            return new SpeckleTarget {
                Name1 = name,
                Name2 = name,
                Type = "M",
                Proj = "P",
                Obs = "O",
                RA2000 = 10,
                Dec2000 = 20,
                Cycles = 1,
                Nights = 1,
                GetRef = getRef,
                NExp = 5,
                ImageTime = Now
            };
        }

        public static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 20000) {
            var watch = Stopwatch.StartNew();
            while (!condition()) {
                if (watch.ElapsedMilliseconds > timeoutMs) {
                    throw new TimeoutException("Condition was not met within " + timeoutMs + " ms");
                }
                await Task.Delay(10);
            }
        }

        public static async Task AwaitAsync(Task task, int timeoutMs = 40000) {
            var completed = await Task.WhenAny(task, Task.Delay(timeoutMs));
            if (completed != task) {
                throw new TimeoutException("Task did not complete within " + timeoutMs + " ms");
            }
            await task;
        }

        public static void AssertSubsequence(IReadOnlyList<WorkflowPhase> actual, params WorkflowPhase[] expected) {
            var index = 0;
            foreach (var phase in actual) {
                if (index < expected.Length && phase == expected[index]) {
                    index++;
                }
            }
            if (index != expected.Length) {
                throw new Xunit.Sdk.XunitException(
                    "Expected phase subsequence [" + string.Join(", ", expected) + "] not found in [" + string.Join(", ", actual) + "]; matched " + index + " of " + expected.Length);
            }
        }
    }
}
