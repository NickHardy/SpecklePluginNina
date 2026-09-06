#region "copyright"

/*
    Copyright © 2016 - 2021 Stefan Berg <isbeorn86+NINA@googlemail.com> and the N.I.N.A. contributors

    This file is part of N.I.N.A. - Nighttime Imaging 'N' Astronomy.

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using Newtonsoft.Json;
using NINA.Core.Model;
using NINA.Astrometry;
using CsvHelper.Configuration;
using System;
using System.Collections.Generic;
using NINA.Plugin.Speckle.Sequencer.Container;
using System.Linq;
using System.Threading;
using System.Runtime.ExceptionServices;

namespace NINA.Plugin.Speckle.Model {

    [JsonObject(MemberSerialization.OptIn)]
    public class TargetBase {
        static int nextId;
        public int TargetId { get; private set; }
        public TargetBase() {
            TargetId = Interlocked.Increment(ref nextId);
        }

        public List<AltTime> AltList { get; set; } = new List<AltTime>();
        
        public List<AltTime> DomeSlitAltTimeList { get; set; } = new List<AltTime>();
        [JsonProperty]
        public double DomeSlitObservationTime { get; set; }
        [JsonProperty]
        public DateTime DomeSlitObservationStartTime { get; set; }

        public AltTime MeridianAltTime() {
            return AltList.OrderByDescending((altTime) => altTime.Altitude).FirstOrDefault();
        }

        public AltTime ImageTo(NighttimeData nighttimeData, double altitudeCeiling = 90d, double mDistance = 5d, double airmassMin = 0d, double airmassMax = 4d, double distanceToMoon = 20d) {
            DateTime twilightSet = nighttimeData.NauticalTwilightRiseAndSet.Set ?? DateTime.Now;
            DateTime twilightRise = nighttimeData.NauticalTwilightRiseAndSet.Rise ?? DateTime.Now.AddHours(24);
            DateTime minTime = new DateTime(Math.Max(twilightSet.Ticks, DateTime.Now.Ticks));
            return AltList.Where(altTime => altTime.Timestamp > minTime && altTime.Timestamp < twilightRise.AddMinutes(-15))
                .Where(altTime => altTime.Altitude <= altitudeCeiling)
                .Where(altTime => altTime.Airmass >= airmassMin)
                .Where(altTime => altTime.Airmass <= airmassMax)
                .Where(altTime => altTime.DistanceToMoon >= distanceToMoon)
                .Where(altTime => altTime.HourAngleDegrees <= MeridianAltTime().HourAngleDegrees - mDistance || altTime.HourAngleDegrees >= MeridianAltTime().HourAngleDegrees + mDistance)
                .OrderByDescending(altTime => altTime.Altitude).FirstOrDefault();
        }

        public AltTime CurrentAltTime(double altitudeCeiling = 90d, double mDistance = 5d) {
            DateTime windowStart = DateTime.Now;
            DateTime windowEnd = DateTime.Now.AddMinutes(8);
            return AltList
                .Where(altTime => altTime.Timestamp > windowStart && altTime.Timestamp < windowEnd)
                .Where(altTime => altTime.Altitude < altitudeCeiling)
                .Where(altTime => altTime.HourAngleDegrees < MeridianAltTime().HourAngleDegrees - mDistance || altTime.HourAngleDegrees > MeridianAltTime().HourAngleDegrees + mDistance)
                .OrderByDescending(altTime => altTime.Altitude).FirstOrDefault();
        }

        public AltTime CurrentDomeAltTime() {
            DateTime windowStart = DateTime.Now;
            DateTime windowEnd = DateTime.Now.AddMinutes(3);
            return DomeSlitAltTimeList
                .Where(altTime => altTime.Timestamp > windowStart && altTime.Timestamp < windowEnd)
                .OrderBy(altTime => altTime.Timestamp).FirstOrDefault();
        }

        public const double ShutterOvershootPastZenithDegrees = 10d;
        public const double DefaultSlitWidthDegrees = 6d;

        public static bool IsInsideDomeSlit(double altitudeDegrees, double azimuthDegrees, double slitAzimuthDegrees, double slitWidthDegrees, double overshootDegrees = ShutterOvershootPastZenithDegrees) {
            var halfWidth = Math.Abs(slitWidthDegrees) / 2d;
            if (halfWidth <= 0d) {
                return false;
            }
            var alt = AstroUtil.ToRadians(altitudeDegrees);
            var deltaAz = AstroUtil.ToRadians(azimuthDegrees - slitAzimuthDegrees);
            var offset = Math.Abs(Math.Cos(alt) * Math.Sin(deltaAz));
            if (offset > 1d) {
                offset = 1d;
            }
            if (halfWidth < 90d && AstroUtil.ToDegree(Math.Asin(offset)) > halfWidth) {
                return false;
            }
            if (Math.Cos(deltaAz) >= 0d) {
                return true;
            }
            return altitudeDegrees >= 90d - Math.Max(0d, overshootDegrees);
        }

        public void BuildDomeSlitAltTimeList(double altitudeMin, double altitudeMax, double slitStartAzimuth, double slitEndAzimuth, double airmassMin = 0d, double airmassMax = 4d) {
            var slitAzimuth = (slitStartAzimuth + slitEndAzimuth) / 2d;
            var slitWidth = slitEndAzimuth - slitStartAzimuth;
            DomeSlitAltTimeList = AltList
                .Where(altTime => IsInsideDomeSlit(altTime.Altitude, altTime.Azimuth, slitAzimuth, slitWidth))
                .Where(altTime => altTime.Altitude > altitudeMin && altTime.Altitude < altitudeMax)
                .Where(altTime => altTime.Airmass > airmassMin && altTime.Airmass < airmassMax)
                .ToList();

            if (DomeSlitAltTimeList.Count > 0) {
                DomeSlitObservationStartTime = DomeSlitAltTimeList.OrderBy(altTime => altTime.Timestamp).First().Timestamp;
                var lastTime = DomeSlitAltTimeList.OrderBy(altTime => altTime.Timestamp).Last().Timestamp;
                DomeSlitObservationTime = (lastTime - DomeSlitObservationStartTime).TotalSeconds;
            }
        }
    }
}