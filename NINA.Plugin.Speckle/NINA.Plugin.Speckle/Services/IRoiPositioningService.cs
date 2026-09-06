using NINA.Core.Model;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Plugin.Speckle.Services {

    public interface IRoiPositioningService {

        Task<RoiPositionResult> LocateTargetAsync(RoiPositionRequest request, IProgress<ApplicationStatus> progress, CancellationToken ct);
    }
}
