#region "copyright"

/*
    Copyright (c) 2026 Nick Hardy and Leon Bewersdorff

    This file is part of the Speckle Interferometry plugin for
    N.I.N.A. - Nighttime Imaging 'N' Astronomy.

    Released under the MIT License. See LICENSE.txt in the repository
    root, or https://opensource.org/licenses/MIT
*/

#endregion "copyright"

using CsvHelper.Configuration;
using Newtonsoft.Json;
using NINA.Astrometry;
using NINA.Image.ImageData;
using NINA.Plugin.Speckle.Sequencer.Container;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace NINA.Plugin.Speckle.Model {

    [JsonObject(MemberSerialization.OptIn)]
    public class SpeckleTarget : Star {
        public SpeckleTarget() {
        }
        public SpeckleTarget(Star star) {
            foreach (PropertyInfo prop in typeof(Star).GetProperties()) {
                if (prop.CanRead && prop.CanWrite) {
                    prop.SetValue(this, prop.GetValue(star));
                }
            }
        }

        public string Name {
            get => Name1 + (string.IsNullOrWhiteSpace(Name2) ? "" : "_" + Name2);
        }

        public bool IsComplete {
            get => Cycles > 0 && Completed_cycles >= Cycles;
        }

        public string CyclesDisplay {
            get => Completed_cycles + "/" + Cycles;
        }

        public string RaText {
            get => CoordinateFormat.RaHours(RA2000 / 15d);
        }

        public string DecText {
            get => CoordinateFormat.DecDegrees(Dec2000);
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

        public string SourceList { get; set; }

        public int PlanNumber { get; set; }
        [JsonProperty]
        public double MinAltitude { get; set; } = 0d;

        [JsonProperty]
        public bool ImageTarget { get; set; } = true;

        [JsonProperty]
        public string RefGaiaNum { get; set; } = "";

        [JsonProperty]
        public List<ReferenceStar> ReferenceStarList { get; set; }

        public ReferenceStar ReferenceStar { get; set; } = new ReferenceStar();

        public List<SimbadStarCluster> StarClusterList { get; set; }
        public SimbadStarCluster StarCluster { get; set; } = new SimbadStarCluster();

        public SpeckleTargetContainer SpeckleTemplate { get; set; }

        public double Color {
            get => Bp != 0 && Rp != 0 ? Bp - Rp : 0;
        }


        public DateTime ImageTime { get; set; }
        public DateTime? ImagedAt { get; set; }
        public double ImageTimeAlt { get; set; }
        public List<IGenericMetaDataHeader> GenericHeaders(bool all) {
            var headerList = new List<IGenericMetaDataHeader>();
            headerList.Add(new StringMetaDataHeader("Proj", Proj));
            headerList.Add(new StringMetaDataHeader("Obs", Obs));
            headerList.Add(new StringMetaDataHeader("Type", Type));
            headerList.Add(new StringMetaDataHeader("Name1", Name1));
            headerList.Add(new StringMetaDataHeader("Name2", Name2));
            headerList.Add(new StringMetaDataHeader("GaiaNum", GaiaNum));
            if (!all) 
                return headerList;
            headerList.Add(new IntMetaDataHeader("Priority", Priority));
            headerList.Add(new StringMetaDataHeader("Template", Template));
            headerList.Add(new DoubleMetaDataHeader("RA2000", RA2000));
            headerList.Add(new DoubleMetaDataHeader("D2000", Dec2000));
            headerList.Add(new DoubleMetaDataHeader("Bp", Bp));
            headerList.Add(new DoubleMetaDataHeader("Rp", Rp));
            headerList.Add(new DoubleMetaDataHeader("Gmag", Gmag));
            headerList.Add(new StringMetaDataHeader("RefGaiaNum", RefGaiaNum));
            headerList.Add(new DoubleMetaDataHeader("Sep", Sep));
            headerList.Add(new DoubleMetaDataHeader("PA", PA));
            headerList.Add(new DoubleMetaDataHeader("Parallax", Parallax));
            headerList.Add(new StringMetaDataHeader("Spectrum", Spectrum));
            headerList.Add(new DoubleMetaDataHeader("Pmag", Pmag));
            headerList.Add(new DoubleMetaDataHeader("Smag", Smag));
            headerList.Add(new DoubleMetaDataHeader("ExpCsv", Exp));
            headerList.Add(new IntMetaDataHeader("NExp", NExp));
            headerList.Add(new IntMetaDataHeader("NoEC", NoEC));
            headerList.Add(new IntMetaDataHeader("GetRef", GetRef));
            headerList.Add(new DoubleMetaDataHeader("GPrime", GPrime));
            headerList.Add(new DoubleMetaDataHeader("RPrime", RPrime));
            headerList.Add(new DoubleMetaDataHeader("IPrime", IPrime));
            headerList.Add(new DoubleMetaDataHeader("ZPrime", ZPrime));
            headerList.Add(new DoubleMetaDataHeader("RUWE", RUWE));
            headerList.Add(new DoubleMetaDataHeader("FDBL", FDBL));
            headerList.Add(new StringMetaDataHeader("Note1", Note1));
            headerList.Add(new StringMetaDataHeader("Note2", Note2));
            return headerList;
        }
    }

    public sealed class SpeckleTargetMap : ClassMap<SpeckleTarget> {

        public SpeckleTargetMap() {
            Map(column => column.Proj).Name(["Proj", "Proj~"]).Optional().Default("");
            Map(column => column.Obs).Name(["Obs", "Obs~"]).Optional().Default("");
            Map(column => column.Type).Name(["Type", "Type~"]).Optional().Default("M");
            Map(column => column.Name1).Name(["Name1", "Name1*"]).Optional().Default("");
            Map(column => column.Name2).Name(["Name2", "Name2*", "Name3"]).Optional().Default("");
            Map(column => column.Priority).Name(["Priority", "Priority~"]).Optional().Default(1);
            Map(column => column.Template).Name(["Template", "Temp"]).Optional().Default("");
            Map(column => column.RA2000).Name(["RA2000", "RA2000*"]).Optional().Default(0);
            Map(column => column.Dec2000).Name(["D2000", "D2000*", "Dec2000"]).Optional().Default(0);
            Map(column => column.Bp).Name(["Bp", "Bp~", "BP", "BP~"]).Optional().Default(0);
            Map(column => column.Rp).Name(["Rp", "Rp~", "RP", "RP~"]).Optional().Default(0);
            Map(column => column.Gmag).Name(["Gmag", "Gmag~"]).Optional().Default(0);
            Map(column => column.GaiaNum).Name(["GaiaNum", "GaiaNum~"]).Optional().Default("0");
            Map(column => column.Sep).Name(["Sep", "Sep~"]).Optional().Default(0);
            Map(column => column.PA).Name(["PA", "Pa"]).Optional().Default(0);
            Map(column => column.Parallax).Name("Parallax").Optional().Default(0);
            Map(column => column.Spectrum).Name(["Spectrum", "Spec", "Spec~"]).Optional().Default("");
            Map(column => column.Pmag).Name(["Pmag", "Pmag~", "PMag"]).Optional().Default(0);
            Map(column => column.Smag).Name(["Smag", "Smag~", "SMag"]).Optional().Default(0);
            Map(column => column.Filter).Name(["Filter", "Filter~"]).Optional().Default("");
            Map(column => column.Exp).Name(["Exp", "Exp~"]).Optional().Default(0);
            Map(column => column.NExp).Name(["NExp", "Nexp", "NExp~", "Nexp~"]).Optional().Default(0);
            Map(column => column.NoEC).Name("NoEC").Optional().Default(0);
            Map(column => column.GetRef).Name("GetRef").Optional().Default(1);
            Map(column => column.GPrime).Name("GPrime").Optional().Default(0);
            Map(column => column.RPrime).Name("RPrime").Optional().Default(0);
            Map(column => column.IPrime).Name("IPrime").Optional().Default(0);
            Map(column => column.ZPrime).Name("ZPrime").Optional().Default(0);
            Map(column => column.RUWE).Name("RUWE").Optional().Default(0);
            Map(column => column.FDBL).Name("FDBL").Optional().Default(0);
            Map(column => column.Note1).Name("Note1").Optional().Default("");
            Map(column => column.Note2).Name("Note2").Optional().Default("");

            Map(column => column.Nights).Name("Nights").Optional().Default(1);
            Map(column => column.Cycles).Name("Cycles").Optional().Default(1);
            Map(column => column.AirmassMin).Name(["Airmass", "AirmassMin"]).Optional().Default(0);
            Map(column => column.AirmassMax).Name("AirmassMax").Optional().Default(4);
            Map(column => column.MinAltitude).Name("MinAltitude").Optional().Default(0);
            Map(column => column.RefGaiaNum).Name(["RefGaiaNum","RefGaiaNum~"]).Optional().Default("");
            Map(column => column.Completed_cycles).Name("Completed_cycles").Optional().Default(0);
            Map(column => column.Completed_ref_cycles).Name("Completed_ref_cycles").Optional().Default(0);
            Map(column => column.Completed_nights).Name("Completed_nights").Optional().Default(0);
        }
    }
}