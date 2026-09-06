using System;
using System.Windows;

namespace NINA.Plugin.Speckle.Services {

    public static class UiThread {

        public static void Post(Action action) {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.CheckAccess()) {
                action();
                return;
            }
            dispatcher.BeginInvoke(action);
        }

        public static void Send(Action action) {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.CheckAccess()) {
                action();
                return;
            }
            dispatcher.Invoke(action);
        }
    }
}
