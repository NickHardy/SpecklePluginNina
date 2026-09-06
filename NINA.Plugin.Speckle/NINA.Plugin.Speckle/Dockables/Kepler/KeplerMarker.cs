using NINA.Core.Utility;
using NINA.Plugin.Speckle.Model;

namespace NINA.Plugin.Speckle.Dockables.Kepler {

    public enum KeplerMarkerState {
        Pending,
        Queued,
        Current,
        Done,
        Excluded,
        Unscheduled
    }

    public class KeplerMarker : BaseINPC {
        private double positionX;
        private double positionY;
        private string number = string.Empty;
        private KeplerMarkerState state = KeplerMarkerState.Pending;
        private bool isAboveHorizon;
        private bool isHovered;
        private bool showName;

        public KeplerMarker(SpeckleTarget target) {
            Target = target;
        }

        public SpeckleTarget Target { get; }

        public string Label => Target.Name;

        public int OrderIndex { get; set; } = -1;

        public string OrderNote { get; set; } = string.Empty;

        public double X {
            get => positionX;
            set { if (positionX != value) { positionX = value; RaisePropertyChanged(); } }
        }

        public double Y {
            get => positionY;
            set { if (positionY != value) { positionY = value; RaisePropertyChanged(); } }
        }

        public double Altitude { get; set; }

        public double Azimuth { get; set; }

        public string Number {
            get => number;
            set { if (number != value) { number = value; RaisePropertyChanged(); } }
        }

        public KeplerMarkerState State {
            get => state;
            set {
                if (state == value) {
                    return;
                }
                state = value;
                RaisePropertyChanged();
                RaisePropertyChanged(nameof(IsSelectable));
            }
        }

        public bool IsAboveHorizon {
            get => isAboveHorizon;
            set { if (isAboveHorizon != value) { isAboveHorizon = value; RaisePropertyChanged(); } }
        }

        public bool IsHovered {
            get => isHovered;
            set { if (isHovered != value) { isHovered = value; RaisePropertyChanged(); } }
        }

        public bool ShowName {
            get => showName;
            set { if (showName != value) { showName = value; RaisePropertyChanged(); } }
        }

        public bool IsSelectable =>
            state != KeplerMarkerState.Done
            && state != KeplerMarkerState.Excluded
            && state != KeplerMarkerState.Unscheduled
            && state != KeplerMarkerState.Current;
    }

    public class KeplerLabel : BaseINPC {
        private double positionX;
        private double positionY;

        public KeplerLabel(string text) {
            Text = text;
        }

        public string Text { get; }

        public double X {
            get => positionX;
            set { positionX = value; RaisePropertyChanged(); }
        }

        public double Y {
            get => positionY;
            set { positionY = value; RaisePropertyChanged(); }
        }
    }
}
