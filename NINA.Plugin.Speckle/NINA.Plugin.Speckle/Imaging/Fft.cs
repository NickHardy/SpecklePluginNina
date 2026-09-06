using System;

namespace NINA.Plugin.Speckle.Imaging {

    public sealed class Fft {
        private const int TransposeBlock = 32;

        private readonly int size;
        private readonly int[] reversal;
        private readonly double[] twiddleRe;
        private readonly double[] twiddleIm;

        public Fft(int size) {
            if (size < 2 || !IsPowerOfTwo(size)) {
                throw new ArgumentException("FFT size must be a power of two of at least 2", nameof(size));
            }
            this.size = size;
            reversal = BuildReversal(size);
            twiddleRe = new double[size / 2];
            twiddleIm = new double[size / 2];
            for (var bit = 0; bit < size / 2; bit++) {
                var angle = -2.0 * Math.PI * bit / size;
                twiddleRe[bit] = Math.Cos(angle);
                twiddleIm[bit] = Math.Sin(angle);
            }
        }

        public int Size => size;

        public static bool IsPowerOfTwo(int value) {
            return value > 0 && (value & (value - 1)) == 0;
        }

        public static int LargestPowerOfTwoAtMost(int value) {
            if (value < 1) {
                return 0;
            }
            var stride = 1;
            while (stride * 2 <= value) {
                stride *= 2;
            }
            return stride;
        }

        public void Forward(double[] re, double[] im) {
            Transform2D(re, im, false);
        }

        public void Inverse(double[] re, double[] im) {
            Transform2D(re, im, true);
        }

        public void Forward1D(double[] re, double[] im, int offset) {
            Transform1D(re, im, offset, false);
        }

        public static void FftShift(double[] values, int size) {
            if (values == null) {
                throw new ArgumentNullException(nameof(values));
            }
            if (values.Length != size * size) {
                throw new ArgumentException("Array length must be size squared", nameof(values));
            }
            if ((size & 1) != 0) {
                throw new ArgumentException("FftShift requires an even size", nameof(size));
            }
            var half = size / 2;
            for (var y = 0; y < half; y++) {
                var top = y * size;
                var bottom = (y + half) * size;
                for (var column = 0; column < half; column++) {
                    var a00 = top + column;
                    var a11 = bottom + column + half;
                    var swap = values[a00];
                    values[a00] = values[a11];
                    values[a11] = swap;

                    var a01 = top + column + half;
                    var a10 = bottom + column;
                    swap = values[a01];
                    values[a01] = values[a10];
                    values[a10] = swap;
                }
            }
        }

        private void Transform2D(double[] re, double[] im, bool inverse) {
            if (re == null) {
                throw new ArgumentNullException(nameof(re));
            }
            if (im == null) {
                throw new ArgumentNullException(nameof(im));
            }
            if (re.Length != size * size || im.Length != size * size) {
                throw new ArgumentException("Arrays must have length size * n");
            }
            for (var y = 0; y < size; y++) {
                Transform1D(re, im, y * size, inverse);
            }
            Transpose(re, size);
            Transpose(im, size);
            for (var y = 0; y < size; y++) {
                Transform1D(re, im, y * size, inverse);
            }
            Transpose(re, size);
            Transpose(im, size);
            if (inverse) {
                var scale = 1.0 / ((double)size * size);
                for (var i = 0; i < re.Length; i++) {
                    re[i] *= scale;
                    im[i] *= scale;
                }
            }
        }

        private void Transform1D(double[] re, double[] im, int offset, bool inverse) {
            for (var i = 0; i < size; i++) {
                var mirrored = reversal[i];
                if (mirrored > i) {
                    var left = offset + i;
                    var right = offset + mirrored;
                    var swap = re[left];
                    re[left] = re[right];
                    re[right] = swap;
                    swap = im[left];
                    im[left] = im[right];
                    im[right] = swap;
                }
            }
            for (var len = 2; len <= size; len <<= 1) {
                var half = len >> 1;
                var step = size / len;
                for (var start = 0; start < size; start += len) {
                    var bit = 0;
                    for (var mirrored = 0; mirrored < half; mirrored++) {
                        var wr = twiddleRe[bit];
                        var wi = inverse ? -twiddleIm[bit] : twiddleIm[bit];
                        bit += step;
                        var left = offset + start + mirrored;
                        var right = left + half;
                        var br = re[right];
                        var bi = im[right];
                        var xr = br * wr - bi * wi;
                        var xi = br * wi + bi * wr;
                        re[right] = re[left] - xr;
                        im[right] = im[left] - xi;
                        re[left] += xr;
                        im[left] += xi;
                    }
                }
            }
        }

        private static void Transpose(double[] values, int size) {
            for (var i = 0; i < size; i += TransposeBlock) {
                var iMax = Math.Min(i + TransposeBlock, size);
                for (var j = i; j < size; j += TransposeBlock) {
                    var jMax = Math.Min(j + TransposeBlock, size);
                    for (var y = i; y < iMax; y++) {
                        var column = j > y ? j : y + 1;
                        var row = y * size;
                        for (; column < jMax; column++) {
                            var rowIndex = row + column;
                            var columnIndex = column * size + y;
                            var swap = values[rowIndex];
                            values[rowIndex] = values[columnIndex];
                            values[columnIndex] = swap;
                        }
                    }
                }
            }
        }

        private static int[] BuildReversal(int size) {
            var bits = 0;
            while ((1 << bits) < size) {
                bits++;
            }
            var rev = new int[size];
            for (var i = 0; i < size; i++) {
                var reversed = 0;
                for (var b = 0; b < bits; b++) {
                    if ((i & (1 << b)) != 0) {
                        reversed |= 1 << (bits - 1 - b);
                    }
                }
                rev[i] = reversed;
            }
            return rev;
        }
    }
}
