using NINA.Core.Utility;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows;

namespace NINA.Plugin.Speckle.Services {

    public class CaptureEntry : BaseINPC {
        private string filter = string.Empty;
        private double exposureTime;
        private int frameCount;

        public CaptureEntry() {
        }

        public CaptureEntry(string filter, double exposureTime, int frameCount) {
            this.filter = Normalize(filter);
            this.exposureTime = Clamp(exposureTime);
            this.frameCount = Clamp(frameCount);
        }

        public string Filter {
            get => filter;
            set {
                var wanted = Normalize(value);
                if (string.Equals(filter, wanted, StringComparison.Ordinal)) {
                    return;
                }
                Logger.Info("Capture entry filter changed from " + Describe(filter) + " to " + Describe(wanted));
                filter = wanted;
                RaisePropertyChanged();
            }
        }

        public double ExposureTime {
            get => exposureTime;
            set {
                var wanted = Clamp(value);
                if (exposureTime == wanted) {
                    return;
                }
                Logger.Info("Capture entry exposure changed from " + exposureTime + " s to " + wanted + " s for " + Describe(filter));
                exposureTime = wanted;
                RaisePropertyChanged();
            }
        }

        public int FrameCount {
            get => frameCount;
            set {
                var wanted = Clamp(value);
                if (frameCount == wanted) {
                    return;
                }
                Logger.Info("Capture entry frames changed from " + frameCount + " to " + wanted + " for " + Describe(filter));
                frameCount = wanted;
                RaisePropertyChanged();
            }
        }

        private static string Describe(string name) {
            return string.IsNullOrWhiteSpace(name) ? "no filter" : name;
        }

        public CaptureEntry Clone() {
            return new CaptureEntry(filter, exposureTime, frameCount);
        }

        public override string ToString() {
            return "filter " + (filter.Length > 0 ? filter : "none")
                + ", exposure " + (exposureTime > 0 ? exposureTime.ToString(CultureInfo.InvariantCulture) + " s" : "auto")
                + ", frames " + (frameCount > 0 ? frameCount.ToString(CultureInfo.InvariantCulture) : "default");
        }

        private static string Normalize(string value) {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }

        private static double Clamp(double value) {
            return value > 0 ? value : 0;
        }

        private static int Clamp(int value) {
            return value > 0 ? value : 0;
        }
    }

    public class CaptureEntryCollection : ObservableCollection<CaptureEntry> {

        public void AddEntry(CaptureEntry entry) {
            UiThread.Send(() => Add(entry));
        }

        public void RemoveEntry(CaptureEntry entry) {
            UiThread.Send(() => Remove(entry));
        }

        public void MoveEntry(int oldIndex, int newIndex) {
            UiThread.Send(() => Move(oldIndex, newIndex));
        }

        public void ResetTo(IEnumerable<CaptureEntry> entries) {
            var wanted = entries == null ? new List<CaptureEntry>() : entries.ToList();
            UiThread.Send(() => {
                Clear();
                foreach (var entry in wanted) {
                    Add(entry);
                }
            });
        }
    }

    public static class CapturePlan {

        public static IReadOnlyList<CaptureEntry> Single(string filter, double exposureTime, int frameCount) {
            return new List<CaptureEntry> { new CaptureEntry(filter, exposureTime, frameCount) };
        }

        public static List<CaptureEntry> Clone(IEnumerable<CaptureEntry> entries) {
            return entries == null ? new List<CaptureEntry>() : entries.Select(entry => entry.Clone()).ToList();
        }

        public static string Describe(IEnumerable<CaptureEntry> entries) {
            var list = entries?.ToList() ?? new List<CaptureEntry>();
            if (list.Count == 0) {
                return "no capture entries";
            }
            return list.Count + (list.Count == 1 ? " entry: " : " entries: ")
                + string.Join("; ", list.Select((entry, index) => "[" + (index + 1) + "] " + entry));
        }
    }
}
