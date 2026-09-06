using NINA.Core.Utility;
using System;
using System.Reflection;
using System.Windows;
using System.Windows.Threading;

namespace NINA.Plugin.Speckle.Dockables {

    public static class ZoomToFit {
        private const string ResetMethod = "ButtonZoomReset_Click";

        public static readonly DependencyProperty TokenProperty = DependencyProperty.RegisterAttached(
            "Token", typeof(int), typeof(ZoomToFit), new PropertyMetadata(0, OnTokenChanged));

        public static void SetToken(DependencyObject element, int value) {
            element.SetValue(TokenProperty, value);
        }

        public static int GetToken(DependencyObject element) {
            return (int)element.GetValue(TokenProperty);
        }

        private static void OnTokenChanged(DependencyObject element, DependencyPropertyChangedEventArgs args) {
            if (!(element is FrameworkElement view) || !(args.NewValue is int token) || token <= 0) {
                return;
            }
            view.Dispatcher.BeginInvoke(new Action(() => Fit(view)), DispatcherPriority.Loaded);
        }

        private static void Fit(FrameworkElement view) {
            if (!view.IsLoaded) {
                return;
            }
            var reset = view.GetType().GetMethod(ResetMethod, BindingFlags.Instance | BindingFlags.NonPublic);
            if (reset == null) {
                Logger.Warning("The image view of this NINA build has no " + ResetMethod + "; the zoom was left as it was");
                return;
            }
            try {
                reset.Invoke(view, new object[] { view, new RoutedEventArgs() });
                Logger.Info("Image zoom set back to fit the whole frame");
            } catch (Exception ex) {
                Logger.Error("Could not set the image zoom back to fit the whole frame", ex);
            }
        }
    }
}
