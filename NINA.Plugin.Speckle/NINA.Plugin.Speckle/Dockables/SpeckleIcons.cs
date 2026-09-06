using System.Windows;
using System.Windows.Media;

namespace NINA.Plugin.Speckle.Dockables {

    public static class SpeckleIcons {

        public static GeometryGroup PanelIcon { get; } = Build();

        private static GeometryGroup Build() {
            var geometry = new GeometryGroup { FillRule = FillRule.Nonzero };
            geometry.Children.Add(new EllipseGeometry(new Point(35, 40), 22, 22));
            geometry.Children.Add(new EllipseGeometry(new Point(78, 68), 13, 13));
            geometry.Children.Add(Geometry.Parse("M35,4 L39,30 L35,40 L31,30 Z M35,76 L39,50 L35,40 L31,50 Z"));
            geometry.Freeze();
            return geometry;
        }
    }
}
