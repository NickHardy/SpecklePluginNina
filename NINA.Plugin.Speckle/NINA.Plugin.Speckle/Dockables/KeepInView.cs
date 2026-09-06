using System.Windows;

namespace NINA.Plugin.Speckle.Dockables {

    public static class KeepInView {

        public static readonly DependencyProperty WhenActiveProperty = DependencyProperty.RegisterAttached(
            "WhenActive", typeof(bool), typeof(KeepInView), new PropertyMetadata(false, OnWhenActiveChanged));

        public static void SetWhenActive(DependencyObject element, bool value) {
            element.SetValue(WhenActiveProperty, value);
        }

        public static bool GetWhenActive(DependencyObject element) {
            return (bool)element.GetValue(WhenActiveProperty);
        }

        private static void OnWhenActiveChanged(DependencyObject element, DependencyPropertyChangedEventArgs args) {
            if (!(args.NewValue is bool wanted) || !wanted || !(element is FrameworkElement frameworkElement)) {
                return;
            }
            frameworkElement.Dispatcher.BeginInvoke(new System.Action(() => {
                if (frameworkElement.IsLoaded) {
                    frameworkElement.BringIntoView();
                }
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }
    }
}
