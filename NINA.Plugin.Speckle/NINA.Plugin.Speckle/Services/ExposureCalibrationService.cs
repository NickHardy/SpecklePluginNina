using NINA.Astrometry;
using NINA.Core.Model;
using NINA.Core.Utility;
using NINA.Core.Utility.Notification;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Equipment.Model;
using NINA.Image.ImageAnalysis;
using NINA.Image.Interfaces;
using NINA.Plugin.Speckle.Model;
using NINA.Profile.Interfaces;
using NINA.WPF.Base.Interfaces.Mediator;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Plugin.Speckle.Services {

    [Export(typeof(IExposureCalibrationService))]
    [PartCreationPolicy(CreationPolicy.Shared)]
    public class ExposureCalibrationService : IExposureCalibrationService {
        private readonly IProfileService profileService;
        private readonly ICameraMediator cameraMediator;
        private readonly IImagingMediator imagingMediator;
        private readonly IFilterWheelMediator filterWheelMediator;

        [ImportingConstructor]
        public ExposureCalibrationService(IProfileService profileService, ICameraMediator cameraMediator, IImagingMediator imagingMediator, IFilterWheelMediator filterWheelMediator) {
            this.profileService = profileService;
            this.cameraMediator = cameraMediator;
            this.imagingMediator = imagingMediator;
            this.filterWheelMediator = filterWheelMediator;
        }

        public async Task<double> FindRoiExposureTimeAsync(ExposureCalibrationRequest request, IProgress<ApplicationStatus> progress, CancellationToken token) {
            var exposureCount = 1;
            var exposureTime = request.StepTime;
            var longestAllowed = Math.Max(request.MaxTime, request.StepTime);
            var cameraMaxAdu = HistogramMath.CameraBitDepthToAdu(request.BitDepth);
            var capture = new CaptureSequence() {
                ExposureTime = exposureTime,
                Binning = request.Binning,
                Gain = request.Gain,
                Offset = request.Offset,
                ImageType = request.ImageType,
                ProgressExposureCount = exposureCount,
                EnableSubSample = true,
                SubSambleRectangle = request.Roi
            };

            var imageParams = new PrepareImageParameters(true, false);
            try {
                while (exposureTime <= longestAllowed) {
                    while (exposureTime <= longestAllowed) {
                        capture.ExposureTime = exposureTime;
                        await cameraMediator.Capture(capture, token, progress);
                        progress.Report(new ApplicationStatus() { Status = "Calculating Roi exposureTime: " + exposureTime });
                        token.ThrowIfCancellationRequested();
                        IExposureData exposureData = await cameraMediator.Download(token);
                        token.ThrowIfCancellationRequested();

                        var imageData = await exposureData.ToImageData(progress, token);

                        var stats = await imageData.Statistics.Task;

                        capture.ProgressExposureCount = exposureCount;
                        var statsPerc = (stats.Max - stats.Mean) / stats.Max;
                        Logger.Debug("ExposureTime " + exposureTime + " Stats: Max " + stats.Max + " Mean " + stats.Mean + " CameraMaxAdu " + cameraMaxAdu + " Percentage " + statsPerc * 100);
                        if (stats.Max > cameraMaxAdu / 100 && statsPerc >= request.TargetAdu) break;
                        exposureCount++;
                        exposureTime = Math.Round(exposureTime + request.StepTime, 3);
                        request.OnIteration?.Invoke(exposureCount, exposureTime);
                    }
                    List<IImageStatistics> statsArray = new List<IImageStatistics>();
                    for (int i = 1; i < 10; i++) {
                        await cameraMediator.Capture(capture, token, progress);
                        progress.Report(new ApplicationStatus() { Status = "Calculating Roi exposureTime: " + exposureTime });
                        token.ThrowIfCancellationRequested();
                        IExposureData exposureData = await cameraMediator.Download(token);
                        token.ThrowIfCancellationRequested();

                        var imageData = await exposureData.ToImageData(progress, token);

                        await imagingMediator.PrepareImage(imageData, imageParams, token);

                        statsArray.Add(await imageData.Statistics.Task);
                    }
                    var avgMax = statsArray.Average(stats => stats.Max);
                    var avgMean = statsArray.Average(stats => stats.Mean);
                    Logger.Debug("Calulated averages: Max " + avgMax + " Mean " + avgMean);
                    if ((avgMax - avgMean) / avgMax >= request.TargetAdu) break;
                    exposureCount++;
                    exposureTime = Math.Round(exposureTime + request.StepTime, 3);
                    request.OnIteration?.Invoke(exposureCount, exposureTime);
                }
                if (exposureTime > longestAllowed) exposureTime = longestAllowed;
                return exposureTime;
            } catch (Exception) {
                cameraMediator.AbortExposure();
                throw;
            }
        }

        public double EstimateExposureTime(ExposureEstimateRequest request) {
            var airMass = RetrieveAirmass(request.Target);
            var elevation = profileService.ActiveProfile.AstrometrySettings.Elevation;
            var skybackground = RetrieveSkyBackground();
            var exposureTimePrecision = 0.002;
            var azerosum = CalculateAtmosphere(request.Telescope, request.Camera, RetrieveCurrentFilter(), airMass, elevation);
            return Calculate(request, RetrieveCurrentFilter(), azerosum, skybackground, exposureTimePrecision);
        }

        private double RetrieveAirmass(SpeckleTarget speckleTarget) {
            var lat = profileService.ActiveProfile.AstrometrySettings.Latitude;
            var longt = profileService.ActiveProfile.AstrometrySettings.Longitude;
            var altitude = speckleTarget.Coordinates().Transform(Angle.ByDegree(lat), Angle.ByDegree(longt), DateTime.Now).Altitude.Degree;
            return AstroUtil.Airmass(altitude);
        }

        private double RetrieveSkyBackground() {
            return 21;
        }

        private Filter RetrieveCurrentFilter() {
            Filter activeFilter;
            string activeFilterName = "";
            if (filterWheelMediator.GetInfo().Connected) { activeFilterName = filterWheelMediator.GetInfo().SelectedFilter.Name; }
            switch (activeFilterName) {
                case "L":
                    activeFilter = Filter.L;
                    break;
                case "R":
                    activeFilter = Filter.R;
                    break;
                case "G":
                    activeFilter = Filter.G;
                    break;
                case "B":
                    activeFilter = Filter.B;
                    break;
                case "Sloan U":
                    activeFilter = Filter.SU;
                    break;
                case "Sloan G":
                    activeFilter = Filter.SG;
                    break;
                case "Sloan R":
                    activeFilter = Filter.SR;
                    break;
                case "Sloan Z":
                    activeFilter = Filter.SZ;
                    break;
                case "Sloan I":
                    activeFilter = Filter.SI;
                    break;
                default:
                    activeFilter = Filter.None;
                    Logger.Debug("Warning: No filter stored in the calculation matches the currently active filter's name in NINA. Assuming no filter is being used.");
                    break;
            }
            Logger.Debug("Active filter for the calculation is now '" + activeFilter + "'.");
            return activeFilter;
        }

        private double RetrieveASDSNR(double fluxRatio, double magnitudePrimary) {
            double[,] prim7 = { { 0.05, 90 }, { 0.1, 80 }, { 0.25, 60 }, { 0.5, 45 }, { 0.75, 35 }, { 1, 25 } };
            double[,] prim8 = { { 0.05, 100 }, { 0.1, 90 }, { 0.25, 70 }, { 0.5, 50 }, { 0.75, 30 }, { 1, 20 } };
            double[,] prim9 = { { 0.05, 100 }, { 0.1, 80 }, { 0.25, 70 }, { 0.5, 50 }, { 0.75, 35 }, { 1, 25 } };
            double[,] prim10 = { { 0.05, 100 }, { 0.1, 90 }, { 0.25, 70 }, { 0.5, 50 }, { 0.75, 40 }, { 1, 30 } };
            double[,] prim11 = { { 0.05, 130 }, { 0.1, 100 }, { 0.25, 70 }, { 0.5, 50 }, { 0.75, 35 }, { 1, 25 } };
            double[,] prim12 = { { 0.05, 130 }, { 0.1, 90 }, { 0.25, 70 }, { 0.5, 55 }, { 0.75, 40 }, { 1, 30 } };
            double[,] prim13 = { { 0.05, 135 }, { 0.1, 90 }, { 0.25, 75 }, { 0.5, 50 }, { 0.75, 40 }, { 1, 20 } };
            double[,] prim14 = { { 0.05, 130 }, { 0.1, 110 }, { 0.25, 70 }, { 0.5, 50 }, { 0.75, 40 }, { 1, 30 } };
            double[,] prim15 = { { 0.05, 120 }, { 0.1, 120 }, { 0.25, 90 }, { 0.5, 60 }, { 0.75, 50 }, { 1, 40 } };
            List<double> fluxratios = new List<double>() { 0, 0.05, 0.1, 0.25, 0.5, 0.75, 1 };
            double roundedRatio = fluxratios.OrderBy(item => Math.Abs(fluxRatio - item)).First();
            Logger.Debug("The fluxratio is " + fluxRatio + ", rounded to " + roundedRatio + ".");
            if (roundedRatio == 0)
                throw new SequenceEntityFailedException("Fluxratio is not in range of ASD simulations. Falling back to user's time in list.");

            Dictionary<int, double[,]> primArrays = new Dictionary<int, double[,]>
            {
                { 7, prim7 },
                { 8, prim8 },
                { 9, prim9 },
                { 10, prim10 },
                { 11, prim11 },
                { 12, prim12 },
                { 13, prim13 },
                { 14, prim14 },
                { 15, prim15 },
            };
            double[,] selectedArray = primArrays[(int)magnitudePrimary];

            for (int i = 0; i < selectedArray.GetLength(0); i++) {
                if (selectedArray[i, 0] == roundedRatio) {
                    Logger.Debug("Found an asdSNR of " + selectedArray[i, 1] + " in " + selectedArray + " for the given primary magnitude of " + magnitudePrimary + " and fluxratio.");
                    return selectedArray[i, 1];
                }
            }
            Notification.ShowError("Couldn't find a fluxratio of " + fluxRatio + " for the asdSNR. Please verify the target list.");
            return 0;
        }

        private double CalculateAtmosphere(Telescope telescope, Camera camera, Filter filter, double airMass, double elevation) {
            var arrayWavelength = new double[] { 350.0, 400.0, 450.0, 500.0, 550.0, 600.0, 650.0, 700.0, 750.0, 800.0, 850.0, 900.0, 950.0, 1000.0 };
            var arrayPalomarExtinction = new double[] { 0.67, 0.36, 0.24, 0.18, 0.15, 0.13, 0.1, 0.07, 0.06, 0.05, 0.04, 0.04, 0.04, 0.03 };
            var zeroMagA0FluxSDensity = new double[] { 3.5, 7.5, 6.5, 4.9, 3.55, 2.8, 2.1, 1.7, 1.45, 1.1, 1.0, 0.9, 0.75, 0.65 };
            var arrayAtmosphericTransmission = new double[14];
            var arrayZeroMagA0Fluxin50nmBW = new double[14];
            var arrayZeroMagA0Starin50nmBW = new double[14];
            Logger.Debug("Elevation: " + elevation);
            Logger.Debug("Airmass: " + airMass);
            Logger.Debug("Palomar: " + arrayPalomarExtinction[0]);

            for (int i = 0; i < arrayPalomarExtinction.Length; i++) {
                arrayAtmosphericTransmission[i] = Math.Pow(10, (-0.4 * airMass * arrayPalomarExtinction[i] * Math.Exp(-elevation / 2500.0) / Math.Exp(-2000.0 / 2500.0)));
            }
            for (int i = 0; i < arrayPalomarExtinction.Length; i++) {
                arrayZeroMagA0Fluxin50nmBW[i] = 500.0 * (zeroMagA0FluxSDensity[i] * 0.000000001) / (2.0 * Math.Pow(10.0, -18.0) / (arrayWavelength[i] * 0.000000001));
            }
            for (int i = 0; i < arrayZeroMagA0Starin50nmBW.Length; i++) {
                arrayZeroMagA0Starin50nmBW[i] = (arrayZeroMagA0Fluxin50nmBW[i] * arrayAtmosphericTransmission[i] * 0.6 * filter.ArrayTransmission[i] * camera.ArrayQE[i] * ((Math.Pow(telescope.Focallength / 10.0, 2.0) - Math.Pow(telescope.ObstructionD / 10.0, 2.0)) * 0.785)) / 45.92362983;
            }
            double azerosum = 0;
            foreach (double value in arrayZeroMagA0Starin50nmBW) {
                azerosum += value;
            }
            Logger.Debug("The sum of arrayZeroMagA0Starin50nmBW is " + azerosum + ".");
            return azerosum;
        }

        private double Calculate(ExposureEstimateRequest request, Filter filter, double azerosum, double skybackground, double exposureTimePrecision) {
            var speckleTarget = request.Target;
            var telescope = request.Telescope;
            var camera = request.Camera;
            var barlow = request.Barlow;
            double photonshotnoise = 0;
            double darkcurrent = 0;
            double skyglownoise = 0;
            double totalnoise = 0;
            double tempsignal = 0;
            double tempSNR = 0;
            double exposureTime = 0.002;
            double SNR = 0;
            double minExposure = 0.02;
            double imagescale = ((206.265 * camera.PixelSize) / (telescope.Focallength * barlow.BarlowFactor));
            double RNinPE = camera.ReadNoise * Math.Sqrt(Math.Pow(30.0, 2.0) * 0.785);

            var truePMag = Math.Min(speckleTarget.Pmag, speckleTarget.Smag);
            truePMag = request.IsRef && speckleTarget.ReferenceStar.Rp != 0.0 ? speckleTarget.ReferenceStar.Rp : truePMag;
            if (truePMag < 7 || truePMag > 15)
                throw new SequenceEntityFailedException("Calculation requested for " + speckleTarget.Name + ", but primary mag is not in range of ASD simulations. Falling back to user's time in list.");
            Logger.Debug("True Primary is " + truePMag);

            var trueSMag = Math.Max(speckleTarget.Pmag, speckleTarget.Smag);
            Logger.Debug("True Secondary is " + trueSMag);

            var fluxRatio = Math.Pow(100, (truePMag - trueSMag) / 5.0);
            var combinedMagnitude = 2.5 * Math.Log10(1.0 / (Math.Pow(10.0, ((trueSMag - truePMag) / 2.5)) + 1.0)) + trueSMag;

            if (request.IntendedSNR != 0) {
                SNR = request.IntendedSNR;
                Logger.Debug("User override using intended SNR of " + request.IntendedSNR + ".");
            } else
                SNR = RetrieveASDSNR(fluxRatio, truePMag);

            if (barlow.BarlowFactor == 1) { Logger.Debug("Using pixelsize of " + camera.PixelSize + " microns and FL of " + telescope.Focallength + "mm."); } else { Logger.Debug("Using pixelsize of " + camera.PixelSize + " microns and FL of " + telescope.Focallength * barlow.BarlowFactor + "mm due to the " + barlow.BarlowFactor + "x barlow."); }
            Logger.Debug("Iteration starting with: SNR: " + SNR + ", RNinPE: " + RNinPE + ", " +
                    "imagescale: " + imagescale + "arcsec/px, magtp: " + truePMag + ", magts: " + trueSMag + ", fluxr: " + fluxRatio + ", precision: " + exposureTimePrecision + "s.");

            do {
                tempsignal = 1.0 * azerosum * Math.Pow(10.0, (-0.4 * combinedMagnitude)) * exposureTime;
                skyglownoise = Math.Sqrt(Math.Pow((imagescale * 35.0), 2.0) * 0.785 * azerosum * Math.Pow(10, (-0.4 * skybackground)) * exposureTime);
                darkcurrent = Math.Sqrt(camera.DarkCurrent * exposureTime * Math.Pow(35.0, 2.0) * 0.785);
                photonshotnoise = Math.Sqrt(tempsignal);
                totalnoise = Math.Sqrt(Math.Pow(photonshotnoise, 2.0) + Math.Pow(camera.ReadNoise, 2.0) + Math.Pow(skybackground, 2) + Math.Pow(camera.DarkCurrent, 2.0));

                tempSNR = Math.Round(tempsignal / totalnoise);
                if (tempSNR > SNR) {
                    Logger.Debug("FINISHED: tempSNR " + tempSNR + " is greater than " + SNR + ", returning exp. time: " + exposureTime + " with primmag. of " + truePMag + " and secmag. of " + trueSMag + ".");
                    if (exposureTime + 0.00 < minExposure) { return minExposure; } else { return exposureTime + exposureTimePrecision; }
                }

                exposureTime += exposureTimePrecision;
                Logger.Debug("Debug iteration: exposureTime: " + exposureTime + ", tempsignal: " + tempsignal + ", " +
                    "skyglownoise: " + skyglownoise + ", darkcurrent: " + darkcurrent + ", photonshot: " + photonshotnoise + ", total: " + totalnoise + ".");
            } while (exposureTime < 5.0);
            Notification.ShowInformation("Calculation failed for " + speckleTarget.Name + " as exposure time exceeded the maximum (5s).");
            return 0d;
        }
    }
}
