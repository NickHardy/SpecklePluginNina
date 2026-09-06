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
    public class SimbadGalaxy : Star {
        public SimbadGalaxy(List<object> obj) {
            Name1 = "GAL";
            Name2 = (string)obj[0];
            RA2000 = Convert.ToDouble(obj[1]);
            Dec2000 = Convert.ToDouble(obj[2]);
            Gmag = Convert.ToDouble(obj[3]);
            distance = Convert.ToDouble(obj[4]);
        }

        public SimbadGalaxy() {
            Name1 = "GAL";
            Name2 = "";
        }

        [JsonProperty]
        public double distance { get; set; }


    }
}