using System;

namespace NINA.Plugin.Speckle.Imaging {

    public static class Apodization {
        public const double DefaultTukeyAlpha = 0.25;

        public static double[] Hann(int size) {
            var line = new double[size];
            for (var i = 0; i < size; i++) {
                line[i] = 0.5 * (1.0 - Math.Cos(2.0 * Math.PI * i / (size - 1)));
            }
            return Separable(line);
        }

        public static double[] Tukey(int size, double alpha) {
            alpha = Math.Clamp(alpha, 0.0, 1.0);
            var line = new double[size];
            var taper = alpha * (size - 1) / 2.0;
            for (var i = 0; i < size; i++) {
                if (taper <= 0.0) {
                    line[i] = 1.0;
                } else if (i < taper) {
                    line[i] = 0.5 * (1.0 + Math.Cos(Math.PI * (i / taper - 1.0)));
                } else if (i > size - 1 - taper) {
                    line[i] = 0.5 * (1.0 + Math.Cos(Math.PI * ((size - 1 - i) / taper - 1.0)));
                } else {
                    line[i] = 1.0;
                }
            }
            return Separable(line);
        }

        private static double[] Separable(double[] line) {
            var size = line.Length;
            var window = new double[size * size];
            for (var y = 0; y < size; y++) {
                var wy = line[y];
                var row = y * size;
                for (var x = 0; x < size; x++) {
                    window[row + x] = wy * line[x];
                }
            }
            return window;
        }
    }
}
