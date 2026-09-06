using NINA.Astrometry;
using NINA.Core.Model;
using NINA.Plugin.Speckle.Model;
using System;
using System.Collections.Generic;

namespace NINA.Plugin.Speckle.Services {

    public static class AltTimeCalculator {

        public static List<AltTime> Compute(Coordinates coords, ObserverInfo observer, CustomHorizon horizon, double stepHours) {
            var start = DateTime.Now.AddHours(-1);
            var siderealTime = AstroUtil.GetLocalSiderealTime(start, observer.Longitude);
            var hourAngle = AstroUtil.GetHourAngle(siderealTime, coords.RA);
            var end = DateTime.Now.AddHours(10).AddHours(1);
            var siderealEndTime = AstroUtil.GetLocalSiderealTime(end, observer.Longitude);
            var hourEndAngle = AstroUtil.GetHourAngle(siderealEndTime, coords.RA);
            if (hourEndAngle < hourAngle) {
                hourEndAngle += 24;
            }

            List<AltTime> altList = new List<AltTime>();
            for (double angle = hourAngle; angle < hourEndAngle; angle += stepHours) {
                var degAngle = AstroUtil.HoursToDegrees(angle);
                var altitude = AstroUtil.GetAltitude(degAngle, observer.Latitude, coords.Dec);
                var azimuth = AstroUtil.GetAzimuth(degAngle, altitude, observer.Latitude, coords.Dec);
                var horizonAltitude = 0d;
                if (horizon != null) {
                    horizonAltitude = horizon.GetAltitude(azimuth);
                }
                if (altitude > horizonAltitude) {
                    altList.Add(new AltTime(altitude, azimuth, degAngle, start, AstroUtil.Airmass(altitude), CalculateMoonSeparation(start, coords, observer)));
                }
                start = start.AddHours(stepHours);
            }
            return altList;
        }

        public static double CalculateMoonSeparation(DateTime time, Coordinates coords, ObserverInfo observer) {
            NOVAS.SkyPosition pos = AstroUtil.GetMoonPosition(time, AstroUtil.GetJulianDate(time), observer);
            var moonRaRadians = AstroUtil.ToRadians(AstroUtil.HoursToDegrees(pos.RA));
            var moonDecRadians = AstroUtil.ToRadians(pos.Dec);

            Coordinates target = coords.Transform(Epoch.JNOW);
            var targetRaRadians = AstroUtil.ToRadians(target.RADegrees);
            var targetDecRadians = AstroUtil.ToRadians(target.Dec);

            var theta = SOFA.Seps(moonRaRadians, moonDecRadians, targetRaRadians, targetDecRadians);
            return AstroUtil.ToDegree(theta);
        }
    }
}
