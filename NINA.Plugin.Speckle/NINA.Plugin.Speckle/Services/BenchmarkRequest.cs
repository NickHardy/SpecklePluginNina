using System;

namespace NINA.Plugin.Speckle.Services {

    public static class BenchmarkRequest {

        public static event EventHandler Requested;

        public static void Raise() {
            Requested?.Invoke(null, EventArgs.Empty);
        }
    }
}
