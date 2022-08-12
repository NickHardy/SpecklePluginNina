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

namespace NINA.Plugin.Speckle.Model {

    public class GdsTarget {
        public string Number { get; set; }
        public string SeqNum0 { get; set; }
        public string SeqNum1 { get; set; }
        public string WDSIndex { get; set; }
        public string RA0 { get; set; }
        public string Decl0 { get; set; }
        public string RA1 { get; set; }
        public string Decl1 { get; set; }
        public double GaiaPA { get; set; }
        public double GaiaSep { get; set; }
        public double GBIndex { get; set; }
        public int num3Band { get; set; }
        public double Gmag0 { get; set; }
        public double Gmag1 { get; set; }
        public double BPmag0 { get; set; }
        public double BPmag1 { get; set; }
        public double RPmag0 { get; set; }
        public double RPmag1 { get; set; }
        public double PMRA0 { get; set; }
        public double PMDec0 { get; set; }
        public double PMRA1 { get; set; }
        public double PMDec1 { get; set; }
        public double PMRA_Err0 { get; set; }
        public double PMDec_Err0 { get; set; }
        public double PMRA_Err1 { get; set; }
        public double PMDec_Err1 { get; set; }
        public double Parallax0 { get; set; }
        public double Parallax1 { get; set; }
        public double Parallax_Err0 { get; set; }
        public double Parallax_Err1 { get; set; }
        public int VarFlag { get; set; }
        public double Teff0 { get; set; }
        public double Teff1 { get; set; }
        public double Lum0 { get; set; }
        public double Lum1 { get; set; }
        public double Radius0 { get; set; }
        public double Radius1 { get; set; }
        public double RV0 { get; set; }
        public double RV1 { get; set; }
        public double RV_Err0 { get; set; }
        public double RV_Err1 { get; set; }
        public double Mass0 { get; set; }
        public double Mass1 { get; set; }
        public double Distance0 { get; set; }
        public double SepAU { get; set; }
        public double Period { get; set; }
        public string WDSName { get; set; }
        public string DD { get; set; }
        public string Comp { get; set; }
        public string RA { get; set; }
        public string Decl { get; set; }
        public string PA { get; set; }
        public string Sep { get; set; }
        public string Date { get; set; }
        public string Nobs { get; set; }
        public string Mag0 { get; set; }
        public string Mag1 { get; set; }
        public string DiffMag { get; set; }
        public string SpecType { get; set; }
        public string User { get; set; }
        public string Target { get; set; }
        public int Cycles { get; set; }
        public int Nights { get; set; }
        public double ExposureTime { get; set; }
        public int Exposures { get; set; }
        public string Filter { get; set; }
        public int Priority { get; set; }
        public int Completed_nights { get; set; }
        public int Completed_cycles { get; set; }
        public int Completed_ref_cycles { get; set; }
        public double Rotation { get; set; }
        public string Template { get; set; }
        public double MaxAlt { get; set; }
        public int GetRef { get; set; }
    }

    public class GdsTargetClassMap : ClassMap<GdsTarget> {
        public GdsTargetClassMap() {
            Map(m => m.Number).Name("Number").Optional().Default("");
            Map(m => m.SeqNum0).Name("SeqNum0").Optional().Default("");
            Map(m => m.SeqNum1).Name("SeqNum1").Optional().Default("");
            Map(m => m.WDSIndex).Name("WDSIndex").Optional().Default("");
            Map(m => m.RA0).Name("RA0");
            Map(m => m.Decl0).Name("Decl0");
            Map(m => m.RA1).Name("RA1").Optional().Default("");
            Map(m => m.Decl1).Name("Decl1").Optional().Default("");
            Map(m => m.GaiaPA).Name("Gaia PA").Optional().Default(0);
            Map(m => m.GaiaSep).Name("Gaia Sep").Optional().Default(0);
            Map(m => m.GBIndex).Name("GB Index").Optional().Default(0);
            Map(m => m.num3Band).Name("3Band").Optional().Default(0);
            Map(m => m.Gmag0).Name("Gmag0").Optional().Default(0);
            Map(m => m.Gmag1).Name("Gmag1").Optional().Default(0);
            Map(m => m.BPmag0).Name("BPmag0").Optional().Default(0);
            Map(m => m.BPmag1).Name("BPmag1").Optional().Default(0);
            Map(m => m.RPmag0).Name("RPmag0").Optional().Default(0);
            Map(m => m.RPmag1).Name("RPmag1").Optional().Default(0);
            Map(m => m.PMRA0).Name("PMRA0").Optional().Default(0);
            Map(m => m.PMDec0).Name("PMDec0").Optional().Default(0);
            Map(m => m.PMRA1).Name("PMRA1").Optional().Default(0);
            Map(m => m.PMDec1).Name("PMDec1").Optional().Default(0);
            Map(m => m.PMRA_Err0).Name("PMRA_Err0").Optional().Default(0);
            Map(m => m.PMDec_Err0).Name("PMDec_Err0").Optional().Default(0);
            Map(m => m.PMRA_Err1).Name("PMRA_Err1").Optional().Default(0);
            Map(m => m.PMDec_Err1).Name("PMDec_Err1").Optional().Default(0);
            Map(m => m.Parallax0).Name("Parallax0").Optional().Default(0);
            Map(m => m.Parallax1).Name("Parallax1").Optional().Default(0);
            Map(m => m.Parallax_Err0).Name("Parallax_Err0").Optional().Default(0);
            Map(m => m.Parallax_Err1).Name(" Parallax_Err1").Optional().Default(0);
            Map(m => m.VarFlag).Name("VarFlag").Optional().Default(0);
            Map(m => m.Teff0).Name("Teff0").Optional().Default(0);
            Map(m => m.Teff1).Name("Teff1").Optional().Default(0);
            Map(m => m.Lum0).Name("Lum0").Optional().Default(0);
            Map(m => m.Lum1).Name("Lum1").Optional().Default(0);
            Map(m => m.Radius0).Name("Radius0").Optional().Default(0);
            Map(m => m.Radius1).Name("Radius1").Optional().Default(0);
            Map(m => m.RV0).Name("RV0").Optional().Default(0);
            Map(m => m.RV1).Name("RV1").Optional().Default(0);
            Map(m => m.RV_Err0).Name("RV_Err0").Optional().Default(0);
            Map(m => m.RV_Err1).Name("RV_Err1").Optional().Default(0);
            Map(m => m.Mass0).Name("Mass0").Optional().Default(0d);
            Map(m => m.Mass1).Name("Mass1").Optional().Default(0d);
            Map(m => m.Distance0).Name("Distance0").Optional().Default(0d);
            Map(m => m.SepAU).Name("SepAU").Optional().Default(0d);
            Map(m => m.Period).Name("Period").Optional().Default(0d);
            Map(m => m.WDSName).Name("WDS Name").Optional().Default("");
            Map(m => m.DD).Name("DD").Optional().Default("");
            Map(m => m.Comp).Name("Comp").Optional().Default("");
            Map(m => m.RA).Name("RA").Optional().Default("");
            Map(m => m.Decl).Name("Decl").Optional().Default("");
            Map(m => m.PA).Name("PA").Optional().Default("");
            Map(m => m.Sep).Name("Sep").Optional().Default("");
            Map(m => m.Date).Name("Date").Optional().Default("");
            Map(m => m.Nobs).Name("Nobs").Optional().Default("");
            Map(m => m.Mag0).Name("Mag0").Optional().Default("");
            Map(m => m.Mag1).Name("Mag1").Optional().Default("");
            Map(m => m.DiffMag).Name("DiffMag").Optional().Default("");
            Map(m => m.SpecType).Name("SpecType").Optional().Default("");
            Map(m => m.User).Name("User").Optional().Default("");
            Map(m => m.Target).Name("Target").Optional().Default("");
            Map(m => m.Cycles).Name("Cycles").Optional().Default(0);
            Map(m => m.Nights).Name("Nights").Optional().Default(0);
            Map(m => m.ExposureTime).Name("ExposureTime").Optional().Default(0);
            Map(m => m.Exposures).Name("Exposures").Optional().Default(0);
            Map(m => m.Filter).Name("Filter").Optional().Default("");
            Map(m => m.Priority).Name("Priority").Optional().Default(0);
            Map(m => m.Completed_cycles).Name("Completed_cycles").Optional().Default(0);
            Map(m => m.Completed_ref_cycles).Name("Completed_ref_cycles").Optional().Default(0);
            Map(m => m.Completed_nights).Name("Completed_nights").Optional().Default(0);
            Map(m => m.Rotation).Name("Rotation").Optional().Default(0);
            Map(m => m.Template).Name("Template").Optional().Default("");
            Map(m => m.MaxAlt).Name("MaxAlt").Optional().Default(90d);
            Map(m => m.GetRef).Name("GetRef").Optional().Default(1);
        }
    }
}