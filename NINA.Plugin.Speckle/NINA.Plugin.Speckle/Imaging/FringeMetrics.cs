using System;

namespace NINA.Plugin.Speckle.Imaging {

    public sealed class FringeDetection {
        public double Dx { get; init; }
        public double Dy { get; init; }
        public double Offset { get; init; }
        public double PeriodPx { get; init; }
        public double PositionAngleDeg { get; init; }
        public double LobeSnr { get; init; }
        public double LobeFraction { get; init; }
        public int PeakX { get; init; }
        public int PeakY { get; init; }
    }

    public static class FringeMetrics {
        public const double PeakExclusionRadius = 4.0;
        public const double AnnulusHalfWidth = 2.0;
        public const double MadToSigma = 1.4826;

        public static double ExclusionRadius(int size, double kSpaceRadius) {
            if (kSpaceRadius <= 0.0) {
                return 3.0;
            }
            return Math.Max(3.0, size / kSpaceRadius);
        }

        public static FringeDetection DetectOffset(double[] psdShifted, RadialGrid grid, Fft fft,
                                                   double exclusionRadius, double maxRadius,
                                                   double[] work, double[] scratch) {
            return DetectOffset(psdShifted, grid, fft, exclusionRadius, maxRadius, 0.0, work, scratch);
        }

        public static FringeDetection DetectOffset(double[] psdShifted, RadialGrid grid, Fft fft,
                                                   double exclusionRadius, double maxRadius,
                                                   double lowFrequencyRadius,
                                                   double[] work, double[] scratch) {
            var size = grid.Size;
            var centre = grid.Centre;
            PowerSpectrum.Autocorrelation(psdShifted, size, grid, lowFrequencyRadius, fft, work, scratch);
            var acCentre = work[centre * size + centre];
            PowerSpectrum.RadialSubtract(work, grid, 1, work);

            var radius = grid.Radius;
            var bestValue = double.NegativeInfinity;
            var bestIndex = -1;
            for (var i = 0; i < work.Length; i++) {
                var radiusHere = radius[i];
                if (radiusHere <= exclusionRadius || radiusHere > maxRadius) {
                    continue;
                }
                if (work[i] > bestValue) {
                    bestValue = work[i];
                    bestIndex = i;
                }
            }
            if (bestIndex < 0) {
                return new FringeDetection { PeriodPx = double.PositiveInfinity };
            }

            var peakY = bestIndex / size;
            var peakX = bestIndex - peakY * size;
            Refine(work, size, peakX, peakY, out var subX, out var subY);
            var vx = peakX + subX - centre;
            var vy = peakY + subY - centre;
            var magnitude = Math.Sqrt(vx * vx + vy * vy);
            var peakRadius = Math.Sqrt((double)(peakX - centre) * (peakX - centre) + (double)(peakY - centre) * (peakY - centre));

            var mirrorX = 2 * centre - peakX;
            var mirrorY = 2 * centre - peakY;
            var annulus = scratch;
            var count = 0;
            for (var y = 0; y < size; y++) {
                for (var x = 0; x < size; x++) {
                    var index = y * size + x;
                    var radiusHere = radius[index];
                    if (radiusHere <= exclusionRadius || radiusHere > maxRadius) {
                        continue;
                    }
                    if (Math.Abs(radiusHere - peakRadius) > AnnulusHalfWidth) {
                        continue;
                    }
                    if (Hypot(x - peakX, y - peakY) <= PeakExclusionRadius) {
                        continue;
                    }
                    if (Hypot(x - mirrorX, y - mirrorY) <= PeakExclusionRadius) {
                        continue;
                    }
                    annulus[count++] = work[index];
                }
            }

            var snr = 0.0;
            if (count > 0) {
                var median = Statistics.Median(annulus, 0, count);
                for (var i = 0; i < count; i++) {
                    annulus[i] = Math.Abs(annulus[i] - median);
                }
                var sigma = MadToSigma * Statistics.Median(annulus, 0, count);
                if (sigma > 0.0) {
                    snr = (work[bestIndex] - median) / sigma;
                }
            }

            var angle = Math.Atan2(vy, vx) * 180.0 / Math.PI;
            angle %= 180.0;
            if (angle < 0.0) {
                angle += 180.0;
            }

            return new FringeDetection {
                Dx = vx,
                Dy = vy,
                Offset = magnitude,
                PeriodPx = magnitude > 0.0 ? size / magnitude : double.PositiveInfinity,
                PositionAngleDeg = angle,
                LobeSnr = snr,
                LobeFraction = acCentre > 0.0 ? work[bestIndex] / acCentre : 0.0,
                PeakX = peakX,
                PeakY = peakY
            };
        }

        public static double FringeVisibility(double[] psdShifted, RadialGrid grid,
                                              double period, double angleDeg,
                                              double lowPassRadius, double highPassRadius,
                                              out double projection) {
            projection = 0.0;
            var size = grid.Size;
            var centre = grid.Centre;
            if (period <= 0.0 || double.IsInfinity(period) || lowPassRadius <= 0.0) {
                return 0.0;
            }
            var kx = Math.Cos(angleDeg * Math.PI / 180.0);
            var ky = Math.Sin(angleDeg * Math.PI / 180.0);
            var lowFactor = 1.0 / (2.0 * lowPassRadius * lowPassRadius);
            var highFactor = highPassRadius > 0.0 ? 1.0 / (2.0 * highPassRadius * highPassRadius) : 0.0;
            var twoPiOverPeriod = 2.0 * Math.PI / period;
            var cosSum = 0.0;
            var sinSum = 0.0;
            var average = 0.0;
            for (var y = 0; y < size; y++) {
                var dy = (double)(y - centre);
                var row = y * size;
                for (var x = 0; x < size; x++) {
                    var dx = (double)(x - centre);
                    var radiusSquared = dx * dx + dy * dy;
                    var weight = Math.Exp(-radiusSquared * lowFactor);
                    if (highFactor > 0.0) {
                        weight *= 1.0 - Math.Exp(-radiusSquared * highFactor);
                    }
                    if (x == centre && y == centre) {
                        weight = 0.0;
                    }
                    var weightedPower = weight * psdShifted[row + x];
                    var arg = twoPiOverPeriod * (dx * kx + dy * ky);
                    cosSum += Math.Cos(arg) * weightedPower;
                    sinSum += Math.Sin(arg) * weightedPower;
                    average += weightedPower;
                }
            }
            if (average <= 0.0) {
                return 0.0;
            }
            projection = Math.Sqrt(cosSum * cosSum + sinSum * sinSum) / average;
            return 2.0 * projection;
        }

        public static double RatioFromVisibility(double visibility) {
            var clampedVisibility = Math.Clamp(visibility, 1e-12, 1.0);
            return (1.0 - Math.Sqrt(Math.Max(0.0, 1.0 - clampedVisibility * clampedVisibility))) / clampedVisibility;
        }

        public static double MagnitudeDifference(double ratio) {
            if (ratio <= 0.0) {
                return double.NaN;
            }
            return -2.5 * Math.Log10(ratio);
        }

        private static void Refine(double[] values, int size, int x, int y, out double dx, out double dy) {
            dx = 0.0;
            dy = 0.0;
            var index = y * size + x;
            if (x > 0 && x < size - 1) {
                var secondDifference = values[index - 1] - 2.0 * values[index] + values[index + 1];
                if (secondDifference != 0.0) {
                    dx = Math.Clamp(0.5 * (values[index - 1] - values[index + 1]) / secondDifference, -1.0, 1.0);
                }
            }
            if (y > 0 && y < size - 1) {
                var secondDifference = values[index - size] - 2.0 * values[index] + values[index + size];
                if (secondDifference != 0.0) {
                    dy = Math.Clamp(0.5 * (values[index - size] - values[index + size]) / secondDifference, -1.0, 1.0);
                }
            }
        }

        private static double Hypot(int dx, int dy) {
            return Math.Sqrt((double)dx * dx + (double)dy * dy);
        }
    }
}
