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
using NINA.Core.Model;
using NINA.Image.ImageData;
using NINA.Plugin.Speckle.Sequencer.SequenceItem;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace NINA.Plugin.Speckle.Model {

    [JsonObject(MemberSerialization.OptIn)]
    public class ReferenceStar : Star {
        public ReferenceStar(List<object> obj) {
            Name1 = "SSRef";
            Name2 = (string)obj[0];
            RA2000 = Convert.ToDouble(obj[1]);
            Dec2000 = Convert.ToDouble(obj[2]);
            Rp = Convert.ToDouble(obj[3]);
            distance = Convert.ToDouble(obj[4]);
        }

        public ReferenceStar() {
            Name1 = "SSRef";
        }

        public ReferenceStar(Star star) {
            foreach (PropertyInfo prop in typeof(Star).GetProperties()) {
                if (prop.CanRead && prop.CanWrite) {
                    prop.SetValue(this, prop.GetValue(star));
                }
            }
        }

        public string Name {
            get => Name1 + (string.IsNullOrWhiteSpace(Name2) || "_".Equals(Name2) ? "" : "_" + Name2);
        }

        [JsonProperty]
        public double distance { get; set; }

        [JsonProperty]
        public double color { get; set; }

        [JsonProperty]
        public string Title {
            get => $"{Name}, Distance: {Math.Round(distance, 3)}°, Color: {Math.Round(color, 2)} (B-V), VMag: {Math.Round(Rp, 2)}";
        }

        public Coordinates Coordinates() {
            return new Coordinates(Angle.ByDegree(RA2000), Angle.ByDegree(Dec2000), Epoch.J2000);
        }
        public List<IGenericMetaDataHeader> GenericHeaders() {
            var headerList = new List<IGenericMetaDataHeader>();
            headerList.Add(new StringMetaDataHeader("Proj", Proj));
            headerList.Add(new StringMetaDataHeader("Obs", Obs));
            headerList.Add(new StringMetaDataHeader("Type", Type));
            headerList.Add(new StringMetaDataHeader("Name1", Name1));
            headerList.Add(new StringMetaDataHeader("Name2", Name2));
            headerList.Add(new StringMetaDataHeader("GaiaNum", GaiaNum));
            headerList.Add(new DoubleMetaDataHeader("Distance", distance));
            headerList.Add(new DoubleMetaDataHeader("Color", color));
            headerList.Add(new BoolMetaDataHeader("IsRef", true));
            return headerList;
        }
    }

        public sealed class ReferenceStarMap : ClassMap<ReferenceStar> {

        public ReferenceStarMap() {
            // StarMap
            Map(m => m.TargetRecno).Name("targetrecno").Optional().Default(0);
            Map(m => m.Recno).Name("recno").Optional().Default(0);
            Map(m => m.Proj).Name(["Proj~", "Proj"]).Optional().Default("");
            Map(m => m.Obs).Name(["Obs~","Obs"]).Optional().Default("");
            Map(m => m.Type).Name(["Type~", "Type"]).Optional().Default("M");
            Map(m => m.Name1).Name(["Name1*", "Name1"]).Optional().Default("");
            Map(m => m.Name2).Name(["Name2*", "Name2"]).Optional().Default("");
            Map(m => m.Priority).Name(["Priority~", "Priority"]).Optional().Default(1);
            Map(m => m.Template).Name(["Temp","Template"]).Optional().Default("");
            Map(m => m.RA2000).Name(["RA2000*", "RA2000"]).Optional().Default(0);
            Map(m => m.Dec2000).Name(["D2000*", "Dec2000"]).Optional().Default(0);
            Map(m => m.Bp).Name(["Bp~", "Bp"]).Optional().Default(0);
            Map(m => m.Rp).Name(["Rp~", "Rp"]).Optional().Default(0);
            Map(m => m.Gmag).Name(["Gmag~", "Gmag"]).Optional().Default(0);
            Map(m => m.GaiaNum).Name(["GaiaNum~", "GaiaNum"]).Optional().Default("0");
            Map(m => m.Sep).Name(["Sep~", "Sep"]).Optional().Default(0);
            Map(m => m.PA).Name(["PA", "Pa"]).Optional().Default(0);
            Map(m => m.Parallax).Name("Parallax").Optional().Default(0);
            Map(m => m.Spectrum).Name("Spectrum").Optional().Default("");
            Map(m => m.Pmag).Name(["Pmag~", "Pmag"]).Optional().Default(0);
            Map(m => m.Smag).Name(["Smag~", "Smag"]).Optional().Default(0);
            Map(m => m.Filter).Name(["Filter~", "Filter"]).Optional().Default("");
            Map(m => m.Exp).Name(["Exp~", "Exp"]).Optional().Default(0);
            Map(m => m.NExp).Name(["NExp~", "NExp", "Nexp"]).Optional().Default(0);
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
        }
    }

}