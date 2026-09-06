using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace NINA.Plugin.Speckle.Dockables {

    public static class MirrorAnimation {
        private static readonly TimeSpan ShortestSweep = TimeSpan.FromMilliseconds(300);
        private static readonly TimeSpan LongestSweep = TimeSpan.FromMilliseconds(1500);
        private static readonly TimeSpan UnpacedSweep = TimeSpan.FromMilliseconds(600);

        private static readonly DependencyProperty LastChangeProperty = DependencyProperty.RegisterAttached(
            "LastChange",
            typeof(DateTime),
            typeof(MirrorAnimation),
            new PropertyMetadata(DateTime.MinValue));

        public static readonly DependencyProperty AngleProperty = DependencyProperty.RegisterAttached(
            "Angle",
            typeof(double),
            typeof(MirrorAnimation),
            new PropertyMetadata(0d, OnAngleChanged));

        public static readonly DependencyProperty FadeProperty = DependencyProperty.RegisterAttached(
            "Fade",
            typeof(double),
            typeof(MirrorAnimation),
            new PropertyMetadata(1d, OnFadeChanged));

        public static void SetAngle(DependencyObject element, double value) {
            element.SetValue(AngleProperty, value);
        }

        public static double GetAngle(DependencyObject element) {
            return (double)element.GetValue(AngleProperty);
        }

        public static void SetFade(DependencyObject element, double value) {
            element.SetValue(FadeProperty, value);
        }

        public static double GetFade(DependencyObject element) {
            return (double)element.GetValue(FadeProperty);
        }

        private static void OnAngleChanged(DependencyObject element, DependencyPropertyChangedEventArgs args) {
            if (!(element is UIElement rotated)) {
                return;
            }
            if (!(rotated.RenderTransform is RotateTransform rotation) || rotation.IsFrozen) {
                rotation = new RotateTransform();
                rotated.RenderTransform = rotation;
            }
            rotation.BeginAnimation(RotateTransform.AngleProperty, Glide((double)args.NewValue, SweepFor(element)));
        }

        private static void OnFadeChanged(DependencyObject element, DependencyPropertyChangedEventArgs args) {
            if (element is UIElement faded) {
                faded.BeginAnimation(UIElement.OpacityProperty, Glide((double)args.NewValue, SweepFor(element)));
            }
        }

        private static DoubleAnimation Glide(double to, Duration sweep) {
            return new DoubleAnimation(to, sweep) {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
            };
        }

        private static Duration SweepFor(DependencyObject element) {
            var now = DateTime.UtcNow;
            var previous = (DateTime)element.GetValue(LastChangeProperty);
            element.SetValue(LastChangeProperty, now);
            if (previous == DateTime.MinValue) {
                return new Duration(UnpacedSweep);
            }
            var sinceLastChange = now - previous;
            if (sinceLastChange < ShortestSweep) {
                return new Duration(ShortestSweep);
            }
            return new Duration(sinceLastChange > LongestSweep ? LongestSweep : sinceLastChange);
        }
    }
}
