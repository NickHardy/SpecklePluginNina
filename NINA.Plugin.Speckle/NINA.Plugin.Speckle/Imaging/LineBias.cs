using System;

namespace NINA.Plugin.Speckle.Imaging {

    public static class LineBias {

        public static void Remove(double[] pixels, int size, double[] lineScratch) {
            if (pixels == null || lineScratch == null || size <= 0) {
                return;
            }
            if (pixels.Length < size * size || lineScratch.Length < size) {
                throw new ArgumentException("Line bias removal needs a square frame and a scratch line of the same width", nameof(pixels));
            }
            for (var y = 0; y < size; y++) {
                var row = y * size;
                Array.Copy(pixels, row, lineScratch, 0, size);
                var median = Statistics.Median(lineScratch, 0, size);
                for (var x = 0; x < size; x++) {
                    pixels[row + x] -= median;
                }
            }
            for (var x = 0; x < size; x++) {
                for (var y = 0; y < size; y++) {
                    lineScratch[y] = pixels[y * size + x];
                }
                var median = Statistics.Median(lineScratch, 0, size);
                for (var y = 0; y < size; y++) {
                    pixels[y * size + x] -= median;
                }
            }
        }

        public static void Widen(ushort[] source, double[] destination, int length) {
            for (var i = 0; i < length; i++) {
                destination[i] = source[i];
            }
        }
    }
}
