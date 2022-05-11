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

namespace NINA.Plugin.Speckle.Model {

    [JsonObject(MemberSerialization.OptIn)]
    public class SpeckleTarget {
        static int nextId;
        public int TargetId { get; private set; }
        public SpeckleTarget() {
            TargetId = Interlocked.Increment(ref nextId);
        }

        [JsonProperty]
        public string User { get; set; }
        [JsonProperty]
        public string Target { get; set; }
        [JsonProperty]
        public string Ra { get; set; }
        [JsonProperty]
        public string Dec { get; set; }
        [JsonProperty]
        public string RA0 { get; set; }
        [JsonProperty]
        public string Decl0 { get; set; }
        [JsonProperty]
        public double Orientation { get; set; }
        [JsonProperty]
        public double ArcsecPerPix { get; set; }
        [JsonProperty]
        public int Nights { get; set; }
        [JsonProperty]
        public int Cycles { get; set; }
        [JsonProperty]
        public int Priority { get; set; }
        [JsonProperty]
        public double ExposureTime { get; set; }
        [JsonProperty]
        public int Exposures { get; set; }
        [JsonProperty]
        public double Magnitude { get; set; }
        [JsonProperty]
        public double Separation { get; set; }
        [JsonProperty]
        public int Completed_nights { get; set; }
        [JsonProperty]
        public int Completed_cycles { get; set; }
        [JsonProperty]
        public int Completed_ref_cycles { get; set; }
        [JsonProperty]
        public string Template { get; set; }
        [JsonProperty]
        public string Filter { get; set; }

        public List<AltTime> AltList { get; set; } = new List<AltTime>();

        public List<SimbadSaoStar> ReferenceStarList { get; set; }
        public SimbadSaoStar ReferenceStar { get; set; } = new SimbadSaoStar();

        public List<SimbadStarCluster> StarClusterList { get; set; }
        public SimbadStarCluster StarCluster { get; set; } = new SimbadStarCluster();

        public SpeckleTargetContainer SpeckleTemplate { get; set; }

        public Coordinates Coordinates() {
            double RaDeg;
            double DecDeg;
            if (!Double.TryParse(Ra, out RaDeg)) {
                RaDeg = AstroUtil.HMSToDegrees(Ra);
            }
            if (!Double.TryParse(Dec, out DecDeg)) {
                DecDeg = AstroUtil.DMSToDegrees(Dec);
            }
            return new Coordinates(Angle.ByDegree(RaDeg), Angle.ByDegree(DecDeg), Epoch.J2000);
        }

        public AltTime MeridianAltTime() {
            return AltList.OrderByDescending((x) => x.alt).FirstOrDefault();
        }

        public AltTime ImageFrom(double alt = 40d) {
            return AltList.Where(x => x.datetime > DateTime.Now).Where((x) => x.alt > alt).OrderBy((x) => x.alt).FirstOrDefault();
        }

        public DateTime ImageTime { get; set; }
        public double ImageTimeAlt { get; set; }
        public AltTime ImageTo(NighttimeData nighttimeData, double alt = 80d, double mDistance = 5d) {
            DateTime twilightSet = nighttimeData.NauticalTwilightRiseAndSet.Set ?? DateTime.Now;
            DateTime twilightRise = nighttimeData.NauticalTwilightRiseAndSet.Rise ?? DateTime.Now.AddHours(24);
            DateTime minTime = new DateTime(Math.Max(twilightSet.Ticks, DateTime.Now.Ticks));
            return AltList.Where(x => x.datetime > minTime && x.datetime < twilightRise)
                .Where(x => x.alt < alt)
                .Where(x => x.deg < MeridianAltTime().deg - mDistance || x.deg > MeridianAltTime().deg + mDistance)
                .OrderByDescending(x => x.alt).FirstOrDefault();
        }

        public AltTime getCurrentAltTime(double alt = 80d, double mDistance = 5d) {
            DateTime begin = DateTime.Now;
            DateTime end = DateTime.Now.AddMinutes(8);
            return AltList
                .Where(x => x.datetime > begin && x.datetime < end)
                .Where(x => x.alt < alt)
                .Where(x => x.deg < MeridianAltTime().deg - mDistance || x.deg > MeridianAltTime().deg + mDistance)
                .OrderByDescending(x => x.alt).FirstOrDefault();
        }
    }

    public sealed class SpeckleTargetMap : ClassMap<SpeckleTarget> {

        public SpeckleTargetMap() {
            Map(m => m.User).Name("User").Optional().Default("");
            Map(m => m.Target).Name("Target").Optional().Default("");
            Map(m => m.Ra).Name("Ra");
            Map(m => m.Dec).Name("Dec");
            Map(m => m.RA0).Name("RA0");
            Map(m => m.Decl0).Name("Decl0");
            Map(m => m.Nights).Name("Nights").Optional().Default(1);
            Map(m => m.Cycles).Name("Cycles").Optional().Default(1);
            Map(m => m.Priority).Name("Priority").Optional().Default(0);
            Map(m => m.ExposureTime).Name("ExposureTime").Optional().Default(0);
            Map(m => m.Exposures).Name("Exposures").Optional().Default(0);
            Map(m => m.Magnitude).Name("Magnitude").Optional().Default(0);
            Map(m => m.Separation).Name("Separation").Optional().Default(0);
            Map(m => m.Template).Name("Template").Optional().Default("");
            Map(m => m.Filter).Name("Filter").Optional().Default("");
            Map(m => m.Completed_cycles).Name("Completed_cycles").Optional().Default(0);
            Map(m => m.Completed_ref_cycles).Name("Completed_ref_cycles").Optional().Default(0);
            Map(m => m.Completed_nights).Name("Completed_nights").Optional().Default(0);
        }
    }
}