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
    public class SimbadStar : TargetBase {
        public SimbadStar(List<object> obj) {
            main_id = (string)obj[0];
            ra = Convert.ToDouble(obj[1]);
            dec = Convert.ToDouble(obj[2]);
            v_mag = Convert.ToDouble(obj[3]);
            distance = Convert.ToDouble(obj[4]);
        }

        public SimbadStar() {
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
        [JsonProperty]
        public double color { get; set; }
        [JsonProperty]
        public string otype_txt { get; set; }
        [JsonProperty]
        public double b_mag { get; set; }

        public Coordinates Coordinates() {
            return new Coordinates(Angle.ByDegree(ra), Angle.ByDegree(dec), Epoch.J2000);
        }

    }
}