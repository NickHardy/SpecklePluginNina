using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace NINA.Plugin.Speckle.Test {

    public static class TestVectorData {
        public const int Size = 64;
        public const int FrameCount = 32;
        public const double Tolerance = 1e-9;

        public static string Directory => Path.Combine(AppContext.BaseDirectory, "TestVector");

        public static ushort[][] LoadFrames() {
            var bytes = File.ReadAllBytes(Path.Combine(Directory, "frames_u16.bin"));
            var pixels = Size * Size;
            if (bytes.Length != FrameCount * pixels * 2) {
                throw new InvalidDataException("frames_u16.bin has an unexpected length of " + bytes.Length);
            }
            var frames = new ushort[FrameCount][];
            var offset = 0;
            for (var k = 0; k < FrameCount; k++) {
                var frame = new ushort[pixels];
                for (var i = 0; i < pixels; i++) {
                    frame[i] = (ushort)(bytes[offset] | (bytes[offset + 1] << 8));
                    offset += 2;
                }
                frames[k] = frame;
            }
            return frames;
        }

        public static double[] LoadBinary(string name) {
            var bytes = File.ReadAllBytes(Path.Combine(Directory, name));
            var values = new double[bytes.Length / 8];
            Buffer.BlockCopy(bytes, 0, values, 0, values.Length * 8);
            return values;
        }

        public static double[] LoadMatrix(string name) {
            var rows = new List<double[]>();
            foreach (var line in File.ReadLines(Path.Combine(Directory, name))) {
                if (line.Length == 0 || line[0] == '#') {
                    continue;
                }
                var parts = line.Split(',');
                var row = new double[parts.Length];
                for (var i = 0; i < parts.Length; i++) {
                    row[i] = double.Parse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture);
                }
                rows.Add(row);
            }
            var cols = rows[0].Length;
            var flat = new double[rows.Count * cols];
            for (var y = 0; y < rows.Count; y++) {
                Array.Copy(rows[y], 0, flat, y * cols, cols);
            }
            return flat;
        }

        public static JsonElement Scalars() {
            using (var stream = File.OpenRead(Path.Combine(Directory, "expected_scalars.json"))) {
                return JsonDocument.Parse(stream).RootElement.Clone();
            }
        }

        public static double Scalar(params string[] path) {
            var element = Scalars();
            foreach (var key in path) {
                element = element.GetProperty(key);
            }
            return element.GetDouble();
        }

        public static double RelativeError(double[] actual, double[] expected) {
            if (actual.Length != expected.Length) {
                throw new ArgumentException("Array lengths differ: " + actual.Length + " vs " + expected.Length);
            }
            var maxDifference = 0.0;
            var maxExpected = 0.0;
            for (var i = 0; i < actual.Length; i++) {
                var difference = Math.Abs(actual[i] - expected[i]);
                if (difference > maxDifference) {
                    maxDifference = difference;
                }
                var expectedMagnitude = Math.Abs(expected[i]);
                if (expectedMagnitude > maxExpected) {
                    maxExpected = expectedMagnitude;
                }
            }
            if (maxExpected == 0.0) {
                return maxDifference;
            }
            return maxDifference / maxExpected;
        }
    }
}
