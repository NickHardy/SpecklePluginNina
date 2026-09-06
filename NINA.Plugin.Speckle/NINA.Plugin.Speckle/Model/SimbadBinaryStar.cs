#region "copyright"

/*
    Copyright (c) 2026 Nick Hardy and Leon Bewersdorff

    This file is part of the Speckle Interferometry plugin for
    N.I.N.A. - Nighttime Imaging 'N' Astronomy.

    Released under the MIT License. See LICENSE.txt in the repository
    root, or https://opensource.org/licenses/MIT
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
    public class SimbadBinaryStar {
        public SimbadBinaryStar(List<object> obj) {
            main_id = (string)obj[0];
            ra = Convert.ToDouble(obj[1]);
            dec = Convert.ToDouble(obj[2]);
            v_mag = Convert.ToDouble(obj[3]);
            distance = Convert.ToDouble(obj[4]);
        }

        public SimbadBinaryStar() {
            main_id = "";
        }

        [JsonProperty]
        public string main_id { get; set; }
        [JsonProperty]
        public double ra { get; set; }
        [JsonProperty]
        public double dec { get; set; }
        [JsonProperty]
        public double v_mag { get; set; }
        [JsonProperty]
        public double distance { get; set; }

        public Coordinates Coordinates() {
            return new Coordinates(Angle.ByDegree(ra), Angle.ByDegree(dec), Epoch.J2000);
        }

    }
}