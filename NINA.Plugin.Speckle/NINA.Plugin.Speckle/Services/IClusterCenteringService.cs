using NINA.Core.Model;
using NINA.PlateSolving;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Plugin.Speckle.Services {

    public interface IClusterCenteringService {

        Task<PlateSolveResult> CenterViaStarClusterAsync(ClusterCenteringRequest request, IProgress<ApplicationStatus> progress, IProgress<PlateSolveProgress> solveStatus, CancellationToken ct);
    }
}
