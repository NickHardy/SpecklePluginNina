using NINA.Plugin.Speckle.Imaging;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using Xunit;

namespace NINA.Plugin.Speckle.Test {

    public static class Vector {
        public const double Tolerance = 1e-9;

        public static string Path(string folder, string name) {
            return System.IO.Path.Combine(AppContext.BaseDirectory, folder, name);
        }

        public static ushort[][] LoadFrames(string folder, int frames, int size) {
            var bytes = File.ReadAllBytes(Path(folder, "frames_u16.bin"));
            var expected = frames * size * size * 2;
            Assert.Equal(expected, bytes.Length);
            var result = new ushort[frames][];
            var offset = 0;
            for (var k = 0; k < frames; k++) {
                var frame = new ushort[size * size];
                for (var i = 0; i < frame.Length; i++) {
                    frame[i] = (ushort)(bytes[offset] | (bytes[offset + 1] << 8));
                    offset += 2;
                }
                result[k] = frame;
            }
            return result;
        }

        public static double[] LoadBinary(string folder, string name, int count) {
            var bytes = File.ReadAllBytes(Path(folder, name));
            Assert.Equal(count * 8, bytes.Length);
            var values = new double[count];
            Buffer.BlockCopy(bytes, 0, values, 0, bytes.Length);
            return values;
        }

        public static double[] LoadCsv(string folder, string name, int count) {
            var values = new List<double>(count);
            foreach (var line in File.ReadLines(Path(folder, name))) {
                if (line.Length == 0 || line[0] == '#') {
                    continue;
                }
                foreach (var cell in line.Split(',')) {
                    values.Add(double.Parse(cell, NumberStyles.Float, CultureInfo.InvariantCulture));
                }
            }
            Assert.Equal(count, values.Count);
            return values.ToArray();
        }

        public static JsonElement LoadScalars(string folder) {
            using var doc = JsonDocument.Parse(File.ReadAllText(Path(folder, "expected_scalars.json")));
            return doc.RootElement.Clone();
        }

        public static double RelativeError(double[] actual, double[] expected) {
            Assert.Equal(expected.Length, actual.Length);
            var worst = 0.0;
            var scale = 0.0;
            for (var i = 0; i < expected.Length; i++) {
                var diff = Math.Abs(actual[i] - expected[i]);
                if (diff > worst) {
                    worst = diff;
                }
                var magnitude = Math.Abs(expected[i]);
                if (magnitude > scale) {
                    scale = magnitude;
                }
            }
            return scale > 0.0 ? worst / scale : worst;
        }

        public static void AssertMatches(double[] actual, double[] expected, string what) {
            var error = RelativeError(actual, expected);
            Assert.True(error <= Tolerance,
                what + " differs from the reference by " + error.ToString("E3", CultureInfo.InvariantCulture)
                + " relative, tolerance " + Tolerance.ToString("E0", CultureInfo.InvariantCulture));
        }

        public static void AssertClose(double actual, double expected, double tolerance, string what) {
            var scale = Math.Max(Math.Abs(expected), 1e-300);
            var error = Math.Abs(actual - expected) / scale;
            Assert.True(error <= tolerance,
                what + ": expected " + expected.ToString("G17", CultureInfo.InvariantCulture)
                + " but got " + actual.ToString("G17", CultureInfo.InvariantCulture)
                + " (relative error " + error.ToString("E3", CultureInfo.InvariantCulture) + ")");
        }

        public static double[] Accumulate(ushort[][] frames, int size, bool subtractMean) {
            var accumulator = new PowerSpectrumAccumulator(size);
            foreach (var frame in frames) {
                accumulator.AddFrame(frame, subtractMean, null);
            }
            var psd = new double[size * size];
            accumulator.CopyMeanPsd(psd);
            return psd;
        }
    }

    public class FftConventionTests {

        [Theory]
        [InlineData(8)]
        [InlineData(16)]
        [InlineData(64)]
        [InlineData(128)]
        public void ForwardThenInverseReturnsTheInput(int size) {
            var fft = new Fft(size);
            var random = new Random(4242);
            var re = new double[size * size];
            var im = new double[size * size];
            var originalRe = new double[size * size];
            var originalIm = new double[size * size];
            for (var i = 0; i < re.Length; i++) {
                originalRe[i] = re[i] = random.NextDouble() * 2000.0 - 1000.0;
                originalIm[i] = im[i] = random.NextDouble() * 2000.0 - 1000.0;
            }
            fft.Forward(re, im);
            fft.Inverse(re, im);
            for (var i = 0; i < re.Length; i++) {
                Assert.True(Math.Abs(re[i] - originalRe[i]) < 1e-9, "round trip real part");
                Assert.True(Math.Abs(im[i] - originalIm[i]) < 1e-9, "round trip imaginary part");
            }
        }

        [Fact]
        public void ParsevalPinsTheScalingConvention() {
            const int n = 64;
            var fft = new Fft(n);
            var random = new Random(7);
            var re = new double[n * n];
            var im = new double[n * n];
            var energy = 0.0;
            for (var i = 0; i < re.Length; i++) {
                re[i] = random.NextDouble() - 0.5;
                energy += re[i] * re[i];
            }
            fft.Forward(re, im);
            var spectral = 0.0;
            for (var i = 0; i < re.Length; i++) {
                spectral += re[i] * re[i] + im[i] * im[i];
            }
            Vector.AssertClose(spectral / (n * n), energy, 1e-12, "Parseval");
        }

        [Fact]
        public void ImpulseAtOriginTransformsToAConstantPlane() {
            const int n = 32;
            var fft = new Fft(n);
            var re = new double[n * n];
            var im = new double[n * n];
            re[0] = 5.0;
            fft.Forward(re, im);
            for (var i = 0; i < re.Length; i++) {
                Assert.True(Math.Abs(re[i] - 5.0) < 1e-12);
                Assert.True(Math.Abs(im[i]) < 1e-12);
            }
        }

        [Fact]
        public void FftShiftIsItsOwnInverse() {
            const int n = 16;
            var plane = new double[n * n];
            var original = new double[n * n];
            for (var i = 0; i < plane.Length; i++) {
                original[i] = plane[i] = i;
            }
            Fft.FftShift(plane, n);
            Assert.Equal(original[0], plane[(n / 2) * n + n / 2]);
            Fft.FftShift(plane, n);
            Assert.Equal(original, plane);
        }
    }

    public class SyntheticVectorTests {
        private const string Folder = "TestVector";
        private const int Size = 64;
        private const int Frames = 32;
        private const int Length = Size * Size;

        [Fact]
        public void FramesAreReadWithTheRightEndiannessAndStride() {
            var frames = Vector.LoadFrames(Folder, Frames, Size);
            var scalars = Vector.LoadScalars(Folder).GetProperty("frames_checksum");
            ulong sum = 0;
            var min = int.MaxValue;
            var max = int.MinValue;
            foreach (var frame in frames) {
                foreach (var value in frame) {
                    sum += value;
                    min = Math.Min(min, value);
                    max = Math.Max(max, value);
                }
            }
            Assert.Equal(scalars.GetProperty("sum_u64").GetUInt64(), sum);
            Assert.Equal(scalars.GetProperty("min").GetInt32(), min);
            Assert.Equal(scalars.GetProperty("max").GetInt32(), max);
        }

        [Fact]
        public void AccumulatedMeanPowerSpectrumMatchesThePythonReference() {
            var psd = Vector.Accumulate(Vector.LoadFrames(Folder, Frames, Size), Size, true);
            var expected = Vector.LoadBinary(Folder, "expected_psd_mean_unshifted.f64", Length);
            Vector.AssertMatches(psd, expected, "accumulated mean PSD, unshifted");

            var scalars = Vector.LoadScalars(Folder).GetProperty("psd_mean_unshifted");
            Vector.AssertClose(psd[0], scalars.GetProperty("at_0_0").GetDouble(), 1e-9, "psd[0,0]");
            Vector.AssertClose(psd[1], scalars.GetProperty("at_0_1").GetDouble(), 1e-9, "psd[0,1]");
            Vector.AssertClose(psd[Size], scalars.GetProperty("at_1_0").GetDouble(), 1e-9, "psd[1,0]");
            Vector.AssertClose(psd[32 * Size + 32], scalars.GetProperty("at_32_32").GetDouble(), 1e-9, "psd[32,32]");
            Vector.AssertClose(psd[10 * Size + 20], scalars.GetProperty("at_10_20").GetDouble(), 1e-9, "psd[y=10,x=20]");
        }

        [Fact]
        public void AccumulationWithoutPreprocessingMatchesTheReference() {
            var psd = Vector.Accumulate(Vector.LoadFrames(Folder, Frames, Size), Size, false);
            var expected = Vector.LoadCsv(Folder, "expected_psd_mean_unshifted_noprep.csv", Length);
            Vector.AssertMatches(psd, expected, "accumulated mean PSD without preprocessing");
        }

        [Fact]
        public void SingleFramePowerSpectrumMatchesTheReference() {
            var frames = Vector.LoadFrames(Folder, Frames, Size);
            var accumulator = new PowerSpectrumAccumulator(Size);
            accumulator.AddFrame(frames[0], true, null);
            var last = new double[Length];
            accumulator.CopyLastPsd(last);
            var expected = Vector.LoadCsv(Folder, "expected_frame0_psd_unshifted.csv", Length);
            Vector.AssertMatches(last, expected, "single frame PSD");

            var parseval = Vector.LoadScalars(Folder).GetProperty("parseval_frame0");
            var spectral = 0.0;
            foreach (var v in last) {
                spectral += v;
            }
            Vector.AssertClose(spectral / Length,
                parseval.GetProperty("sum_psd_over_n2").GetDouble(), 1e-12, "Parseval on frame 0");
        }

        [Fact]
        public void ShiftedBiasRemovedFlattenedAndAutocorrelationAllMatch() {
            var scalars = Vector.LoadScalars(Folder);
            var kRadius = scalars.GetProperty("optics").GetProperty("k_space_radius_px").GetDouble();
            var grid = new RadialGrid(Size);
            var fft = new Fft(Size);

            var psd = Vector.Accumulate(Vector.LoadFrames(Folder, Frames, Size), Size, true);
            Fft.FftShift(psd, Size);
            Vector.AssertMatches(psd, Vector.LoadCsv(Folder, "expected_psd_mean_shifted.csv", Length),
                "shifted mean PSD");

            var pedestal = PowerSpectrum.RemovePhotonBias(psd, grid, kRadius);
            Vector.AssertClose(pedestal,
                scalars.GetProperty("photon_bias").GetProperty("pedestal").GetDouble(), 1e-9, "photon bias pedestal");
            Vector.AssertMatches(psd, Vector.LoadBinary(Folder, "expected_psd_biasremoved_shifted.f64", Length),
                "bias removed PSD");

            var flattened = new double[Length];
            var profile = PowerSpectrum.RadialFlatten(psd, grid, PowerSpectrum.DefaultFlattenSmooth,
                PowerSpectrum.DefaultFlattenFloorFraction, flattened);
            Vector.AssertMatches(profile, Vector.LoadCsv(Folder, "expected_radial_profile.csv", profile.Length),
                "radial median profile");
            Vector.AssertMatches(flattened, Vector.LoadCsv(Folder, "expected_psd_flattened_shifted.csv", Length),
                "radially flattened PSD");

            var re = new double[Length];
            var im = new double[Length];
            PowerSpectrum.Autocorrelation(psd, Size, fft, re, im);
            Vector.AssertMatches(re, Vector.LoadCsv(Folder, "expected_autocorr_shifted.csv", Length),
                "autocorrelation");

            var scratch = new double[Length];
            FringeRenderer.BuildDisplay(flattened, grid, scratch, FringeStretchMode.Asinh, 1.0,
                FringeRenderer.DefaultLowPercentile, FringeRenderer.DefaultHighPercentile);
            Vector.AssertMatches(flattened, Vector.LoadCsv(Folder, "expected_display_gray.csv", Length),
                "display image");
        }

        [Fact]
        public void MetricsReproduceThePythonRecoveredValues() {
            var scalars = Vector.LoadScalars(Folder);
            var optics = scalars.GetProperty("optics");
            var expected = scalars.GetProperty("metrics");
            var kRadius = optics.GetProperty("k_space_radius_px").GetDouble();
            var grid = new RadialGrid(Size);
            var fft = new Fft(Size);

            var psd = Vector.Accumulate(Vector.LoadFrames(Folder, Frames, Size), Size, true);
            Fft.FftShift(psd, Size);
            PowerSpectrum.RemovePhotonBias(psd, grid, kRadius);

            var work = new double[Length];
            var scratch = new double[Length];
            var detection = FringeMetrics.DetectOffset(psd, grid, fft,
                FringeMetrics.ExclusionRadius(Size, kRadius), Size / 2 - 2, work, scratch);

            Vector.AssertClose(detection.Offset, expected.GetProperty("d_px").GetDouble(), 1e-9, "separation in pixels");
            Vector.AssertClose(detection.Dx, expected.GetProperty("dx").GetDouble(), 1e-9, "dx");
            Vector.AssertClose(detection.Dy, expected.GetProperty("dy").GetDouble(), 1e-9, "dy");
            Vector.AssertClose(detection.PeriodPx, expected.GetProperty("period_px").GetDouble(), 1e-9, "fringe period");
            Vector.AssertClose(detection.PositionAngleDeg, expected.GetProperty("pa_det_deg").GetDouble(), 1e-9, "position angle");
            Vector.AssertClose(detection.LobeSnr, expected.GetProperty("lobe_snr").GetDouble(), 1e-9, "lobe SNR");

            var highPass = Math.Clamp(detection.PeriodPx, 3.0, 0.5 * kRadius);
            var visibility = FringeMetrics.FringeVisibility(psd, grid, detection.PeriodPx,
                detection.PositionAngleDeg, kRadius, highPass, out var projection);
            Vector.AssertClose(projection, expected.GetProperty("fv").GetDouble(), 1e-9, "cosine projection");
            Vector.AssertClose(visibility, expected.GetProperty("visibility").GetDouble(), 1e-9, "fringe visibility");
            Vector.AssertClose(FringeMetrics.RatioFromVisibility(visibility),
                expected.GetProperty("ratio_from_v").GetDouble(), 1e-9, "brightness ratio");
        }

        [Fact]
        public void TwoDeltaCaseGivesVerticalBandsWithTheClosedFormPeriod() {
            var frame = new double[Length];
            frame[32 * Size + 32] = 1000.0;
            frame[32 * Size + 37] = 1000.0;

            var accumulator = new PowerSpectrumAccumulator(Size);
            accumulator.AddFrame(frame, false, null);
            var psd = new double[Length];
            accumulator.CopyMeanPsd(psd);
            Fft.FftShift(psd, Size);

            Vector.AssertMatches(psd, Vector.LoadCsv(Folder, "analytic_two_delta_psd_shifted.csv", Length),
                "two delta power spectrum");

            for (var x = 0; x < Size; x++) {
                var expected = 1000.0 * 1000.0 * (2.0 + 2.0 * Math.Cos(2.0 * Math.PI * 5.0 * (x - 32) / Size));
                Vector.AssertClose(psd[32 * Size + x], expected, 1e-9, "closed form cut at y = 32, x = " + x);
            }

            var columnSpread = 0.0;
            for (var y = 0; y < Size; y++) {
                columnSpread = Math.Max(columnSpread, Math.Abs(psd[y * Size + 20] - psd[32 * Size + 20]));
            }
            Assert.True(columnSpread < 1e-6,
                "brightness must be constant along y for a pair separated along x; the index order is transposed");
        }

        [Fact]
        public void AveragingFramesFirstDestroysTheFringes() {
            var frames = Vector.LoadFrames(Folder, Frames, Size);
            var kRadius = Vector.LoadScalars(Folder).GetProperty("optics")
                .GetProperty("k_space_radius_px").GetDouble();
            var grid = new RadialGrid(Size);
            var fft = new Fft(Size);

            var mean = new double[Length];
            foreach (var frame in frames) {
                for (var i = 0; i < Length; i++) {
                    mean[i] += frame[i];
                }
            }
            for (var i = 0; i < Length; i++) {
                mean[i] /= Frames;
            }
            var wrong = new PowerSpectrumAccumulator(Size);
            wrong.AddFrame(mean, true, null);
            var wrongPsd = new double[Length];
            wrong.CopyMeanPsd(wrongPsd);

            var correct = Snr(Vector.Accumulate(frames, Size, true), grid, fft, kRadius);
            var averagedFirst = Snr(wrongPsd, grid, fft, kRadius);

            Assert.True(correct > 6.0, "the accumulated per-frame power spectrum must detect the pair, got " + correct);
            Assert.True(averagedFirst < 4.0,
                "averaging the frames before transforming must destroy the detection, got " + averagedFirst);
            Assert.True(correct > 2.0 * averagedFirst, "the correct pipeline must be decisively better");
        }

        private static double Snr(double[] psdUnshifted, RadialGrid grid, Fft fft, double kRadius) {
            Fft.FftShift(psdUnshifted, Size);
            PowerSpectrum.RemovePhotonBias(psdUnshifted, grid, kRadius);
            var work = new double[Length];
            var scratch = new double[Length];
            return FringeMetrics.DetectOffset(psdUnshifted, grid, fft,
                FringeMetrics.ExclusionRadius(Size, kRadius), Size / 2 - 2, work, scratch).LobeSnr;
        }
    }

    public class RealDataVectorTests {
        private const string Folder = "TestVectorReal";
        private const int Size = 128;
        private const int Frames = 32;
        private const int Length = Size * Size;
        private const double KRadius = 23.0;
        private const double ArcsecPerPixel = 0.025000206305202756;

        private static FringeDetection DetectOnRealFrames(bool removeLineBias, bool apodize) {
            var frames = Vector.LoadFrames(Folder, Frames, Size);
            var accumulator = new PowerSpectrumAccumulator(Size);
            var window = apodize ? Apodization.Tukey(Size, Apodization.DefaultTukeyAlpha) : null;
            var widened = new double[Length];
            var line = new double[Size];
            foreach (var frame in frames) {
                LineBias.Widen(frame, widened, Length);
                if (removeLineBias) {
                    LineBias.Remove(widened, Size, line);
                }
                accumulator.AddFrame(widened, true, window);
            }
            var psd = new double[Length];
            accumulator.CopyMeanPsd(psd);
            Fft.FftShift(psd, Size);
            var grid = new RadialGrid(Size);
            PowerSpectrum.RemovePhotonBias(psd, grid, KRadius);
            var exclusion = FringeMetrics.ExclusionRadius(Size, KRadius);
            var maxRadius = Math.Max(Math.Min(Size / 3.0, 3.0 / ArcsecPerPixel), exclusion + 2.0);
            var work = new double[Length];
            var scratch = new double[Length];
            return FringeMetrics.DetectOffset(psd, grid, accumulator.Transform, exclusion, maxRadius, 3.0, work, scratch);
        }

        [Fact]
        public void TheDetectorNoLongerLocksOntoTheFrameEdgeWrap() {
            var scalars = Vector.LoadScalars(Folder);
            var artefact = scalars.GetProperty("metrics").GetProperty("d_px").GetDouble();
            var detection = DetectOnRealFrames(true, true);
            Assert.True(artefact > 60.0, "the pinned vector must still record the old artefact at " + artefact);
            Assert.True(detection.Offset < 45.0,
                "the detected offset must sit inside the physical search band, got " + detection.Offset);
        }

        [Fact]
        public void RealFramesRecoverThePairOnceTheLineBiasIsRemoved() {
            var truth = Vector.LoadScalars(Folder).GetProperty("full_stack_reference");
            var expectedOffset = truth.GetProperty("d_px").GetDouble();
            var expectedAngle = truth.GetProperty("pa_det_deg").GetDouble();
            var detection = DetectOnRealFrames(true, true);
            Assert.True(Math.Abs(detection.Offset - expectedOffset) < 4.0,
                "offset " + detection.Offset + " should be near the full stack truth " + expectedOffset);
            var angleError = Math.Abs(detection.PositionAngleDeg - expectedAngle);
            angleError = Math.Min(angleError, 180.0 - angleError);
            Assert.True(angleError < 12.0,
                "position angle " + detection.PositionAngleDeg + " should be near the full stack truth " + expectedAngle);
        }

        [Fact]
        public void RealFramesAreReadWithTheRightEndiannessAndStride() {
            var frames = Vector.LoadFrames(Folder, Frames, Size);
            var scalars = Vector.LoadScalars(Folder).GetProperty("frames_checksum");
            ulong sum = 0;
            var min = int.MaxValue;
            var max = int.MinValue;
            foreach (var frame in frames) {
                foreach (var value in frame) {
                    sum += value;
                    min = Math.Min(min, value);
                    max = Math.Max(max, value);
                }
            }
            Assert.Equal(scalars.GetProperty("sum_u64").GetUInt64(), sum);
            Assert.Equal(scalars.GetProperty("min").GetInt32(), min);
            Assert.Equal(scalars.GetProperty("max").GetInt32(), max);
        }

        [Fact]
        public void RealAccumulatedPowerSpectrumMatchesThePythonReference() {
            var psd = Vector.Accumulate(Vector.LoadFrames(Folder, Frames, Size), Size, true);
            Vector.AssertMatches(psd, Vector.LoadBinary(Folder, "expected_psd_mean_unshifted.f64", Length),
                "real data accumulated mean PSD");

            var scalars = Vector.LoadScalars(Folder).GetProperty("psd_mean_unshifted");
            Vector.AssertClose(psd[0], scalars.GetProperty("at_0_0").GetDouble(), 1e-9, "psd[0,0]");
            Vector.AssertClose(psd[1], scalars.GetProperty("at_0_1").GetDouble(), 1e-9, "psd[0,1]");
            Vector.AssertClose(psd[Size], scalars.GetProperty("at_1_0").GetDouble(), 1e-9, "psd[1,0]");
            Vector.AssertClose(psd[64 * Size + 64], scalars.GetProperty("at_64_64").GetDouble(), 1e-9, "psd[64,64]");
        }

        [Fact]
        public void RealParsevalHoldsOnSensorDataWithALargeOffset() {
            var frames = Vector.LoadFrames(Folder, Frames, Size);
            var accumulator = new PowerSpectrumAccumulator(Size);
            accumulator.AddFrame(frames[0], true, null);
            var last = new double[Length];
            accumulator.CopyLastPsd(last);

            var parseval = Vector.LoadScalars(Folder).GetProperty("parseval_frame0");
            var spectral = 0.0;
            foreach (var v in last) {
                spectral += v;
            }
            Vector.AssertClose(spectral / Length,
                parseval.GetProperty("sum_psd_over_n2").GetDouble(), 1e-11, "Parseval on real frame 0");
            Vector.AssertClose(spectral / Length,
                parseval.GetProperty("sum_pixels_squared").GetDouble(), 1e-11, "Parseval identity on real frame 0");
        }

        [Fact]
        public void RealPostProcessingChainMatchesThePythonReference() {
            var scalars = Vector.LoadScalars(Folder);
            var kRadius = scalars.GetProperty("optics").GetProperty("k_space_radius_px").GetDouble();
            var grid = new RadialGrid(Size);
            var fft = new Fft(Size);

            var psd = Vector.Accumulate(Vector.LoadFrames(Folder, Frames, Size), Size, true);
            Fft.FftShift(psd, Size);

            var pedestal = PowerSpectrum.RemovePhotonBias(psd, grid, kRadius);
            Vector.AssertClose(pedestal,
                scalars.GetProperty("photon_bias").GetProperty("pedestal").GetDouble(), 1e-9, "real photon bias pedestal");
            Vector.AssertMatches(psd, Vector.LoadBinary(Folder, "expected_psd_biasremoved_shifted.f64", Length),
                "real bias removed PSD");

            var flattened = new double[Length];
            var profile = PowerSpectrum.RadialFlatten(psd, grid, PowerSpectrum.DefaultFlattenSmooth,
                PowerSpectrum.DefaultFlattenFloorFraction, flattened);
            Vector.AssertMatches(profile, Vector.LoadBinary(Folder, "expected_radial_profile.f64", profile.Length),
                "real radial median profile");
            Vector.AssertMatches(flattened, Vector.LoadBinary(Folder, "expected_psd_flattened_shifted.f64", Length),
                "real radially flattened PSD");

            var re = new double[Length];
            var im = new double[Length];
            PowerSpectrum.Autocorrelation(psd, Size, fft, re, im);
            Vector.AssertMatches(re, Vector.LoadBinary(Folder, "expected_autocorr_shifted.f64", Length),
                "real autocorrelation");

            var scratch = new double[Length];
            FringeRenderer.BuildDisplay(flattened, grid, scratch, FringeStretchMode.Asinh, 1.0,
                FringeRenderer.DefaultLowPercentile, FringeRenderer.DefaultHighPercentile);
            Vector.AssertMatches(flattened, Vector.LoadBinary(Folder, "expected_display_gray.f64", Length),
                "real display image");
        }
    }
}
