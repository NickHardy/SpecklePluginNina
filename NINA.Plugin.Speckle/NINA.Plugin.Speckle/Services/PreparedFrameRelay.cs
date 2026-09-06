using NINA.Image.Interfaces;
using System;
using System.Threading;

namespace NINA.Plugin.Speckle.Services {

    public static class PreparedFrameRelay {
        private static int captureLoopsHoldingDisplay;

        public static event EventHandler<IRenderedImage> Published;

        public static bool CaptureLoopOwnsDisplay => Volatile.Read(ref captureLoopsHoldingDisplay) > 0;

        public static void TakeOverDisplay() {
            Interlocked.Increment(ref captureLoopsHoldingDisplay);
        }

        public static void HandDisplayBack() {
            if (Interlocked.Decrement(ref captureLoopsHoldingDisplay) < 0) {
                Interlocked.Exchange(ref captureLoopsHoldingDisplay, 0);
            }
        }

        public static void Publish(IRenderedImage renderedImage) {
            if (renderedImage != null) {
                Published?.Invoke(null, renderedImage);
            }
        }
    }
}
