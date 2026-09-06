using NINA.Astrometry;

namespace NINA.Plugin.Speckle.Services {

    public static class SkySeparation {

        public static double Arcmin(Coordinates from, Coordinates to) {
            var radians = SOFA.Seps(AstroUtil.ToRadians(from.RADegrees), AstroUtil.ToRadians(from.Dec),
                                    AstroUtil.ToRadians(to.RADegrees), AstroUtil.ToRadians(to.Dec));
            return AstroUtil.DegreeToArcmin(AstroUtil.ToDegree(radians));
        }
    }
}
