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
        public int Number { get; set; }
        public int SeqNum0 { get; set; }
        public int SeqNum1 { get; set; }
        public int WDSIndex { get; set; }
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
        public string BPmag1 { get; set; }
        public double RPmag0 { get; set; }
        public string RPmag1 { get; set; }
        public double PMRA0 { get; set; }
        public double PMDec0 { get; set; }
        public string PMRA1 { get; set; }
        public string PMDec1 { get; set; }
        public double PMRA_Err0 { get; set; }
        public double PMDec_Err0 { get; set; }
        public string PMRA_Err1 { get; set; }
        public string PMDec_Err1 { get; set; }
        public double Parallax0 { get; set; }
        public string Parallax1 { get; set; }
        public double Parallax_Err0 { get; set; }
        public string Parallax_Err1 { get; set; }
        public int VarFlag { get; set; }
        public double Teff0 { get; set; }
        public string Teff1 { get; set; }
        public string Lum0 { get; set; }
        public string Lum1 { get; set; }
        public string Radius0 { get; set; }
        public string Radius1 { get; set; }
        public string RV0 { get; set; }
        public string RV1 { get; set; }
        public string RV_Err0 { get; set; }
        public string RV_Err1 { get; set; }
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
    }

    public class GdsTargetClassMap : ClassMap<GdsTarget> {
        public GdsTargetClassMap() {
            Map(m => m.Number).Name("Number");
            Map(m => m.SeqNum0).Name("SeqNum0");
            Map(m => m.SeqNum1).Name("SeqNum1");
            Map(m => m.WDSIndex).Name("WDSIndex");
            Map(m => m.RA0).Name("RA0");
            Map(m => m.Decl0).Name("Decl0");
            Map(m => m.RA1).Name("RA1");
            Map(m => m.Decl1).Name("Decl1");
            Map(m => m.GaiaPA).Name("Gaia PA");
            Map(m => m.GaiaSep).Name("Gaia Sep");
            Map(m => m.GBIndex).Name("GB Index");
            Map(m => m.num3Band).Name("3Band");
            Map(m => m.Gmag0).Name("Gmag0");
            Map(m => m.Gmag1).Name("Gmag1");
            Map(m => m.BPmag0).Name("BPmag0");
            Map(m => m.BPmag1).Name("BPmag1");
            Map(m => m.RPmag0).Name("RPmag0");
            Map(m => m.RPmag1).Name("RPmag1");
            Map(m => m.PMRA0).Name("PMRA0");
            Map(m => m.PMDec0).Name("PMDec0");
            Map(m => m.PMRA1).Name("PMRA1");
            Map(m => m.PMDec1).Name("PMDec1");
            Map(m => m.PMRA_Err0).Name("PMRA_Err0");
            Map(m => m.PMDec_Err0).Name("PMDec_Err0");
            Map(m => m.PMRA_Err1).Name("PMRA_Err1");
            Map(m => m.PMDec_Err1).Name("PMDec_Err1");
            Map(m => m.Parallax0).Name("Parallax0");
            Map(m => m.Parallax1).Name("Parallax1");
            Map(m => m.Parallax_Err0).Name("Parallax_Err0");
            Map(m => m.Parallax_Err1).Name(" Parallax_Err1");
            Map(m => m.VarFlag).Name("VarFlag");
            Map(m => m.Teff0).Name("Teff0");
            Map(m => m.Teff1).Name("Teff1");
            Map(m => m.Lum0).Name("Lum0");
            Map(m => m.Lum1).Name("Lum1");
            Map(m => m.Radius0).Name("Radius0");
            Map(m => m.Radius1).Name("Radius1");
            Map(m => m.RV0).Name("RV0");
            Map(m => m.RV1).Name("RV1");
            Map(m => m.RV_Err0).Name("RV_Err0");
            Map(m => m.RV_Err1).Name("RV_Err1");
            Map(m => m.Mass0).Name("Mass0");
            Map(m => m.Mass1).Name("Mass1");
            Map(m => m.Distance0).Name("Distance0");
            Map(m => m.SepAU).Name("SepAU");
            Map(m => m.Period).Name("Period");
            Map(m => m.WDSName).Name("WDS Name");
            Map(m => m.DD).Name("DD");
            Map(m => m.Comp).Name("Comp");
            Map(m => m.RA).Name("RA");
            Map(m => m.Decl).Name("Decl");
            Map(m => m.PA).Name("PA");
            Map(m => m.Sep).Name("Sep");
            Map(m => m.Date).Name("Date");
            Map(m => m.Nobs).Name("Nobs");
            Map(m => m.Mag0).Name("Mag0");
            Map(m => m.Mag1).Name("Mag1");
            Map(m => m.DiffMag).Name("DiffMag");
            Map(m => m.SpecType).Name("SpecType");
        }
    }
}