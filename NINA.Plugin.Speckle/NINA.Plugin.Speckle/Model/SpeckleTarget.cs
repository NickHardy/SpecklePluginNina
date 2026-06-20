#region "copyright"

/*
    Copyright © 2016 - 2021 Stefan Berg <isbeorn86+NINA@googlemail.com> and the N.I.N.A. contributors

    This file is part of N.I.N.A. - Nighttime Imaging 'N' Astronomy.

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
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

        public Coordinates Coordinates() {
            return new Coordinates(Angle.ByDegree(RA2000), Angle.ByDegree(Dec2000), Epoch.J2000);
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
            // StarMap
            Map(m => m.Proj).Name(["Proj", "Proj~"]).Optional().Default("");
            Map(m => m.Obs).Name(["Obs", "Obs~"]).Optional().Default("");
            Map(m => m.Type).Name(["Type", "Type~"]).Optional().Default("M");
            Map(m => m.Name1).Name(["Name1", "Name1*"]).Optional().Default("");
            Map(m => m.Name2).Name(["Name2", "Name2*"]).Optional().Default("");
            Map(m => m.Priority).Name(["Priority", "Priority~"]).Optional().Default(1);
            Map(m => m.Template).Name(["Template", "Temp"]).Optional().Default("");
            Map(m => m.RA2000).Name(["RA2000", "RA2000*"]).Optional().Default(0);
            Map(m => m.Dec2000).Name(["D2000", "D2000*", "Dec2000"]).Optional().Default(0);
            Map(m => m.Bp).Name(["Bp", "Bp~", "BP", "BP~"]).Optional().Default(0);
            Map(m => m.Rp).Name(["Rp", "Rp~", "RP", "RP~"]).Optional().Default(0);
            Map(m => m.Gmag).Name(["Gmag", "Gmag~"]).Optional().Default(0);
            Map(m => m.GaiaNum).Name(["GaiaNum", "GaiaNum~"]).Optional().Default("0");
            Map(m => m.Sep).Name(["Sep", "Sep~"]).Optional().Default(0);
            Map(m => m.PA).Name(["PA", "Pa"]).Optional().Default(0);
            Map(m => m.Parallax).Name("Parallax").Optional().Default(0);
            Map(m => m.Spectrum).Name("Spectrum").Optional().Default("");
            Map(m => m.Pmag).Name(["Pmag", "Pmag~", "PMag"]).Optional().Default(0);
            Map(m => m.Smag).Name(["Smag", "Smag~", "SMag"]).Optional().Default(0);
            Map(m => m.Exp).Name(["Exp", "Exp~"]).Optional().Default(0);
            Map(m => m.NExp).Name(["NExp", "Nexp", "NExp~", "Nexp~"]).Optional().Default(0);
            Map(m => m.NoEC).Name("NoEC").Optional().Default(0);
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
            Map(m => m.RefGaiaNum).Name(["RefGaiaNum","RefGaiaNum~"]).Optional().Default("");
            Map(m => m.Completed_cycles).Name("Completed_cycles").Optional().Default(0);
            Map(m => m.Completed_ref_cycles).Name("Completed_ref_cycles").Optional().Default(0);
            Map(m => m.Completed_nights).Name("Completed_nights").Optional().Default(0);
        }
    }
}