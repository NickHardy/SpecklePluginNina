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
using NINA.Plugin.Speckle.Sequencer.SequenceItem;

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
        public string Name {
            get => Name1 + "_" + (string.IsNullOrWhiteSpace(Name2) ? "Gaia-" + GaiaNum.ToString() : Name2);
        }

        [JsonProperty]
        public double distance { get; set; }

        [JsonProperty]
        public double color { get; set; }

        public string Title {
            get => $"{Name1}, Distance: {Math.Round(distance, 3)}°, Color: {Math.Round(color, 2)} (B-V), VMag: {Math.Round(Rp, 2)}";
        }

        public Coordinates Coordinates() {
            return new Coordinates(Angle.ByDegree(RA2000), Angle.ByDegree(Dec2000), Epoch.J2000);
        }

    }
}