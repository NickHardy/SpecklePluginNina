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
    public class Metadata {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("description")]
        public string Description { get; set; }

        [JsonProperty("datatype")]
        public string Datatype { get; set; }

        [JsonProperty("arraysize")]
        public string Arraysize { get; set; }

        [JsonProperty("ucd")]
        public string Ucd { get; set; }

        [JsonProperty("unit")]
        public string Unit { get; set; }
    }

    [JsonObject(MemberSerialization.OptIn)]
    public class VoTable {
        [JsonProperty("metadata")]
        public List<Metadata> Metadata { get; set; }

        [JsonProperty("data")]
        public List<List<object>> Data { get; set; }

    }

}