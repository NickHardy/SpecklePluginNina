using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace NINA.Plugin.Speckle.Dockables.Kepler {

    public partial class KeplerSkyView : UserControl {
        private const double ClickThreshold = 4d;
        private Point downPoint;
        private Point lastPoint;
        private bool pressed;
        private bool dragging;

        public KeplerSkyView() {
            InitializeComponent();
        }

        private KeplerSkyVM ViewModel => DataContext as KeplerSkyVM;

        private void OnSurfaceSizeChanged(object sender, SizeChangedEventArgs e) {
            var vm = ViewModel;
            if (vm == null) {
                return;
            }
            vm.SurfaceWidth = e.NewSize.Width;
            vm.SurfaceHeight = e.NewSize.Height;
        }

        private void OnInputMouseDown(object sender, MouseButtonEventArgs e) {
            var vm = ViewModel;
            if (vm == null) {
                return;
            }
            if (e.ClickCount == 2) {
                pressed = false;
                dragging = false;
                Input.ReleaseMouseCapture();
                Input.Cursor = Cursors.Arrow;
                vm.ResetView();
                return;
            }
            downPoint = e.GetPosition(Surface);
            lastPoint = downPoint;
            pressed = true;
            dragging = false;
            Input.CaptureMouse();
        }

        private void OnInputMouseMove(object sender, MouseEventArgs e) {
            var vm = ViewModel;
            if (vm == null) {
                return;
            }
            var position = e.GetPosition(Surface);
            if (pressed && e.LeftButton != MouseButtonState.Pressed) {
                EndInput();
            }
            if (pressed) {
                if (!dragging
                    && (Math.Abs(position.X - downPoint.X) >= ClickThreshold || Math.Abs(position.Y - downPoint.Y) >= ClickThreshold)) {
                    dragging = true;
                    Input.Cursor = Cursors.SizeAll;
                }
                if (dragging) {
                    vm.Pan(position.X - lastPoint.X, position.Y - lastPoint.Y);
                    lastPoint = position;
                }
                return;
            }
            Input.Cursor = vm.Hover(position) != null ? Cursors.Hand : Cursors.Arrow;
        }

        private void OnInputMouseUp(object sender, MouseButtonEventArgs e) {
            var wasPressed = pressed;
            var wasDragging = dragging;
            EndInput();
            var vm = ViewModel;
            if (vm == null || !wasPressed || wasDragging) {
                return;
            }
            vm.Select(vm.HitTest(e.GetPosition(Surface)));
        }

        private void OnInputLostMouseCapture(object sender, MouseEventArgs e) {
            EndInput();
        }

        private void EndInput() {
            pressed = false;
            dragging = false;
            Input.ReleaseMouseCapture();
            Input.Cursor = Cursors.Arrow;
        }

        private void OnInputMouseWheel(object sender, MouseWheelEventArgs e) {
            var vm = ViewModel;
            if (vm == null) {
                return;
            }
            vm.Zoom(e.Delta, e.GetPosition(Surface));
            e.Handled = true;
        }

        private void OnInputMouseLeave(object sender, MouseEventArgs e) {
            ViewModel?.Hover(null);
        }
    }
}
