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

    public class AltTime {
        public DateTime Timestamp { get; set; }
        public double Altitude { get; set; }
        public double Azimuth { get; set; }
        public double HourAngleDegrees { get; set; }
        public double Airmass { get; set; }
        public double DistanceToMoon { get; set; }

        public AltTime(double altitude, double azimuth, double hourAngleDegrees, DateTime timestamp, double airmass, double distanceToMoon) {
            Altitude = altitude;
            Azimuth = azimuth;
            HourAngleDegrees = hourAngleDegrees;
            Timestamp = timestamp;
            Airmass = airmass;
            DistanceToMoon = distanceToMoon;
        }

    }
}