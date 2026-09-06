using NINA.Core.Utility;
using NINA.Plugin.Speckle.Workflow;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace NINA.Plugin.Speckle.Dockables {

    public enum ChainStepState {
        Pending,
        Active,
        Done,
        Error
    }

    public class ChainStepVM : BaseINPC {
        public const double ChipHeight = 44;
        public const double NotchWidth = 13;

        private const double LabelFontSize = 16;
        private const double LabelPadding = 15;

        private static readonly Typeface ChipTypeface = new Typeface(new FontFamily("Segoe UI"),
                                                                     FontStyles.Normal,
                                                                     FontWeights.SemiBold,
                                                                     FontStretches.Normal);

        private ChainStepState state;
        private bool isFirst;

        public ChainStepVM(string label, WorkflowPhase phase, bool isReference) {
            Label = label;
            Phase = phase;
            IsReference = isReference;
            ChipWidth = MeasureLabel(label) + NotchWidth * 2 + LabelPadding * 2;
            RebuildGeometry();
        }

        public string Label { get; }

        public WorkflowPhase Phase { get; }

        public bool IsReference { get; }

        public double ChipWidth { get; }

        public Geometry ChipGeometry { get; private set; }

        public Thickness ChipMargin => isFirst ? new Thickness(0) : new Thickness(-NotchWidth, 0, 0, 0);

        public ChainStepState State {
            get => state;
            set { state = value; RaisePropertyChanged(); }
        }

        public bool IsFirst {
            get => isFirst;
            set {
                isFirst = value;
                RebuildGeometry();
                RaisePropertyChanged();
                RaisePropertyChanged(nameof(ChipGeometry));
                RaisePropertyChanged(nameof(ChipMargin));
            }
        }

        private void RebuildGeometry() {
            var middle = ChipHeight / 2;
            var shoulder = ChipWidth - NotchWidth;
            var outline = new PathFigure { StartPoint = new Point(0, 0), IsClosed = true };
            outline.Segments.Add(new LineSegment(new Point(shoulder, 0), true));
            outline.Segments.Add(new LineSegment(new Point(ChipWidth, middle), true));
            outline.Segments.Add(new LineSegment(new Point(shoulder, ChipHeight), true));
            outline.Segments.Add(new LineSegment(new Point(0, ChipHeight), true));
            if (!isFirst) {
                outline.Segments.Add(new LineSegment(new Point(NotchWidth, middle), true));
            }
            var geometry = new PathGeometry();
            geometry.Figures.Add(outline);
            geometry.Freeze();
            ChipGeometry = geometry;
        }

        private static double MeasureLabel(string label) {
            var measured = new FormattedText(label ?? string.Empty,
                                             CultureInfo.CurrentUICulture,
                                             FlowDirection.LeftToRight,
                                             ChipTypeface,
                                             LabelFontSize,
                                             Brushes.Black,
                                             1.0);
            return measured.WidthIncludingTrailingWhitespace;
        }
    }
}
