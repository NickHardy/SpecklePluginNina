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
    public class SpeckleTarget : Star {
        public SpeckleTarget() {
        }

        public string Name {
            get => Name1 + "_" + (string.IsNullOrWhiteSpace(Name2) ? "Gaia-" + GaiaNum.ToString() : Name2);
        }

        [JsonProperty]
        public double Orientation { get; set; }
        [JsonProperty]
        public double ArcsecPerPix { get; set; }
        [JsonProperty]
        public int Nights { get; set; }
        [JsonProperty]
        public int Cycles { get; set; }
        [JsonProperty]
        public int Completed_nights { get; set; }
        [JsonProperty]
        public int Completed_cycles { get; set; }
        [JsonProperty]
        public int Completed_ref_cycles { get; set; }
        [JsonProperty]
        public string TemplateRef { get; set; }
        [JsonProperty]
        public double AirmassMin { get; set; } = 0d;
        [JsonProperty]
        public double AirmassMax { get; set; } = 4d;
        public bool RegisterTarget { get; set; } = true;
        [JsonProperty]
        public double MinAltitude { get; set; } = 0d;

        [JsonProperty]
        public bool ImageTarget { get; set; } = true;

        [JsonProperty]
        public long RefGaiaNum { get; set; } = 0;

        public List<ReferenceStar> ReferenceStarList { get; set; }
        [JsonProperty]
        public ReferenceStar ReferenceStar { get; set; } = new ReferenceStar();

        public List<SimbadStarCluster> StarClusterList { get; set; }
        public SimbadStarCluster StarCluster { get; set; } = new SimbadStarCluster();

        public SpeckleTargetContainer SpeckleTemplate { get; set; }

        public double Color {
            get => Bp - Rp;
        }

        public Coordinates Coordinates() {
            return new Coordinates(Angle.ByDegree(RA2000), Angle.ByDegree(Dec2000), Epoch.J2000);
        }

        public DateTime ImageTime { get; set; }
        public DateTime? ImagedAt { get; set; }
        public double ImageTimeAlt { get; set; }
    }

    public sealed class SpeckleTargetMap : ClassMap<SpeckleTarget> {

        public SpeckleTargetMap() {
            // StarMap
            Map(m => m.TargetRecno).Name("targetrecno").Optional().Default(0);
            Map(m => m.Recno).Name("recno").Optional().Default(0);
            Map(m => m.Proj).Name("Proj").Optional().Default("");
            Map(m => m.Obs).Name("Obs").Optional().Default("");
            Map(m => m.Type).Name("Type").Optional().Default("");
            Map(m => m.Name1).Name("Name1").Optional().Default("");
            Map(m => m.Name2).Name("Name2").Optional().Default("");
            Map(m => m.Priority).Name("Priority").Optional().Default(1);
            Map(m => m.Template).Name("Template").Optional().Default("");
            Map(m => m.RA2000).Name("RA2000").Optional().Default(0);
            Map(m => m.Dec2000).Name("Dec2000").Optional().Default(0);
            Map(m => m.Bp).Name("Bp").Optional().Default(0);
            Map(m => m.Rp).Name("Rp").Optional().Default(0);
            Map(m => m.Gmag).Name("Gmag").Optional().Default(0);
            Map(m => m.GaiaNum).Name("GaiaNum").Optional().Default(0);
            Map(m => m.Sep).Name("Sep").Optional().Default(0);
            Map(m => m.PA).Name("PA").Optional().Default(0);
            Map(m => m.Parallax).Name("Parallax").Optional().Default(0);
            Map(m => m.Spectrum).Name("Spectrum").Optional().Default("");
            Map(m => m.Pmag).Name("Pmag").Optional().Default(0);
            Map(m => m.Smag).Name("Smag").Optional().Default(0);
            Map(m => m.Filter).Name("Filter").Optional().Default("");
            Map(m => m.ExpTime).Name("ExpTime").Optional().Default(0);
            Map(m => m.NumExp).Name("NumExp").Optional().Default(0);
            Map(m => m.NoExpCalc).Name("NoExpCalc").Optional().Default(0);
            Map(m => m.GetRef).Name("GetRef").Optional().Default(1);
            Map(m => m.GPrime).Name("GPrime").Optional().Default(0);
            Map(m => m.RPrime).Name("RPrime").Optional().Default(0);
            Map(m => m.IPrime).Name("IPrime").Optional().Default(0);
            Map(m => m.ZPrime).Name("ZPrime").Optional().Default(0);
            Map(m => m.RUWE).Name("RUWE").Optional().Default(0);
            Map(m => m.FDBL).Name("FDBL").Optional().Default(0);
            Map(m => m.Note1).Name("Note1").Optional().Default("");
            Map(m => m.Note2).Name("Note2").Optional().Default("");

            // SpeckleTarget
            Map(m => m.Nights).Name("Nights").Optional().Default(1);
            Map(m => m.Cycles).Name("Cycles").Optional().Default(1);
            Map(m => m.AirmassMin).Name(["Airmass", "AirmassMin"]).Optional().Default(0);
            Map(m => m.AirmassMax).Name("AirmassMax").Optional().Default(4);
            Map(m => m.MinAltitude).Name("MinAltitude").Optional().Default(0);
            Map(m => m.GetRef).Name("GetRef").Optional().Default(1);
            Map(m => m.RefGaiaNum).Name("RefGaiaNum").Optional().Default(0);
            Map(m => m.Template).Name("Template").Optional().Default("");
            Map(m => m.Filter).Name("Filter").Optional().Default("");
            Map(m => m.Completed_cycles).Name("Completed_cycles").Optional().Default(0);
            Map(m => m.Completed_ref_cycles).Name("Completed_ref_cycles").Optional().Default(0);
            Map(m => m.Completed_nights).Name("Completed_nights").Optional().Default(0);
        }
    }
}