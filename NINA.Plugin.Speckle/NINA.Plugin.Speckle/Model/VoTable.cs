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