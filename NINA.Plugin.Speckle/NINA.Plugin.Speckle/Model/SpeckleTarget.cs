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

namespace NINA.Plugin.Speckle.Model {

    [JsonObject(MemberSerialization.OptIn)]
    public class SpeckleTarget {
        [JsonProperty]
        public string User { get; set; }
        [JsonProperty]
        public string Target { get; set; }
        [JsonProperty]
        public bool Ref { get; set; }
        [JsonProperty]
        public double Ra { get; set; }
        [JsonProperty]
        public double Dec { get; set; }
        [JsonProperty]
        public double Nights { get; set; }
        [JsonProperty]
        public double Cycles { get; set; }
        [JsonProperty]
        public double Priority { get; set; }
        [JsonProperty]
        public double ExposureTime { get; set; }
        [JsonProperty]
        public int Exposures { get; set; }
        [JsonProperty]
        public double Completed_nights { get; set; }
        [JsonProperty]
        public double Completed_cycles { get; set; }
        [JsonProperty]
        public string Template { get; set; }
        [JsonProperty]
        public DateTime Meridian { get; set; }

        public List<SimbadStarCluster> StarClusterList { get; set; }
        public SimbadStarCluster StarCluster { get; set; } = new SimbadStarCluster();

        public List<SimbadSaoStar> ReferenceStarList { get; set; }
        public SimbadSaoStar ReferenceStar { get; set; } = new SimbadSaoStar();

        public Coordinates Coordinates() {
            return new Coordinates(Angle.ByDegree(Ra), Angle.ByDegree(Dec), Epoch.J2000);
        }
    }

    public sealed class SpeckleTargetMap : ClassMap<SpeckleTarget> {

        public SpeckleTargetMap() {
            Map(m => m.User).Name("User");
            Map(m => m.Target).Name("Target");
            Map(m => m.Ref).Name("Ref");
            Map(m => m.Ra).Name("Ra");
            Map(m => m.Dec).Name("Dec");
            Map(m => m.Nights).Name("Nights");
            Map(m => m.Cycles).Name("Cycles");
            Map(m => m.Priority).Name("Priority");
            Map(m => m.ExposureTime).Name("ExposureTime");
            Map(m => m.Exposures).Name("Exposures");
            Map(m => m.Completed_nights).Name("Completed_nights");
            Map(m => m.Completed_cycles).Name("Completed_cycles");
            Map(m => m.Template).Name("Template");
        }
    }
}