using NINA.Astrometry;
using NINA.Core.Model;
using NINA.Plugin.Speckle.Model;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Plugin.Speckle.Services {

    public interface ITargetListService {

        NighttimeData GetNighttimeData(DateTime now);

        Task<IReadOnlyList<SpeckleTarget>> LoadTargetsAsync(string csvPath, TargetListDefaults defaults, bool ignoreLimits, CancellationToken ct);

        IReadOnlyList<SpeckleTarget> Order(IEnumerable<SpeckleTarget> targets);

        List<AltTime> ComputeAltList(Coordinates coords, ObserverInfo observer, CustomHorizon horizon, double stepHours);

        void ApplyVisibilityWindows(SpeckleTarget target, NighttimeData night, SchedulingOptions opts);

        Task SaveSnapshotAsync(IEnumerable<SpeckleTarget> targets, string directory);

        void WriteTargets(IEnumerable<SpeckleTarget> targets, string csvPath);

        Task<ReferenceStarExportResult> ExportWithReferenceStarsAsync(IReadOnlyList<SpeckleTarget> targets, ExportFormat format, string directory, IProgress<ApplicationStatus> progress, CancellationToken ct);
    }
}
