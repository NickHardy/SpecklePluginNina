using System;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NINA.Plugin.Speckle.Imaging {

    public static class FringeRenderer {
        public const double DefaultLowPercentile = 10.0;
        public const double DefaultHighPercentile = 99.6;
        public const double PaneLowPercentile = 1.0;
        public const double PaneHighPercentile = 99.5;
        public const int PaneSmoothRadius = 1;
        public const double PaneSmoothMinPeriodPx = 8.0;
        public const double AutocorrelationLowPercentile = 10.0;
        public const double AutocorrelationHighPercentile = 99.6;

        public static void Stretch(double[] values, FringeStretchMode mode) {
            switch (mode) {
                case FringeStretchMode.Asinh:
                    for (var i = 0; i < values.Length; i++) {
                        values[i] = Math.Asinh(values[i]);
                    }
                    break;

                case FringeStretchMode.SignedSqrt:
                    for (var i = 0; i < values.Length; i++) {
                        var value = values[i];
                        values[i] = value < 0.0 ? -Math.Sqrt(-value) : Math.Sqrt(value);
                    }
                    break;

                case FringeStretchMode.Log:
                    for (var i = 0; i < values.Length; i++) {
                        values[i] = Math.Log(1.0 + Math.Max(values[i], 0.0));
                    }
                    break;
            }
        }

        public static void PercentileClip(double[] values, double[] scratch, double lowPercent, double highPercent) {
            Array.Copy(values, scratch, values.Length);
            Statistics.Percentiles(scratch, values.Length, lowPercent, highPercent, out var low, out var high);
            var range = high - low;
            if (range <= 0.0) {
                range = 1.0;
            }
            for (var i = 0; i < values.Length; i++) {
                values[i] = Math.Clamp((values[i] - low) / range, 0.0, 1.0);
            }
        }

        public static void BuildDisplay(double[] source, RadialGrid grid, double[] scratch,
                                        FringeStretchMode mode, double dcMaskRadius,
                                        double lowPercent, double highPercent) {
            if (dcMaskRadius >= 0.0) {
                PowerSpectrum.MaskDc(source, grid, dcMaskRadius);
            }
            Stretch(source, mode);
            PercentileClip(source, scratch, lowPercent, highPercent);
        }

        public static void PercentileClipWithin(double[] values, double[] scratch, double lowPercent, double highPercent,
                                                RadialGrid grid, double radius) {
            var count = 0;
            if (grid != null && radius > 0.0) {
                var radiusPerPixel = grid.Radius;
                for (var i = 0; i < values.Length; i++) {
                    if (radiusPerPixel[i] <= radius) {
                        scratch[count++] = values[i];
                    }
                }
            }
            if (count < 16) {
                Array.Copy(values, scratch, values.Length);
                count = values.Length;
            }
            Statistics.Percentiles(scratch, count, lowPercent, highPercent, out var low, out var high);
            var range = high - low;
            if (range <= 0.0) {
                range = 1.0;
            }
            for (var i = 0; i < values.Length; i++) {
                values[i] = Math.Clamp((values[i] - low) / range, 0.0, 1.0);
            }
        }

        public static void BoxBlur(double[] values, int size, int radius, double[] scratch) {
            if (radius <= 0) {
                return;
            }
            var window = radius * 2 + 1;
            for (var y = 0; y < size; y++) {
                var row = y * size;
                var sum = 0.0;
                for (var x = -radius; x <= radius; x++) {
                    sum += values[row + Math.Clamp(x, 0, size - 1)];
                }
                for (var x = 0; x < size; x++) {
                    scratch[row + x] = sum / window;
                    sum += values[row + Math.Clamp(x + radius + 1, 0, size - 1)] - values[row + Math.Clamp(x - radius, 0, size - 1)];
                }
            }
            for (var x = 0; x < size; x++) {
                var sum = 0.0;
                for (var y = -radius; y <= radius; y++) {
                    sum += scratch[Math.Clamp(y, 0, size - 1) * size + x];
                }
                for (var y = 0; y < size; y++) {
                    values[y * size + x] = sum / window;
                    sum += scratch[Math.Clamp(y + radius + 1, 0, size - 1) * size + x]
                         - scratch[Math.Clamp(y - radius, 0, size - 1) * size + x];
                }
            }
        }

        public static void BinomialBlur(double[] values, int size, double[] scratch) {
            for (var y = 0; y < size; y++) {
                var row = y * size;
                for (var x = 0; x < size; x++) {
                    var left = values[row + Math.Max(x - 1, 0)];
                    var right = values[row + Math.Min(x + 1, size - 1)];
                    scratch[row + x] = 0.25 * left + 0.5 * values[row + x] + 0.25 * right;
                }
            }
            for (var x = 0; x < size; x++) {
                for (var y = 0; y < size; y++) {
                    var above = scratch[Math.Max(y - 1, 0) * size + x];
                    var below = scratch[Math.Min(y + 1, size - 1) * size + x];
                    values[y * size + x] = 0.25 * above + 0.5 * scratch[y * size + x] + 0.25 * below;
                }
            }
        }

        public static void BuildPaneDisplay(double[] source, RadialGrid grid, double[] scratch,
                                            FringeStretchMode mode, double dcMaskRadius, double signalRadius) {
            BuildPaneDisplay(source, grid, scratch, mode, dcMaskRadius, signalRadius, double.PositiveInfinity);
        }

        public static void BuildPaneDisplay(double[] source, RadialGrid grid, double[] scratch,
                                            FringeStretchMode mode, double dcMaskRadius, double signalRadius,
                                            double fringePeriodPx) {
            if (dcMaskRadius >= 0.0) {
                PowerSpectrum.MaskDc(source, grid, dcMaskRadius);
            }
            if (fringePeriodPx >= PaneSmoothMinPeriodPx) {
                BinomialBlur(source, grid.Size, scratch);
            }
            Stretch(source, mode);
            PercentileClipWithin(source, scratch, PaneLowPercentile, PaneHighPercentile, grid, signalRadius);
        }

        public static byte[] ToGray8(double[] normalized, byte[] destination) {
            return ToGray8(normalized, destination, normalized.Length);
        }

        public static byte[] ToGray8(double[] normalized, byte[] destination, int count) {
            for (var i = 0; i < count; i++) {
                destination[i] = (byte)Math.Round(255.0 * normalized[i], MidpointRounding.AwayFromZero);
            }
            return destination;
        }

        public static BitmapSource CreateGray8Bitmap(byte[] gray, int size) {
            var bitmap = BitmapSource.Create(size, size, 96, 96, PixelFormats.Gray8, null, gray, size);
            bitmap.Freeze();
            return bitmap;
        }
    }
}
