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