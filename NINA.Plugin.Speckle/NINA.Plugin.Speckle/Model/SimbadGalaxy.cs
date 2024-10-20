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

        public Coordinates Coordinates() {
            return new Coordinates(Angle.ByDegree(RA2000), Angle.ByDegree(Dec2000), Epoch.J2000);
        }

    }
}