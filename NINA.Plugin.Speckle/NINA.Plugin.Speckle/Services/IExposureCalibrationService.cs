using NINA.Core.Model;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Plugin.Speckle.Services {

    public interface IExposureCalibrationService {

        Task<double> FindRoiExposureTimeAsync(ExposureCalibrationRequest request, IProgress<ApplicationStatus> progress, CancellationToken ct);

        double EstimateExposureTime(ExposureEstimateRequest request);
    }
}
