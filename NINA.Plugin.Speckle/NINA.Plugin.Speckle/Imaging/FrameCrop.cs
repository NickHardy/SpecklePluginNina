using System;

namespace NINA.Plugin.Speckle.Imaging {

    public static class FrameCrop {
        public const int AutomaticSize = 256;

        public static int ChooseSize(int width, int height, int requested, int minSize, int maxSize) {
            var fits = Fft.LargestPowerOfTwoAtMost(Math.Min(width, height));
            if (fits < minSize) {
                return 0;
            }
            var size = requested > 0 && Fft.IsPowerOfTwo(requested) ? requested : Math.Min(fits, AutomaticSize);
            size = Math.Min(size, fits);
            size = Math.Min(size, maxSize);
            size = Math.Max(size, minSize);
            return size;
        }

        public static void Crop(ushort[] pixels, int width, int height,
                                double roiX, double roiY, double roiWidth, double roiHeight,
                                int n, ushort[] destination) {
            if (pixels == null) {
                throw new ArgumentNullException(nameof(pixels));
            }
            if (destination == null) {
                throw new ArgumentNullException(nameof(destination));
            }
            if (width <= 0 || height <= 0 || pixels.Length < width * height) {
                throw new ArgumentException("Frame dimensions do not match the pixel buffer", nameof(pixels));
            }
            if (destination.Length < n * n) {
                throw new ArgumentException("Destination buffer is too small", nameof(destination));
            }

            double centreX;
            double centreY;
            if (roiWidth > 0 && roiHeight > 0 && (width > roiWidth || height > roiHeight)) {
                centreX = roiX + roiWidth / 2.0;
                centreY = roiY + roiHeight / 2.0;
            } else {
                centreX = width / 2.0;
                centreY = height / 2.0;
            }

            var startX = (int)Math.Round(centreX - n / 2.0, MidpointRounding.AwayFromZero);
            var startY = (int)Math.Round(centreY - n / 2.0, MidpointRounding.AwayFromZero);
            startX = Math.Clamp(startX, Math.Min(0, width - n), Math.Max(0, width - n));
            startY = Math.Clamp(startY, Math.Min(0, height - n), Math.Max(0, height - n));

            var needsPad = width < n || height < n;
            var fill = needsPad ? FrameMedian(pixels, width, height) : (ushort)0;

            for (var y = 0; y < n; y++) {
                var sourceY = startY + y;
                var destRow = y * n;
                if (sourceY < 0 || sourceY >= height) {
                    for (var x = 0; x < n; x++) {
                        destination[destRow + x] = fill;
                    }
                    continue;
                }
                var sourceRow = sourceY * width;
                var copyStart = Math.Max(0, -startX);
                var copyEnd = Math.Min(n, width - startX);
                for (var x = 0; x < copyStart; x++) {
                    destination[destRow + x] = fill;
                }
                if (copyEnd > copyStart) {
                    Array.Copy(pixels, sourceRow + startX + copyStart, destination, destRow + copyStart, copyEnd - copyStart);
                }
                for (var x = Math.Max(copyEnd, 0); x < n; x++) {
                    destination[destRow + x] = fill;
                }
            }
        }

        private static ushort FrameMedian(ushort[] pixels, int width, int height) {
            var histogram = new int[ushort.MaxValue + 1];
            var count = width * height;
            for (var i = 0; i < count; i++) {
                histogram[pixels[i]]++;
            }
            var target = count / 2;
            var running = 0;
            for (var v = 0; v <= ushort.MaxValue; v++) {
                running += histogram[v];
                if (running > target) {
                    return (ushort)v;
                }
            }
            return 0;
        }
    }
}
