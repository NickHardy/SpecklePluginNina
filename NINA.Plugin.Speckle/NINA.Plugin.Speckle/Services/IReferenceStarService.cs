using NINA.Core.Model;
using NINA.Plugin.Speckle.Model;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Plugin.Speckle.Services {

    public interface IReferenceStarService {

        IReadOnlyList<ReferenceStar> ReferenceStars { get; }

        IReadOnlyList<GaiaReferenceStar> GaiaReferenceStars { get; }

        Task EnsureReferenceListsLoadedAsync(CancellationToken ct);

        Task<ReferenceStarResult> ResolveAsync(SpeckleTarget target, IReadOnlyList<SpeckleTarget> allTargets, ReferenceStarQueryOptions opts, IProgress<ApplicationStatus> progress, CancellationToken ct);
    }
}
