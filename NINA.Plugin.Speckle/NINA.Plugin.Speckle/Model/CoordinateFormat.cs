using System;
using System.Globalization;

namespace NINA.Plugin.Speckle.Model {

    public static class CoordinateFormat {
        private static bool useDecimalDegrees;

        public static event EventHandler Changed;

        public static bool UseDecimalDegrees {
            get => useDecimalDegrees;
            set {
                if (useDecimalDegrees == value) {
                    return;
                }
                useDecimalDegrees = value;
                Changed?.Invoke(null, EventArgs.Empty);
            }
        }

        public static string RaHours(double raHours) {
            if (double.IsNaN(raHours)) {
                return "";
            }
            if (useDecimalDegrees) {
                return (raHours * 15d).ToString("0.00000", CultureInfo.InvariantCulture);
            }
            var totalSeconds = raHours * 3600d;
            var hours = (int)(totalSeconds / 3600);
            var minutes = (int)((totalSeconds % 3600) / 60);
            var seconds = (int)Math.Round(totalSeconds % 60);
            if (seconds == 60) { seconds = 0; minutes++; }
            if (minutes == 60) { minutes = 0; hours++; }
            return string.Format(CultureInfo.InvariantCulture, "{0:00} {1:00} {2:00}", hours, minutes, seconds);
        }

        public static string DecDegrees(double decDeg) {
            if (double.IsNaN(decDeg)) {
                return "";
            }
            if (useDecimalDegrees) {
                return decDeg.ToString("+0.00000;-0.00000", CultureInfo.InvariantCulture);
            }
            var sign = decDeg < 0 ? "-" : "+";
            var abs = Math.Abs(decDeg);
            var degrees = (int)abs;
            var minutesFull = (abs - degrees) * 60d;
            var mm = (int)minutesFull;
            var ss = (int)Math.Round((minutesFull - mm) * 60);
            if (ss == 60) { ss = 0; mm++; }
            if (mm == 60) { mm = 0; degrees++; }
            return string.Format(CultureInfo.InvariantCulture, "{0}{1:00} {2:00} {3:00}", sign, degrees, mm, ss);
        }
    }
}
