using System;

namespace NINA.Plugin.Speckle.Imaging {

    public static class PowerSpectrum {
        public const double ArcsecPerRadian = 206264.806247096355;
        public const double DefaultFlattenFloorFraction = 1e-6;
        public const int DefaultFlattenSmooth = 5;

        public static double ArcsecPerPixel(double pixelSizeMicrons, double focalLengthMillimetres, double binning) {
            if (focalLengthMillimetres <= 0.0) {
                return 0.0;
            }
            return 206.265 * pixelSizeMicrons * (binning > 0 ? binning : 1.0) / focalLengthMillimetres;
        }

        public static double KSpaceRadius(int size, double arcsecPerPixel, double apertureMetres, double lambdaMetres) {
            if (arcsecPerPixel <= 0.0 || apertureMetres <= 0.0 || lambdaMetres <= 0.0) {
                return 0.0;
            }
            return size * (arcsecPerPixel / ArcsecPerRadian) * apertureMetres / lambdaMetres;
        }

        public static double DiffractionLimitArcsec(double apertureMetres, double lambdaMetres) {
            if (apertureMetres <= 0.0 || lambdaMetres <= 0.0) {
                return 0.0;
            }
            return ArcsecPerRadian * lambdaMetres / apertureMetres;
        }

        public static double RemovePhotonBias(double[] shifted, RadialGrid grid, double kSpaceRadius) {
            var radius = grid.Radius;
            var outsideCount = 0;
            var sum = 0.0;
            for (var i = 0; i < shifted.Length; i++) {
                if (radius[i] > kSpaceRadius) {
                    sum += shifted[i];
                    outsideCount++;
                }
            }
            if (outsideCount == 0) {
                return 0.0;
            }
            var pedestal = sum / outsideCount;
            for (var i = 0; i < shifted.Length; i++) {
                if (radius[i] > kSpaceRadius) {
                    shifted[i] = 0.0;
                } else {
                    var value = shifted[i] - pedestal;
                    shifted[i] = value > 0.0 ? value : 0.0;
                }
            }
            return pedestal;
        }

        public static double[] RadialFlatten(double[] shifted, RadialGrid grid, int smooth, double floorFraction, double[] destination) {
            return RadialFlatten(shifted, grid, smooth, floorFraction, 0.0, destination);
        }

        public static double[] RadialFlatten(double[] shifted, RadialGrid grid, int smooth, double floorFraction,
                                             double absoluteFloor, double[] destination) {
            var profile = grid.MedianProfile(shifted, smooth);
            var peak = 0.0;
            for (var b = 0; b < profile.Length; b++) {
                if (profile[b] > peak) {
                    peak = profile[b];
                }
            }
            var floor = Math.Max(floorFraction * Math.Max(peak, 1e-300), absoluteFloor);
            var bins = grid.Bin;
            for (var i = 0; i < shifted.Length; i++) {
                var divisor = profile[bins[i]];
                if (divisor < floor) {
                    divisor = floor;
                }
                destination[i] = shifted[i] / divisor;
            }
            return profile;
        }

        public static double[] RadialSubtract(double[] shifted, RadialGrid grid, int smooth, double[] destination) {
            var profile = grid.MedianProfile(shifted, smooth);
            var bins = grid.Bin;
            for (var i = 0; i < shifted.Length; i++) {
                destination[i] = shifted[i] - profile[bins[i]];
            }
            return profile;
        }

        public static void MaskDc(double[] shifted, RadialGrid grid, double radius) {
            if (radius < 0.0) {
                return;
            }
            var radiusPerPixel = grid.Radius;
            for (var i = 0; i < shifted.Length; i++) {
                if (radiusPerPixel[i] <= radius) {
                    shifted[i] = 0.0;
                }
            }
        }

        public static void Autocorrelation(double[] psdShifted, int size, Fft fft, double[] re, double[] im) {
            Array.Copy(psdShifted, re, psdShifted.Length);
            Array.Clear(im, 0, im.Length);
            var centre = size / 2;
            re[centre * size + centre] = 0.0;
            Fft.FftShift(re, size);
            fft.Inverse(re, im);
            Fft.FftShift(re, size);
        }

        public static void Autocorrelation(double[] psdShifted, int size, RadialGrid grid, double lowFrequencyRadius,
                                           Fft fft, double[] re, double[] im) {
            Array.Copy(psdShifted, re, psdShifted.Length);
            Array.Clear(im, 0, im.Length);
            MaskDc(re, grid, lowFrequencyRadius);
            Fft.FftShift(re, size);
            fft.Inverse(re, im);
            Fft.FftShift(re, size);
        }
    }
}
