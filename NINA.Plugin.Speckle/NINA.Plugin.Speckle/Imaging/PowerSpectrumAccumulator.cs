using System;

namespace NINA.Plugin.Speckle.Imaging {

    public sealed class PowerSpectrumAccumulator {
        private readonly double[] re;
        private readonly double[] im;
        private readonly double[] psdSum;
        private readonly double[] lastPsd;

        public PowerSpectrumAccumulator(int size) {
            Size = size;
            Transform = new Fft(size);
            var length = size * size;
            re = new double[length];
            im = new double[length];
            psdSum = new double[length];
            lastPsd = new double[length];
        }

        public int Size { get; }

        public Fft Transform { get; }

        public int FrameCount { get; private set; }

        public void Reset() {
            Array.Clear(psdSum, 0, psdSum.Length);
            Array.Clear(lastPsd, 0, lastPsd.Length);
            FrameCount = 0;
        }

        public void AddFrame(ushort[] pixels, bool subtractMean, double[] window) {
            var length = re.Length;
            if (pixels == null || pixels.Length < length) {
                throw new ArgumentException("Frame buffer is smaller than the transform size", nameof(pixels));
            }
            var offset = 0.0;
            if (subtractMean) {
                var sum = 0.0;
                for (var i = 0; i < length; i++) {
                    sum += pixels[i];
                }
                offset = sum / length;
            }
            if (window == null) {
                for (var i = 0; i < length; i++) {
                    re[i] = pixels[i] - offset;
                    im[i] = 0.0;
                }
            } else {
                for (var i = 0; i < length; i++) {
                    re[i] = (pixels[i] - offset) * window[i];
                    im[i] = 0.0;
                }
            }
            Accumulate();
        }

        public void AddFrame(double[] pixels, bool subtractMean, double[] window) {
            var length = re.Length;
            if (pixels == null || pixels.Length < length) {
                throw new ArgumentException("Frame buffer is smaller than the transform size", nameof(pixels));
            }
            var offset = 0.0;
            if (subtractMean) {
                var sum = 0.0;
                for (var i = 0; i < length; i++) {
                    sum += pixels[i];
                }
                offset = sum / length;
            }
            if (window == null) {
                for (var i = 0; i < length; i++) {
                    re[i] = pixels[i] - offset;
                    im[i] = 0.0;
                }
            } else {
                for (var i = 0; i < length; i++) {
                    re[i] = (pixels[i] - offset) * window[i];
                    im[i] = 0.0;
                }
            }
            Accumulate();
        }

        public void CopyMeanPsd(double[] destination) {
            if (destination == null || destination.Length < psdSum.Length) {
                throw new ArgumentException("Destination is too small", nameof(destination));
            }
            if (FrameCount == 0) {
                Array.Clear(destination, 0, psdSum.Length);
                return;
            }
            var scale = 1.0 / FrameCount;
            for (var i = 0; i < psdSum.Length; i++) {
                destination[i] = psdSum[i] * scale;
            }
        }

        public void CopyLastPsd(double[] destination) {
            if (destination == null || destination.Length < lastPsd.Length) {
                throw new ArgumentException("Destination is too small", nameof(destination));
            }
            Array.Copy(lastPsd, destination, lastPsd.Length);
        }

        private void Accumulate() {
            Transform.Forward(re, im);
            for (var i = 0; i < re.Length; i++) {
                var power = re[i] * re[i] + im[i] * im[i];
                lastPsd[i] = power;
                psdSum[i] += power;
            }
            FrameCount++;
        }
    }
}
