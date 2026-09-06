using NINA.Plugin.Speckle.Imaging;
using System;
using Xunit;

namespace NINA.Plugin.Speckle.Test {

    public class PowerSpectrumPipelineTests {
        private const int FrameSize = TestVectorData.Size;
        private const double Tol = TestVectorData.Tolerance;

        private static double[] Accumulate(ushort[][] frames, bool subtractMean) {
            var accumulator = new PowerSpectrumAccumulator(FrameSize);
            foreach (var frame in frames) {
                accumulator.AddFrame(frame, subtractMean, null);
            }
            var psd = new double[FrameSize * FrameSize];
            accumulator.CopyMeanPsd(psd);
            return psd;
        }

        private static double[] BiasRemovedShifted(ushort[][] frames, out double pedestal, out RadialGrid grid) {
            var psd = Accumulate(frames, true);
            Fft.FftShift(psd, FrameSize);
            grid = new RadialGrid(FrameSize);
            pedestal = PowerSpectrum.RemovePhotonBias(psd, grid, TestVectorData.Scalar("optics", "k_space_radius_px"));
            return psd;
        }

        [Fact]
        public void Frames_MatchThePythonChecksum() {
            var frames = TestVectorData.LoadFrames();
            ulong sum = 0;
            var min = ushort.MaxValue;
            var max = (ushort)0;
            foreach (var frame in frames) {
                foreach (var v in frame) {
                    sum += v;
                    if (v < min) {
                        min = v;
                    }
                    if (v > max) {
                        max = v;
                    }
                }
            }
            Assert.Equal((ulong)TestVectorData.Scalar("frames_checksum", "sum_u64"), sum);
            Assert.Equal((int)TestVectorData.Scalar("frames_checksum", "min"), min);
            Assert.Equal((int)TestVectorData.Scalar("frames_checksum", "max"), max);
        }

        [Fact]
        public void Fft_RoundTripsAndSatisfiesParseval() {
            var fft = new Fft(FrameSize);
            var random = new Random(4242);
            var re = new double[FrameSize * FrameSize];
            var im = new double[FrameSize * FrameSize];
            for (var i = 0; i < re.Length; i++) {
                re[i] = random.NextDouble() * 2000.0 - 1000.0;
            }
            var original = (double[])re.Clone();

            var sumSquares = 0.0;
            for (var i = 0; i < re.Length; i++) {
                sumSquares += re[i] * re[i];
            }

            fft.Forward(re, im);
            var sumPower = 0.0;
            for (var i = 0; i < re.Length; i++) {
                sumPower += re[i] * re[i] + im[i] * im[i];
            }
            Assert.True(Math.Abs(sumPower / (FrameSize * FrameSize) - sumSquares) / sumSquares < 1e-12,
                        "Parseval failed: " + sumPower / (FrameSize * FrameSize) + " vs " + sumSquares);

            fft.Inverse(re, im);
            for (var i = 0; i < re.Length; i++) {
                Assert.True(Math.Abs(re[i] - original[i]) < 1e-9, "Round trip failed at " + i);
                Assert.True(Math.Abs(im[i]) < 1e-9, "Round trip left an imaginary residue at " + i);
            }
        }

        [Fact]
        public void Fft_ImpulseTransformsToConstantMagnitude() {
            var fft = new Fft(FrameSize);
            var re = new double[FrameSize * FrameSize];
            var im = new double[FrameSize * FrameSize];
            re[0] = 7.0;
            fft.Forward(re, im);
            for (var i = 0; i < re.Length; i++) {
                Assert.True(Math.Abs(Math.Sqrt(re[i] * re[i] + im[i] * im[i]) - 7.0) < 1e-9);
            }
        }

        [Fact]
        public void AccumulatedPowerSpectrum_MatchesPython() {
            var psd = Accumulate(TestVectorData.LoadFrames(), true);
            var expected = TestVectorData.LoadBinary("expected_psd_mean_unshifted.f64");
            Assert.True(TestVectorData.RelativeError(psd, expected) <= Tol,
                        "relative error " + TestVectorData.RelativeError(psd, expected));

            Assert.Equal(0.0, psd[0], 6);
            AssertClose(TestVectorData.Scalar("psd_mean_unshifted", "at_1_0"), psd[1 * FrameSize + 0], 1e-9);
            AssertClose(TestVectorData.Scalar("psd_mean_unshifted", "at_0_1"), psd[0 * FrameSize + 1], 1e-9);
            AssertClose(TestVectorData.Scalar("psd_mean_unshifted", "at_10_20"), psd[10 * FrameSize + 20], 1e-9);
            AssertClose(TestVectorData.Scalar("psd_mean_unshifted", "at_63_63"), psd[63 * FrameSize + 63], 1e-9);
        }

        [Fact]
        public void AccumulatedPowerSpectrum_WithoutPreprocessing_MatchesPython() {
            var psd = Accumulate(TestVectorData.LoadFrames(), false);
            var expected = TestVectorData.LoadMatrix("expected_psd_mean_unshifted_noprep.csv");
            Assert.True(TestVectorData.RelativeError(psd, expected) <= Tol);
            AssertClose(TestVectorData.Scalar("psd_mean_unshifted_noprep", "at_0_0"), psd[0], 1e-9);
        }

        [Fact]
        public void SingleFramePowerSpectrum_MatchesPython() {
            var frames = TestVectorData.LoadFrames();
            var accumulator = new PowerSpectrumAccumulator(FrameSize);
            accumulator.AddFrame(frames[0], true, null);
            var last = new double[FrameSize * FrameSize];
            accumulator.CopyLastPsd(last);
            var expected = TestVectorData.LoadMatrix("expected_frame0_psd_unshifted.csv");
            Assert.True(TestVectorData.RelativeError(last, expected) <= Tol);

            var sum = 0.0;
            foreach (var v in last) {
                sum += v;
            }
            AssertClose(TestVectorData.Scalar("parseval_frame0", "sum_psd_over_n2"), sum / (FrameSize * FrameSize), 1e-9);
        }

        [Fact]
        public void ShiftedPowerSpectrum_MatchesPython() {
            var psd = Accumulate(TestVectorData.LoadFrames(), true);
            Fft.FftShift(psd, FrameSize);
            var expected = TestVectorData.LoadMatrix("expected_psd_mean_shifted.csv");
            Assert.True(TestVectorData.RelativeError(psd, expected) <= Tol);
        }

        [Fact]
        public void PhotonBiasRemoval_MatchesPython() {
            var psd = BiasRemovedShifted(TestVectorData.LoadFrames(), out var pedestal, out _);
            var expected = TestVectorData.LoadBinary("expected_psd_biasremoved_shifted.f64");
            Assert.True(TestVectorData.RelativeError(psd, expected) <= Tol);
            AssertClose(TestVectorData.Scalar("photon_bias", "pedestal"), pedestal, 1e-9);
        }

        [Fact]
        public void RadialMedianProfile_MatchesPython() {
            var psd = BiasRemovedShifted(TestVectorData.LoadFrames(), out _, out var grid);
            var profile = grid.MedianProfile(psd, PowerSpectrum.DefaultFlattenSmooth);
            var expected = TestVectorData.LoadMatrix("expected_radial_profile.csv");
            Assert.Equal(expected.Length, profile.Length);
            Assert.True(TestVectorData.RelativeError(profile, expected) <= Tol);
        }

        [Fact]
        public void RadialFlatten_MatchesPython() {
            var psd = BiasRemovedShifted(TestVectorData.LoadFrames(), out _, out var grid);
            var flat = new double[FrameSize * FrameSize];
            PowerSpectrum.RadialFlatten(psd, grid, PowerSpectrum.DefaultFlattenSmooth,
                                        PowerSpectrum.DefaultFlattenFloorFraction, flat);
            var expected = TestVectorData.LoadMatrix("expected_psd_flattened_shifted.csv");
            Assert.True(TestVectorData.RelativeError(flat, expected) <= Tol);
        }

        [Fact]
        public void Autocorrelation_MatchesPython() {
            var psd = BiasRemovedShifted(TestVectorData.LoadFrames(), out _, out _);
            var fft = new Fft(FrameSize);
            var re = new double[FrameSize * FrameSize];
            var im = new double[FrameSize * FrameSize];
            PowerSpectrum.Autocorrelation(psd, FrameSize, fft, re, im);
            var expected = TestVectorData.LoadMatrix("expected_autocorr_shifted.csv");
            Assert.True(TestVectorData.RelativeError(re, expected) <= Tol);
            AssertClose(TestVectorData.Scalar("autocorrelation", "at_center"), re[(FrameSize / 2) * FrameSize + FrameSize / 2], 1e-9);
        }

        [Fact]
        public void DisplayImage_MatchesPython() {
            var psd = BiasRemovedShifted(TestVectorData.LoadFrames(), out _, out var grid);
            var flat = new double[FrameSize * FrameSize];
            PowerSpectrum.RadialFlatten(psd, grid, PowerSpectrum.DefaultFlattenSmooth,
                                        PowerSpectrum.DefaultFlattenFloorFraction, flat);
            var scratch = new double[FrameSize * FrameSize];
            FringeRenderer.BuildDisplay(flat, grid, scratch, FringeStretchMode.Asinh, 1.0,
                                        FringeRenderer.DefaultLowPercentile, FringeRenderer.DefaultHighPercentile);
            var expected = TestVectorData.LoadMatrix("expected_display_gray.csv");
            Assert.True(TestVectorData.RelativeError(flat, expected) <= Tol);
        }

        [Fact]
        public void Metrics_MatchPython() {
            var psd = BiasRemovedShifted(TestVectorData.LoadFrames(), out _, out var grid);
            var kRadius = TestVectorData.Scalar("optics", "k_space_radius_px");
            var fft = new Fft(FrameSize);
            var work = new double[FrameSize * FrameSize];
            var scratch = new double[FrameSize * FrameSize];
            var detection = FringeMetrics.DetectOffset(psd, grid, fft,
                                                       FringeMetrics.ExclusionRadius(FrameSize, kRadius),
                                                       FrameSize / 2.0 - 2.0, work, scratch);

            AssertClose(TestVectorData.Scalar("metrics", "d_px"), detection.Offset, 1e-9);
            AssertClose(TestVectorData.Scalar("metrics", "dx"), detection.Dx, 1e-9);
            AssertClose(TestVectorData.Scalar("metrics", "dy"), detection.Dy, 1e-9);
            AssertClose(TestVectorData.Scalar("metrics", "period_px"), detection.PeriodPx, 1e-9);
            AssertClose(TestVectorData.Scalar("metrics", "pa_det_deg"), detection.PositionAngleDeg, 1e-9);
            AssertClose(TestVectorData.Scalar("metrics", "lobe_snr"), detection.LobeSnr, 1e-9);

            var highPass = Math.Clamp(detection.PeriodPx, 3.0, 0.5 * kRadius);
            AssertClose(TestVectorData.Scalar("metrics", "high_pass_radius"), highPass, 1e-9);
            var visibility = FringeMetrics.FringeVisibility(psd, grid, detection.PeriodPx, detection.PositionAngleDeg,
                                                            kRadius, highPass, out var projection);
            AssertClose(TestVectorData.Scalar("metrics", "fv"), projection, 1e-9);
            AssertClose(TestVectorData.Scalar("metrics", "visibility"), visibility, 1e-9);
            AssertClose(TestVectorData.Scalar("metrics", "ratio_from_v"), FringeMetrics.RatioFromVisibility(visibility), 1e-9);
        }

        [Fact]
        public void TwoDeltaPowerSpectrum_MatchesClosedFormAndIsNotTransposed() {
            var frame = new double[FrameSize * FrameSize];
            frame[32 * FrameSize + 32] = 1000.0;
            frame[32 * FrameSize + 37] = 1000.0;

            var accumulator = new PowerSpectrumAccumulator(FrameSize);
            accumulator.AddFrame(frame, false, null);
            var psd = new double[FrameSize * FrameSize];
            accumulator.CopyMeanPsd(psd);
            Fft.FftShift(psd, FrameSize);

            var expected = TestVectorData.LoadMatrix("analytic_two_delta_psd_shifted.csv");
            Assert.True(TestVectorData.RelativeError(psd, expected) <= Tol);

            for (var x = 0; x < FrameSize; x++) {
                var predicted = 1000.0 * 1000.0 * (2.0 + 2.0 * Math.Cos(2.0 * Math.PI * 5.0 * (x - 32) / FrameSize));
                Assert.True(Math.Abs(psd[32 * FrameSize + x] - predicted) <= 1e-6 * 4.0e6,
                            "closed form mismatch at x=" + x);
            }

            var columnSpread = 0.0;
            for (var y = 0; y < FrameSize; y++) {
                columnSpread = Math.Max(columnSpread, Math.Abs(psd[y * FrameSize + 40] - psd[32 * FrameSize + 40]));
            }
            Assert.True(columnSpread <= 1e-6 * 4.0e6, "the pattern varies along y, so the [x,y] order is transposed");
        }

        [Fact]
        public void AveragingTheFramesFirstDestroysTheFringes() {
            var frames = TestVectorData.LoadFrames();
            var kRadius = TestVectorData.Scalar("optics", "k_space_radius_px");
            var grid = new RadialGrid(FrameSize);
            var fft = new Fft(FrameSize);
            var work = new double[FrameSize * FrameSize];
            var scratch = new double[FrameSize * FrameSize];

            var correct = Accumulate(frames, true);
            Fft.FftShift(correct, FrameSize);
            PowerSpectrum.RemovePhotonBias(correct, grid, kRadius);
            var correctDetection = FringeMetrics.DetectOffset(correct, grid, fft,
                                                              FringeMetrics.ExclusionRadius(FrameSize, kRadius),
                                                              FrameSize / 2.0 - 2.0, work, scratch);

            var meanFrame = new double[FrameSize * FrameSize];
            foreach (var frame in frames) {
                for (var i = 0; i < meanFrame.Length; i++) {
                    meanFrame[i] += frame[i];
                }
            }
            for (var i = 0; i < meanFrame.Length; i++) {
                meanFrame[i] /= frames.Length;
            }
            var wrongAccumulator = new PowerSpectrumAccumulator(FrameSize);
            wrongAccumulator.AddFrame(meanFrame, true, null);
            var wrong = new double[FrameSize * FrameSize];
            wrongAccumulator.CopyMeanPsd(wrong);
            Fft.FftShift(wrong, FrameSize);
            PowerSpectrum.RemovePhotonBias(wrong, grid, kRadius);
            var wrongDetection = FringeMetrics.DetectOffset(wrong, grid, fft,
                                                            FringeMetrics.ExclusionRadius(FrameSize, kRadius),
                                                            FrameSize / 2.0 - 2.0, work, scratch);

            Assert.True(correctDetection.LobeSnr > 6.0, "accumulated per-frame power spectra must detect the pair, got " + correctDetection.LobeSnr);
            Assert.True(wrongDetection.LobeSnr < 4.0, "averaging the frames first must not detect the pair, got " + wrongDetection.LobeSnr);
            Assert.True(correctDetection.LobeSnr > 2.0 * wrongDetection.LobeSnr);
        }

        [Fact]
        public void ChooseSize_PicksTheLargestPowerOfTwoThatFits() {
            Assert.Equal(64, FrameCrop.ChooseSize(256, 256, 64, 64, 512));
            Assert.Equal(512, FrameCrop.ChooseSize(2048, 2048, 512, 64, 512));
            Assert.Equal(256, FrameCrop.ChooseSize(256, 256, 1024, 64, 512));
            Assert.Equal(0, FrameCrop.ChooseSize(40, 40, 0, 64, 512));
        }

        [Fact]
        public void ChooseSize_WithoutARequestStopsAtTheAutomaticSize() {
            Assert.Equal(FrameCrop.AutomaticSize, FrameCrop.ChooseSize(2048, 2048, 0, 64, 512));
            Assert.Equal(FrameCrop.AutomaticSize, FrameCrop.ChooseSize(600, 600, 0, 64, 1024));
            Assert.Equal(128, FrameCrop.ChooseSize(200, 160, 0, 64, 512));
        }

        [Fact]
        public void FrameCrop_UsesTheFrameCentreWhenTheFrameIsAlreadyTheRoi() {
            var pixels = new ushort[FrameSize * FrameSize];
            for (var i = 0; i < pixels.Length; i++) {
                pixels[i] = (ushort)i;
            }
            var destination = new ushort[FrameSize * FrameSize];
            FrameCrop.Crop(pixels, FrameSize, FrameSize, 900, 700, FrameSize, FrameSize, FrameSize, destination);
            Assert.Equal(pixels, destination);
        }

        [Fact]
        public void FrameCrop_CentresOnTheRoiOfAFullFrame() {
            var width = 200;
            var height = 160;
            var pixels = new ushort[width * height];
            for (var y = 0; y < height; y++) {
                for (var x = 0; x < width; x++) {
                    pixels[y * width + x] = (ushort)(x + y * 1000);
                }
            }
            var size = FrameCrop.ChooseSize(width, height, 0, 64, 512);
            Assert.Equal(128, size);

            var destination = new ushort[size * size];
            FrameCrop.Crop(pixels, width, height, 40, 20, 64, 64, size, destination);

            var startX = Math.Clamp(72 - size / 2, 0, width - size);
            var startY = Math.Clamp(52 - size / 2, 0, height - size);
            Assert.Equal(8, startX);
            Assert.Equal(0, startY);
            Assert.Equal(pixels[startY * width + startX], destination[0]);
            Assert.Equal(pixels[(startY + 1) * width + startX + 1], destination[size + 1]);
            Assert.Equal(pixels[(startY + size - 1) * width + startX + size - 1], destination[size * size - 1]);
        }

        [Fact]
        public void FrameCrop_ClampsTheWindowInsideTheFrame() {
            var width = 128;
            var height = 128;
            var pixels = new ushort[width * height];
            for (var i = 0; i < pixels.Length; i++) {
                pixels[i] = (ushort)(i % 1000);
            }
            var destination = new ushort[64 * 64];
            FrameCrop.Crop(pixels, width, height, 0, 0, 8, 8, 64, destination);
            Assert.Equal(pixels[0], destination[0]);
        }

        private static void AssertClose(double expected, double actual, double tolerance) {
            var scale = Math.Max(1.0, Math.Abs(expected));
            Assert.True(Math.Abs(expected - actual) <= tolerance * scale,
                        "expected " + expected.ToString("R") + " but got " + actual.ToString("R"));
        }
    }
}
