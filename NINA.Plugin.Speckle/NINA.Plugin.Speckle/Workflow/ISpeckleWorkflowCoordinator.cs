using NINA.Core.Model;
using NINA.Plugin.Speckle.Model;
using NINA.Plugin.Speckle.Services;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Plugin.Speckle.Workflow {

    public interface ISpeckleWorkflowCoordinator {

        SpeckleWorkflowSession Session { get; }

        WorkflowGate Gate { get; }

        bool IsRunning { get; }

        IReadOnlyList<SpeckleTarget> Targets { get; }

        IReadOnlyList<SpeckleTarget> PinQueue { get; }

        IReadOnlyList<TemplateIssue> TemplateIssues { get; }

        bool StopAfterCurrentTarget { get; set; }

        event EventHandler<WorkflowPhaseChangedEventArgs> PhaseChanged;

        event EventHandler PendingChanged;

        event EventHandler<RoiSeriesProgress> VideoExposuresProgress;

        event EventHandler TargetsChanged;

        event EventHandler QueueChanged;

        event EventHandler CapturePlanChanged;

        IReadOnlyList<string> LoadedLists { get; }

        Task LoadListAsync(string csvPath, CancellationToken ct);

        SpeckleTarget AddTargetByHand(string name, double ra2000, double dec2000);

        Task<IReadOnlyList<ReferenceStar>> FindBrightStarsInSlitAsync(CancellationToken ct);

        void RemoveList(string csvPath);

        void ClearTargets();

        IReadOnlyList<TemplateIssue> ValidateTemplates();

        WorkflowRunOptions BuildRunOptions();

        Task RunAsync(WorkflowRunOptions options, IProgress<ApplicationStatus> progress, CancellationToken ct);



        void Confirm();

        void RetryStep();

        void SkipStep();

        void SkipCurrentTarget();

        void SkipReference();

        void SetSkipReference(bool skip);

        Task PauseAsync();

        void Resume();

        void RequestStop();

        void ImageNow();

        void FinishVideoExposuresEarly();

        void OverrideExposureTime(double seconds);

        IReadOnlyList<string> RefreshAvailableFilters();

        bool AddCaptureEntry(string filter, double exposureTime, int frameCount);

        bool RemoveCaptureEntry(CaptureEntry entry);

        bool MoveCaptureEntry(CaptureEntry entry, int newIndex);

        Task TryCaptureEntryAsync(CaptureEntry entry, CancellationToken ct);

        bool SetCaptureEntryFilter(CaptureEntry entry, string filter);

        bool SetCaptureEntryExposureTime(CaptureEntry entry, double seconds);

        bool SetCaptureEntryFrameCount(CaptureEntry entry, int frames);

        void OverrideReference(ReferenceStar star);

        void JumpToStage(WorkflowPhase phase, bool toReferenceLeg);

        void QueueNext(SpeckleTarget target);

        void RemoveFromQueue(SpeckleTarget target);

        void MoveInQueue(SpeckleTarget target, int newIndex);

        Task SwitchLightPathAsync(LightPath path, IProgress<ApplicationStatus> progress, CancellationToken ct);
    }
}
