using System;

namespace NINA.Plugin.Speckle.Services {

    public static class CameraCoolingRequest {
        public static event Action Interrupted;

        public static void Raise() {
            Interrupted?.Invoke();
        }
    }
}
