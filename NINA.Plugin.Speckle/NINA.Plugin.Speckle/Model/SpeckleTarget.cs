﻿#region "copyright"

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
    public class SpeckleTarget : TargetBase {
        public SpeckleTarget() {
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
        public double PMag { get; set; }
        [JsonProperty]
        public double SMag { get; set; }
        [JsonProperty]
        public int NoCalculation { get; set; }
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
        public string TemplateRef { get; set; }
        [JsonProperty]
        public string Filter { get; set; }
        [JsonProperty]
        public double Rotation { get; set; } = 0d;
        [JsonProperty]
        public double AirmassMin { get; set; } = 0d;
        [JsonProperty]
        public double AirmassMax { get; set; } = 4d;
        [JsonProperty]
        public int GetRef { get; set; }
        public bool RegisterTarget { get; set; } = true;
        [JsonProperty]
        public double MinAltitude { get; set; } = 0d;

        [JsonProperty]
        public bool ImageTarget { get; set; } = true;
        [JsonProperty]
        public string Note { get; set; }

        public List<SimbadStar> ReferenceStarList { get; set; }
        [JsonProperty]
        public SimbadStar ReferenceStar { get; set; } = new SimbadStar();

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

        public DateTime ImageTime { get; set; }
        public DateTime? ImagedAt { get; set; }
        public double ImageTimeAlt { get; set; }
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
            Map(m => m.Rotation).Name("Rotation").Optional().Default(0);
            Map(m => m.AirmassMin).Name(["Airmass", "AirmassMin"]).Optional().Default(0);
            Map(m => m.AirmassMax).Name("AirmassMax").Optional().Default(4);
            Map(m => m.MinAltitude).Name("MinAltitude").Optional().Default(0);
            Map(m => m.GetRef).Name("GetRef").Optional().Default(1);
            Map(m => m.ExposureTime).Name("ExposureTime").Optional().Default(0);
            Map(m => m.Exposures).Name("Exposures").Optional().Default(0);
            Map(m => m.PMag).Name("PMag").Optional().Default(0);
            Map(m => m.SMag).Name("SMag").Optional().Default(0);
            Map(m => m.NoCalculation).Name("NoCalculation").Optional().Default("0");
            Map(m => m.Separation).Name("Separation").Optional().Default(0);
            Map(m => m.Template).Name("Template").Optional().Default("");
            Map(m => m.Filter).Name("Filter").Optional().Default("");
            Map(m => m.Completed_cycles).Name("Completed_cycles").Optional().Default(0);
            Map(m => m.Completed_ref_cycles).Name("Completed_ref_cycles").Optional().Default(0);
            Map(m => m.Completed_nights).Name("Completed_nights").Optional().Default(0);
            Map(m => m.Note).Name("Note").Optional().Default("");
        }
    }
}