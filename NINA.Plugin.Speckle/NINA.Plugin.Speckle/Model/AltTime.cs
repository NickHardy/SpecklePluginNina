﻿#region "copyright"

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
    public class AltTime {
        [JsonProperty]
        public DateTime datetime { get; set; }
        [JsonProperty]
        public double alt { get; set; }
        [JsonProperty]
        public double az { get; set; }
        [JsonProperty]
        public double deg { get; set; }
        [JsonProperty]
        public double airmass { get; set; }
        [JsonProperty]
        public double distanceToMoon { get; set; }

        public AltTime(double alt, double az, double deg, DateTime datetime, double airmass, double distanceToMoon) {
            this.alt = alt;
            this.az = az;
            this.deg = deg;
            this.datetime = datetime;
            this.airmass = airmass;
            this.distanceToMoon = distanceToMoon;
        }

    }
}