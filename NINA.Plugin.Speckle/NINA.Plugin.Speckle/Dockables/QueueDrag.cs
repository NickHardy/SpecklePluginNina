using NINA.Core.Utility;
using NINA.Plugin.Speckle.Model;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace NINA.Plugin.Speckle.Dockables {

    public static class QueueDrag {
        private const string Format = "SpeckleQueueTarget";

        private static Point origin;
        private static SpeckleTarget armed;
        private static SpeckleTarget payload;

        public static readonly DependencyProperty IsHandleProperty = DependencyProperty.RegisterAttached(
            "IsHandle", typeof(bool), typeof(QueueDrag), new PropertyMetadata(false, OnIsHandleChanged));

        public static readonly DependencyProperty IsTargetProperty = DependencyProperty.RegisterAttached(
            "IsTarget", typeof(bool), typeof(QueueDrag), new PropertyMetadata(false, OnIsTargetChanged));

        public static readonly DependencyProperty IsRowProperty = DependencyProperty.RegisterAttached(
            "IsRow", typeof(bool), typeof(QueueDrag), new PropertyMetadata(false, OnIsRowChanged));

        private static readonly DependencyProperty HighlightedProperty = DependencyProperty.RegisterAttached(
            "Highlighted", typeof(bool), typeof(QueueDrag), new PropertyMetadata(false));

        private static readonly DependencyProperty BackgroundBeforeDropProperty = DependencyProperty.RegisterAttached(
            "BackgroundBeforeDrop", typeof(Brush), typeof(QueueDrag), new PropertyMetadata(null));

        private static readonly Brush DropBrush = MakeDropBrush();

        private static Brush MakeDropBrush() {
            var brush = new SolidColorBrush(Color.FromArgb(0x66, 0x80, 0x80, 0x80));
            brush.Freeze();
            return brush;
        }

        public static bool GetIsHandle(DependencyObject element) => (bool)element.GetValue(IsHandleProperty);

        public static void SetIsHandle(DependencyObject element, bool value) => element.SetValue(IsHandleProperty, value);

        public static bool GetIsTarget(DependencyObject element) => (bool)element.GetValue(IsTargetProperty);

        public static void SetIsTarget(DependencyObject element, bool value) => element.SetValue(IsTargetProperty, value);

        public static bool GetIsRow(DependencyObject element) => (bool)element.GetValue(IsRowProperty);

        public static void SetIsRow(DependencyObject element, bool value) => element.SetValue(IsRowProperty, value);

        private static void OnIsHandleChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args) {
            if (dependencyObject is not FrameworkElement element) {
                return;
            }
            element.PreviewMouseLeftButtonDown -= OnHandleMouseDown;
            element.PreviewMouseMove -= OnHandleMouseMove;
            element.PreviewMouseLeftButtonUp -= OnHandleMouseUp;
            if ((bool)args.NewValue) {
                element.PreviewMouseLeftButtonDown += OnHandleMouseDown;
                element.PreviewMouseMove += OnHandleMouseMove;
                element.PreviewMouseLeftButtonUp += OnHandleMouseUp;
            }
        }

        private static void OnIsRowChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args) {
            if (dependencyObject is not FrameworkElement element) {
                return;
            }
            element.PreviewMouseLeftButtonDown -= OnRowMouseDown;
            element.PreviewMouseMove -= OnHandleMouseMove;
            element.PreviewMouseLeftButtonUp -= OnHandleMouseUp;
            if ((bool)args.NewValue) {
                element.PreviewMouseLeftButtonDown += OnRowMouseDown;
                element.PreviewMouseMove += OnHandleMouseMove;
                element.PreviewMouseLeftButtonUp += OnHandleMouseUp;
            }
        }

        private static void OnIsTargetChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args) {
            if (dependencyObject is not FrameworkElement element) {
                return;
            }
            element.DragOver -= OnDragOver;
            element.DragLeave -= OnDragLeave;
            element.Drop -= OnDrop;
            element.AllowDrop = (bool)args.NewValue;
            if ((bool)args.NewValue) {
                element.DragOver += OnDragOver;
                element.DragLeave += OnDragLeave;
                element.Drop += OnDrop;
            }
        }

        private static void OnHandleMouseDown(object sender, MouseButtonEventArgs e) {
            origin = e.GetPosition(null);
            armed = ResolveTarget(sender);
            Logger.Debug("Queue drag armed: " + (armed?.Name ?? "nothing"));
            e.Handled = true;
        }

        private static void OnRowMouseDown(object sender, MouseButtonEventArgs e) {
            if (SitsOnAControl(e.OriginalSource as DependencyObject)) {
                return;
            }
            origin = e.GetPosition(null);
            armed = ResolveTarget(sender);
            Logger.Debug("Queue drag armed from the row: " + (armed?.Name ?? "nothing"));
        }

        private static bool SitsOnAControl(DependencyObject node) {
            while (node != null) {
                if (node is System.Windows.Controls.Primitives.ButtonBase
                    || node is System.Windows.Controls.Primitives.TextBoxBase
                    || node is System.Windows.Controls.ComboBox) {
                    return true;
                }
                node = node is System.Windows.Media.Visual || node is System.Windows.Media.Media3D.Visual3D
                    ? VisualTreeHelper.GetParent(node)
                    : null;
            }
            return false;
        }

        private static void OnHandleMouseUp(object sender, MouseButtonEventArgs e) {
            armed = null;
        }

        private static void OnHandleMouseMove(object sender, MouseEventArgs e) {
            if (armed == null || e.LeftButton != MouseButtonState.Pressed) {
                return;
            }
            var delta = e.GetPosition(null) - origin;
            if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance
                && Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance) {
                return;
            }
            payload = armed;
            armed = null;
            Logger.Info("Queue drag started: " + payload.Name);
            try {
                DragDrop.DoDragDrop((DependencyObject)sender, new DataObject(Format, Format), DragDropEffects.Move);
            } catch (Exception ex) {
                Logger.Error("Queue drag failed", ex);
            } finally {
                payload = null;
            }
        }

        private static void OnDragOver(object sender, DragEventArgs e) {
            e.Effects = payload != null ? DragDropEffects.Move : DragDropEffects.None;
            Highlight(sender as FrameworkElement, payload != null);
            e.Handled = true;
        }

        private static void OnDragLeave(object sender, DragEventArgs e) {
            Highlight(sender as FrameworkElement, false);
        }

        private static void OnDrop(object sender, DragEventArgs e) {
            var element = sender as FrameworkElement;
            Highlight(element, false);
            e.Handled = true;
            var moved = payload;
            if (moved == null) {
                Logger.Warning("Queue drop arrived with nothing being dragged");
                return;
            }
            var vm = FindViewModel(element);
            if (vm == null) {
                Logger.Warning("Queue drop could not find the Speckle panel view model");
                return;
            }
            var over = element?.DataContext as SpeckleTarget;
            Logger.Info("Queue drop: " + moved.Name + " onto " + (over?.Name ?? "the end of the queue"));
            vm.DropInQueue(moved, over);
        }

        private static SpeckleTarget ResolveTarget(object sender) {
            var node = sender as DependencyObject;
            while (node != null) {
                if (node is FrameworkElement element && element.DataContext is SpeckleTarget target) {
                    return target;
                }
                node = VisualTreeHelper.GetParent(node);
            }
            return null;
        }

        private static void Highlight(FrameworkElement element, bool on) {
            if (element is not Border border) {
                return;
            }
            if (on) {
                if (border.GetValue(HighlightedProperty) is bool already && already) {
                    return;
                }
                border.SetValue(HighlightedProperty, true);
                border.SetValue(BackgroundBeforeDropProperty, border.Background);
                border.Background = DropBrush;
                return;
            }
            if (!(border.GetValue(HighlightedProperty) is bool highlighted) || !highlighted) {
                return;
            }
            border.Background = border.GetValue(BackgroundBeforeDropProperty) as Brush;
            border.ClearValue(BackgroundBeforeDropProperty);
            border.ClearValue(HighlightedProperty);
        }

        private static SpeckleDockableVM FindViewModel(DependencyObject start) {
            var node = start;
            while (node != null) {
                if (node is FrameworkElement element && element.DataContext is SpeckleDockableVM vm) {
                    return vm;
                }
                node = VisualTreeHelper.GetParent(node);
            }
            return null;
        }
    }
}
