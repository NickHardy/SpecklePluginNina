using NINA.Core.Utility;
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NINA.Plugin.Speckle.Dockables {

    internal static class PanelBitmaps {

        public static BitmapSource MakeDisplayable(BitmapSource image) {
            try {
                if (image.IsFrozen) {
                    return image;
                }
                if (image.CanFreeze) {
                    image.Freeze();
                    Logger.Debug("Froze the incoming bitmap for cross thread display");
                    return image;
                }
                if (image.Dispatcher == null || image.CheckAccess()) {
                    var copy = new WriteableBitmap(image);
                    if (copy.CanFreeze) {
                        copy.Freeze();
                    }
                    Logger.Debug("Copied the incoming bitmap into a frozen bitmap for display");
                    return copy;
                }
                Logger.Warning("Incoming bitmap is owned by another thread and cannot be frozen; dropping the frame");
                return null;
            } catch (Exception ex) {
                Logger.Error("Could not make the incoming bitmap displayable", ex);
                return null;
            }
        }


        public static string DescribeImage(BitmapSource image) {
            if (image == null) {
                return "null";
            }
            try {
                var owned = image.Dispatcher == null
                    ? "frozen-or-free"
                    : (image.Dispatcher == Application.Current?.Dispatcher ? "ui-dispatcher" : "other-dispatcher");
                return image.PixelWidth + "x" + image.PixelHeight + " " + image.Format + " frozen " + image.IsFrozen + " " + owned;
            } catch (Exception ex) {
                return "unreadable from this thread (" + ex.GetType().Name + ")";
            }
        }


        public static BitmapSource MakeGrayBitmap(ushort[] pixels, int width, int height) {
            ushort min = ushort.MaxValue;
            ushort max = ushort.MinValue;
            foreach (var value in pixels) {
                if (value < min) { min = value; }
                if (value > max) { max = value; }
            }
            var span = Math.Max(1, max - min);
            var stretched = new ushort[pixels.Length];
            for (var i = 0; i < pixels.Length; i++) {
                stretched[i] = (ushort)Math.Clamp((pixels[i] - min) * 65535L / span, 0, ushort.MaxValue);
            }
            var bitmap = BitmapSource.Create(width, height, 96, 96, System.Windows.Media.PixelFormats.Gray16, null, stretched, width * sizeof(ushort));
            bitmap.Freeze();
            return bitmap;
        }

    }
}
