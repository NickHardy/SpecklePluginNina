using NINA.Astrometry;
using NINA.Plugin.Speckle.Model;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace NINA.Plugin.Speckle.Dockables.Kepler {

    public static class KeplerProjection {
        public const double MinScale = 1d;
        public const double MaxScale = 8d;
        public const double SiderealRate = 1.00273790935d;
        public const double SouthAtBottom = 180d;
        public const double NorthAtBottom = 0d;
        private const double MaxHalfWidth = 90d;
        private const double ArcStep = 3d;
        private const int BisectionSteps = 18;

        public static double Mod360(double degrees) {
            return AstroUtil.EuclidianModulus(degrees, 360d);
        }

        public static double RadiusAt(double altitude, double horizonRadius) {
            return horizonRadius * (90d - altitude) / 90d;
        }

        public static Point Project(double altitude, double azimuth, double azAtBottom, Point center, double horizonRadius) {
            var ringRadius = RadiusAt(altitude, horizonRadius);
            var theta = AstroUtil.ToRadians(180d - (azimuth - azAtBottom));
            return new Point(center.X + ringRadius * Math.Sin(theta), center.Y - ringRadius * Math.Cos(theta));
        }

        public static void AltAz(double raDegrees, double decDegrees, double latitude, double siderealHours, out double altitude, out double azimuth) {
            var hourAngle = AstroUtil.HoursToDegrees(AstroUtil.GetHourAngle(siderealHours, raDegrees / 15d));
            altitude = AstroUtil.GetAltitude(hourAngle, latitude, decDegrees);
            azimuth = AstroUtil.GetAzimuth(hourAngle, altitude, latitude, decDegrees);
        }

        public static double SiderealHours(double anchorHours, TimeSpan elapsed) {
            return anchorHours + elapsed.TotalHours * SiderealRate;
        }

        public static double[] SlitHalfWidths(double slitWidthDegrees, double altLow, double altHigh, int samples) {
            var count = Math.Max(2, samples);
            var values = new double[count];
            var span = altHigh - altLow;
            for (var i = 0; i < count; i++) {
                values[i] = HalfWidthAt(altLow + span * i / (count - 1), slitWidthDegrees);
            }
            return values;
        }

        public static Geometry SlitBand(double slitAzimuth, IReadOnlyList<double> halfWidths, double altLow, double altHigh, double azAtBottom, Point center, double horizonRadius) {
            if (halfWidths == null || halfWidths.Count < 2) {
                return Geometry.Empty;
            }
            if (RadiusAt(altLow, horizonRadius) - RadiusAt(altHigh, horizonRadius) < 0.5d) {
                return Geometry.Empty;
            }
            var last = halfWidths.Count - 1;
            var span = altHigh - altLow;
            var points = new PointCollection();
            AppendAzimuthArc(points, altLow, slitAzimuth - halfWidths[0], slitAzimuth + halfWidths[0], azAtBottom, center, horizonRadius);
            for (var i = 1; i <= last; i++) {
                points.Add(Project(altLow + span * i / last, slitAzimuth + halfWidths[i], azAtBottom, center, horizonRadius));
            }
            AppendAzimuthArc(points, altHigh, slitAzimuth + halfWidths[last], slitAzimuth - halfWidths[last], azAtBottom, center, horizonRadius);
            for (var i = last - 1; i >= 0; i--) {
                points.Add(Project(altLow + span * i / last, slitAzimuth - halfWidths[i], azAtBottom, center, horizonRadius));
            }
            if (points.Count < 3) {
                return Geometry.Empty;
            }
            var figure = new PathFigure { StartPoint = points[0], IsClosed = true, IsFilled = true };
            figure.Segments.Add(new PolyLineSegment(points, true));
            var geometry = new PathGeometry();
            geometry.Figures.Add(figure);
            geometry.Freeze();
            return geometry;
        }

        public static Geometry Rings(IEnumerable<double> altitudes, Point center, double horizonRadius) {
            var group = new GeometryGroup();
            foreach (var altitude in altitudes) {
                var ringRadius = RadiusAt(altitude, horizonRadius);
                if (ringRadius > 0.5d) {
                    group.Children.Add(new EllipseGeometry(center, ringRadius, ringRadius));
                }
            }
            group.Freeze();
            return group;
        }

        public static Geometry HorizonBlock(IReadOnlyList<double> altitudeByAzimuth, double azAtBottom, Point center, double horizonRadius) {
            if (altitudeByAzimuth == null || altitudeByAzimuth.Count < 3 || horizonRadius < 1d) {
                return Geometry.Empty;
            }
            var step = 360d / altitudeByAzimuth.Count;
            var points = new PointCollection();
            var anyAboveZero = false;
            for (var i = 0; i < altitudeByAzimuth.Count; i++) {
                var altitude = Math.Clamp(altitudeByAzimuth[i], 0d, 89d);
                anyAboveZero |= altitude > 0.05d;
                points.Add(Project(altitude, i * step, azAtBottom, center, horizonRadius));
            }
            if (!anyAboveZero) {
                return Geometry.Empty;
            }

            var visible = new PathFigure { StartPoint = points[0], IsClosed = true, IsFilled = true };
            visible.Segments.Add(new PolyLineSegment(points, false));
            var blocked = new PathGeometry { FillRule = FillRule.EvenOdd };
            blocked.Figures.Add(CircleFigure(center, horizonRadius));
            blocked.Figures.Add(visible);
            blocked.Freeze();
            return blocked;
        }

        public static Geometry Track(IEnumerable<Point> points) {
            var collection = new PointCollection(points);
            if (collection.Count < 2) {
                return Geometry.Empty;
            }
            var figure = new PathFigure { StartPoint = collection[0], IsClosed = false, IsFilled = false };
            figure.Segments.Add(new PolyLineSegment(collection, true));
            var geometry = new PathGeometry();
            geometry.Figures.Add(figure);
            geometry.Freeze();
            return geometry;
        }

        private static double HalfWidthAt(double altitude, double slitWidthDegrees) {
            if (!TargetBase.IsInsideDomeSlit(altitude, 0d, 0d, slitWidthDegrees)) {
                return 0d;
            }
            if (TargetBase.IsInsideDomeSlit(altitude, MaxHalfWidth, 0d, slitWidthDegrees)) {
                return MaxHalfWidth;
            }
            var low = 0d;
            var high = MaxHalfWidth;
            for (var i = 0; i < BisectionSteps; i++) {
                var mid = (low + high) / 2d;
                if (TargetBase.IsInsideDomeSlit(altitude, mid, 0d, slitWidthDegrees)) {
                    low = mid;
                } else {
                    high = mid;
                }
            }
            return low;
        }

        private static void AppendAzimuthArc(PointCollection points, double altitude, double fromAzimuth, double toAzimuth, double azAtBottom, Point center, double horizonRadius) {
            var sweep = toAzimuth - fromAzimuth;
            var steps = Math.Max(1, (int)Math.Ceiling(Math.Abs(sweep) / ArcStep));
            for (var i = 0; i <= steps; i++) {
                points.Add(Project(altitude, fromAzimuth + sweep * i / steps, azAtBottom, center, horizonRadius));
            }
        }

        private static PathFigure CircleFigure(Point center, double radius) {
            var top = new Point(center.X, center.Y - radius);
            var bottom = new Point(center.X, center.Y + radius);
            var size = new Size(radius, radius);
            var figure = new PathFigure { StartPoint = top, IsClosed = true, IsFilled = true };
            figure.Segments.Add(new ArcSegment(bottom, size, 0d, false, SweepDirection.Clockwise, false));
            figure.Segments.Add(new ArcSegment(top, size, 0d, false, SweepDirection.Clockwise, false));
            return figure;
        }
    }

    public class KeplerSlitProfile {
        private const int SampleCount = 40;

        private string key = string.Empty;
        private double[] halfWidths = Array.Empty<double>();
        private double altLow;
        private double altHigh;

        public Geometry Band(double slitAzimuth, double slitWidth, double low, double high, double azAtBottom, Point center, double horizonRadius) {
            var next = slitWidth.ToString("F3", CultureInfo.InvariantCulture)
                + "|" + low.ToString("F3", CultureInfo.InvariantCulture)
                + "|" + high.ToString("F3", CultureInfo.InvariantCulture);
            if (!string.Equals(next, key, StringComparison.Ordinal)) {
                key = next;
                altLow = low;
                altHigh = high;
                halfWidths = KeplerProjection.SlitHalfWidths(slitWidth, low, high, SampleCount);
            }
            return KeplerProjection.SlitBand(slitAzimuth, halfWidths, altLow, altHigh, azAtBottom, center, horizonRadius);
        }
    }
}
