using System;
using System.Runtime.CompilerServices;
using System.ComponentModel.Composition;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;

namespace NINA.Plugin.Speckle.Dockables {

    [Export(typeof(ResourceDictionary))]
    public partial class DataTemplates : ResourceDictionary {
        private const double ColumnFillTolerance = 12d;

        public DataTemplates() {
            InitializeComponent();
        }

        private void OnPanelLoaded(object sender, RoutedEventArgs e) {
            ((sender as FrameworkElement)?.DataContext as SpeckleDockableVM)?.RefreshAfterReload();
        }

        private static readonly ConditionalWeakTable<DataGrid, DataGridLength[]> declaredColumnWidths =
            new ConditionalWeakTable<DataGrid, DataGridLength[]>();

        private void OnTargetTableLoaded(object sender, RoutedEventArgs e) {
            if (!(sender is DataGrid grid)) {
                return;
            }
            declaredColumnWidths.Remove(grid);
            var declared = new DataGridLength[grid.Columns.Count];
            for (var i = 0; i < grid.Columns.Count; i++) {
                declared[i] = grid.Columns[i].Width;
            }
            declaredColumnWidths.Add(grid, declared);
            grid.Dispatcher.BeginInvoke(new Action(() => ApplyDeclaredWidths(grid)), DispatcherPriority.Loaded);
        }

        private void OnTargetTableSizeChanged(object sender, SizeChangedEventArgs e) {
            if (e.NewSize.Width > 0) {
                ApplyDeclaredWidths(sender as DataGrid);
            }
        }

        private static void ApplyDeclaredWidths(DataGrid grid) {
            if (grid == null || grid.ActualWidth <= 0) {
                return;
            }
            if (!declaredColumnWidths.TryGetValue(grid, out var declared) || declared.Length != grid.Columns.Count) {
                return;
            }
            var fixedWidth = 0d;
            var starUnits = 0d;
            for (var i = 0; i < declared.Length; i++) {
                if (declared[i].IsStar) {
                    starUnits += declared[i].Value;
                } else {
                    fixedWidth += declared[i].Value;
                }
            }
            if (starUnits <= 0d) {
                return;
            }
            var available = grid.ActualWidth - fixedWidth - ColumnFillTolerance;
            if (available <= 0d) {
                return;
            }
            for (var i = 0; i < grid.Columns.Count; i++) {
                var column = grid.Columns[i];
                if (!declared[i].IsStar) {
                    column.Width = declared[i];
                    continue;
                }
                var share = available * declared[i].Value / starUnits;
                if (share < column.MinWidth) {
                    share = column.MinWidth;
                }
                column.Width = new DataGridLength(share, DataGridLengthUnitType.Pixel);
            }
        }
    }

    public class ReferenceEqualityMultiConverter : IMultiValueConverter {

        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) {
            if (values == null || values.Length < 2) {
                return false;
            }
            return ReferenceEquals(values[0], values[1]) && values[0] != null;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) {
            throw new NotSupportedException();
        }
    }

    public class CaptureEntryProgressConverter : IMultiValueConverter {

        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) {
            if (values == null || values.Length < 4) {
                return 0d;
            }
            if (!TryReadInt(values[0], out var index)
                || !TryReadInt(values[1], out var activeNumber)
                || !TryReadInt(values[2], out var framesDone)
                || !TryReadInt(values[3], out var framesWanted)) {
                return 0d;
            }
            var position = index + 1;
            if (position < activeNumber) {
                return 1d;
            }
            if (position > activeNumber || framesWanted <= 0) {
                return 0d;
            }
            return Math.Clamp((double)framesDone / framesWanted, 0d, 1d);
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) {
            throw new NotSupportedException();
        }

        private static bool TryReadInt(object value, out int result) {
            result = 0;
            if (value == null || value == DependencyProperty.UnsetValue) {
                return false;
            }
            try {
                result = System.Convert.ToInt32(value, CultureInfo.InvariantCulture);
                return true;
            } catch (Exception) {
                return false;
            }
        }
    }

    public class ZeroBlankConverter : IValueConverter {

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
            if (value is double seconds) {
                return seconds > 0 ? seconds.ToString(CultureInfo.CurrentCulture) : string.Empty;
            }
            if (value is int frameCount) {
                return frameCount > 0 ? frameCount.ToString(CultureInfo.CurrentCulture) : string.Empty;
            }
            return string.Empty;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
            var text = value as string;
            if (targetType == typeof(int)) {
                return int.TryParse(text, NumberStyles.Integer, culture, out var frameCount) && frameCount > 0 ? frameCount : 0;
            }
            return double.TryParse(text, NumberStyles.Float, culture, out var seconds) && seconds > 0 ? seconds : 0d;
        }
    }

    public class ZeroToVisibilityConverter : IValueConverter {

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
            var isUnset = value switch {
                double seconds => seconds <= 0,
                int frameCount => frameCount <= 0,
                _ => true
            };
            return isUnset ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
            throw new NotSupportedException();
        }
    }

    public class RoomForButtonConverter : IValueConverter {

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
            return value is bool wanted && wanted
                ? new System.Windows.GridLength(1, System.Windows.GridUnitType.Star)
                : System.Windows.GridLength.Auto;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
            throw new NotSupportedException();
        }
    }

    public class FileNameConverter : IValueConverter {

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
            var path = value as string;
            return string.IsNullOrEmpty(path) ? "" : System.IO.Path.GetFileName(path);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
            throw new NotSupportedException();
        }
    }
}
