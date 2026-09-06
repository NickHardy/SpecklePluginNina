using NINA.Core.Model;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Plugin.Speckle.Services {

    public interface ISpeckleAcquisitionService {

        void EnsureFramesCanBeSaved();

        Task<ExposureRunResult> RunRoiSeriesAsync(RoiSeriesRequest request, IProgress<ApplicationStatus> progress, CancellationToken ct);

        Task TakeSingleAsync(SingleExposureRequest request, IProgress<ApplicationStatus> progress, CancellationToken ct);

        Task RunPreviewLoopAsync(PreviewLoopRequest request, IProgress<ApplicationStatus> progress, CancellationToken ct);
    }
}
