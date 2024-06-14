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
        [JsonProperty]
        public List<AltTime> DomeSlitAltTimeList { get; set; } = new List<AltTime>();
        [JsonProperty]
        public double DomeSlitObservationTime { get; set; }
        [JsonProperty]
        public DateTime DomeSlitObservationStartTime { get; set; }

        public AltTime MeridianAltTime() {
            return AltList.OrderByDescending((x) => x.alt).FirstOrDefault();
        }

        public AltTime ImageFrom(double alt = 40d) {
            return AltList.Where(x => x.datetime > DateTime.Now).Where((x) => x.alt > alt).OrderBy((x) => x.alt).FirstOrDefault();
        }

        public AltTime ImageTo(NighttimeData nighttimeData, double alt = 90d, double mDistance = 5d, double airmassMin = 0d, double airmassMax = 4d, double distanceToMoon = 20d) {
            DateTime twilightSet = nighttimeData.NauticalTwilightRiseAndSet.Set ?? DateTime.Now;
            DateTime twilightRise = nighttimeData.NauticalTwilightRiseAndSet.Rise ?? DateTime.Now.AddHours(24);
            DateTime minTime = new DateTime(Math.Max(twilightSet.Ticks, DateTime.Now.Ticks));
            return AltList.Where(x => x.datetime > minTime && x.datetime < twilightRise.AddMinutes(-15))
                .Where(x => x.alt <= alt)
                .Where(x => x.airmass >= airmassMin)
                .Where(x => x.airmass <= airmassMax)
                .Where(x => x.distanceToMoon >= distanceToMoon)
                .Where(x => x.deg <= MeridianAltTime().deg - mDistance || x.deg >= MeridianAltTime().deg + mDistance)
                .OrderByDescending(x => x.alt).FirstOrDefault();
        }

        public AltTime getCurrentAltTime(double alt = 90d, double mDistance = 5d) {
            DateTime begin = DateTime.Now;
            DateTime end = DateTime.Now.AddMinutes(8);
            return AltList
                .Where(x => x.datetime > begin && x.datetime < end)
                .Where(x => x.alt < alt)
                .Where(x => x.deg < MeridianAltTime().deg - mDistance || x.deg > MeridianAltTime().deg + mDistance)
                .OrderByDescending(x => x.alt).FirstOrDefault();
        }

        public AltTime getCurrentDomeAltTime() {
            DateTime begin = DateTime.Now;
            DateTime end = DateTime.Now.AddMinutes(3);
            return DomeSlitAltTimeList
                .Where(x => x.datetime > begin && x.datetime < end)
                .OrderBy(x => x.datetime).FirstOrDefault();
        }

        public void setDomeSlitAltTimeList(Speckle speckle, double begin, double end, double airmassMin = 0d, double airmassMax = 4d) {
            DomeSlitAltTimeList = AltList
                .Where(x => x.az > begin && x.az < end)
                .Where(x => x.alt > speckle.AltitudeMin && x.alt < speckle.AltitudeMax)
                .Where(x => x.airmass > airmassMin && x.airmass < airmassMax)
                .ToList();

            if (DomeSlitAltTimeList.Count > 0) {
                DomeSlitObservationStartTime = DomeSlitAltTimeList.OrderBy(x => x.datetime).First().datetime;
                var lastTime = DomeSlitAltTimeList.OrderBy(x => x.datetime).Last().datetime;
                DomeSlitObservationTime = (lastTime - DomeSlitObservationStartTime).TotalSeconds;
            }
        }
    }
}