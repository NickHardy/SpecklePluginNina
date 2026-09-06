using System;

namespace NINA.Plugin.Speckle.Imaging {

    public sealed class RadialGrid {
        private readonly double[] scratch;
        private readonly int[] binOffsets;
        private readonly int[] binCounts;

        public RadialGrid(int size) {
            if (size < 2 || (size & 1) != 0) {
                throw new ArgumentException("Radial grid requires an even size of at least 2", nameof(size));
            }
            Size = size;
            Centre = size / 2;
            Radius = new double[size * size];
            Bin = new int[size * size];
            var maxBin = 0;
            for (var y = 0; y < size; y++) {
                var dy = y - Centre;
                for (var x = 0; x < size; x++) {
                    var dx = x - Centre;
                    var distance = Math.Sqrt((double)dx * dx + (double)dy * dy);
                    var index = y * size + x;
                    Radius[index] = distance;
                    var binIndex = (int)Math.Floor(distance);
                    Bin[index] = binIndex;
                    if (binIndex > maxBin) {
                        maxBin = binIndex;
                    }
                }
            }
            BinCount = maxBin + 1;
            scratch = new double[size * size];
            binOffsets = new int[BinCount + 1];
            binCounts = new int[BinCount];
            for (var i = 0; i < Bin.Length; i++) {
                binCounts[Bin[i]]++;
            }
            var running = 0;
            for (var binIndex = 0; binIndex < BinCount; binIndex++) {
                binOffsets[binIndex] = running;
                running += binCounts[binIndex];
            }
            binOffsets[BinCount] = running;
        }

        public int Size { get; }

        public int Centre { get; }

        public int BinCount { get; }

        public double[] Radius { get; }

        public int[] Bin { get; }

        public double[] MedianProfile(double[] values, int smooth) {
            if (values == null) {
                throw new ArgumentNullException(nameof(values));
            }
            if (values.Length != Radius.Length) {
                throw new ArgumentException("Value array does not match the grid", nameof(values));
            }
            var cursor = new int[BinCount];
            for (var i = 0; i < values.Length; i++) {
                var binIndex = Bin[i];
                scratch[binOffsets[binIndex] + cursor[binIndex]] = values[i];
                cursor[binIndex]++;
            }
            var profile = new double[BinCount];
            for (var binIndex = 0; binIndex < BinCount; binIndex++) {
                profile[binIndex] = Statistics.Median(scratch, binOffsets[binIndex], binCounts[binIndex]);
            }
            return smooth > 1 ? Smooth(profile, smooth) : profile;
        }

        public static double[] Smooth(double[] profile, int window) {
            if (window <= 1) {
                return profile;
            }
            var half = window / 2;
            var padded = new double[profile.Length + 2 * half];
            for (var i = 0; i < padded.Length; i++) {
                var source = Math.Clamp(i - half, 0, profile.Length - 1);
                padded[i] = profile[source];
            }
            var result = new double[profile.Length];
            var scale = 1.0 / window;
            for (var binIndex = 0; binIndex < profile.Length; binIndex++) {
                var sum = 0.0;
                for (var k = 0; k < window; k++) {
                    sum += padded[binIndex + k];
                }
                result[binIndex] = sum * scale;
            }
            return result;
        }
    }
}
