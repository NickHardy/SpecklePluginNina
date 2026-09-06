using System;
using System.ComponentModel.Composition;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace NINA.Plugin.Speckle.Dockables {

    [Export(typeof(ResourceDictionary))]
    public partial class DataTemplates : ResourceDictionary {

        public DataTemplates() {
            InitializeComponent();
        }

        private void OnPanelLoaded(object sender, RoutedEventArgs e) {
            ((sender as FrameworkElement)?.DataContext as SpeckleDockableVM)?.RefreshAfterReload();
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
