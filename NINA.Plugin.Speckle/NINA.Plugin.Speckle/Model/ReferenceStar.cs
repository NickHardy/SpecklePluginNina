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
            get => $"{Name}, Distance: {Math.Round(distance, 3)} deg, Color: {Math.Round(color, 2)} (B-V), VMag: {Math.Round(Rp, 2)}";
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
            Map(column => column.TargetRecno).Name("targetrecno").Optional().Default(0);
            Map(column => column.Recno).Name("recno").Optional().Default(0);
            Map(column => column.Proj).Name(["Proj~", "Proj"]).Optional().Default("");
            Map(column => column.Obs).Name(["Obs~","Obs"]).Optional().Default("");
            Map(column => column.Type).Name(["Type~", "Type"]).Optional().Default("M");
            Map(column => column.Name1).Name(["Name1*", "Name1"]).Optional().Default("");
            Map(column => column.Name2).Name(["Name2*", "Name2"]).Optional().Default("");
            Map(column => column.Priority).Name(["Priority~", "Priority"]).Optional().Default(1);
            Map(column => column.Template).Name(["Temp","Template"]).Optional().Default("");
            Map(column => column.RA2000).Name(["RA2000*", "RA2000"]).Optional().Default(0);
            Map(column => column.Dec2000).Name(["D2000*", "Dec2000"]).Optional().Default(0);
            Map(column => column.Bp).Name(["Bp~", "Bp"]).Optional().Default(0);
            Map(column => column.Rp).Name(["Rp~", "Rp"]).Optional().Default(0);
            Map(column => column.Gmag).Name(["Gmag~", "Gmag"]).Optional().Default(0);
            Map(column => column.GaiaNum).Name(["GaiaNum~", "GaiaNum"]).Optional().Default("0");
            Map(column => column.Sep).Name(["Sep~", "Sep"]).Optional().Default(0);
            Map(column => column.PA).Name(["PA", "Pa"]).Optional().Default(0);
            Map(column => column.Parallax).Name("Parallax").Optional().Default(0);
            Map(column => column.Spectrum).Name("Spectrum").Optional().Default("");
            Map(column => column.Pmag).Name(["Pmag~", "Pmag"]).Optional().Default(0);
            Map(column => column.Smag).Name(["Smag~", "Smag"]).Optional().Default(0);
            Map(column => column.Filter).Name(["Filter~", "Filter"]).Optional().Default("");
            Map(column => column.Exp).Name(["Exp~", "Exp"]).Optional().Default(0);
            Map(column => column.NExp).Name(["NExp~", "NExp", "Nexp"]).Optional().Default(0);
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
        }
    }

}