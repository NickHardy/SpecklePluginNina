#region "copyright"

/*
    Copyright (c) 2026 Nick Hardy and Leon Bewersdorff

    This file is part of the Speckle Interferometry plugin for
    N.I.N.A. - Nighttime Imaging 'N' Astronomy.

    Released under the MIT License. See LICENSE.txt in the repository
    root, or https://opensource.org/licenses/MIT
*/

#endregion "copyright"

using Google.Protobuf.WellKnownTypes;
using Newtonsoft.Json;
using NINA.Core.Utility;

namespace NINA.Plugin.Speckle.Model {

    [JsonObject(MemberSerialization.OptIn)]

    public class Camera
    {
        [JsonProperty]
        public string CameraName { get; set; }
        [JsonProperty]
        public double PixelSize { get; set; }
        [JsonProperty]
        public double ReadNoise { get; set; }
        [JsonProperty]
        public double DarkCurrent { get; set; }
        [JsonProperty]
        public double[] ArrayQE { get; set; }

        public static Camera qhy600mPro = new Camera("QHY600M-Pro", 3.59, 1, 0.0005, new double[] { 0.51, 0.51, 0.78, 0.87, 0.89, 0.84, 0.74, 0.61, 0.54, 0.1, 0.1, 0.1, 0.1, 0.1 });
        public static Camera qhy268mPro = new Camera("QHY268M-Pro", 3.59, 1, 0.0005, new double[] { 0.51, 0.51, 0.78, 0.87, 0.89, 0.84, 0.74, 0.61, 0.54, 0.1, 0.1, 0.1, 0.1, 0.1 });

        public Camera(string cameraName, double pixelSize, double readNoise, double darkCurrent, double[] arrayQE)
        {
            CameraName = cameraName;
            PixelSize = pixelSize;
            ReadNoise = readNoise;
            DarkCurrent = darkCurrent;
            ArrayQE = arrayQE;
        }
    }
    public class Filter
    {
        [JsonProperty]
        public string FilterName { get; set; }
        [JsonProperty]
        public double[] ArrayTransmission { get; set; }

        public static Filter L = new Filter("L", new double[] { 0, 0, 0.98, 0.981, 0.9848, 0.986, 0.983, 0.0019, 0, 0, 0, 0, 0, 0 });
        public static Filter R = new Filter("R", new double[] { 0, 0, 0, 0, 0, 0.783, 0.974, 0.1979, 0, 0, 0, 0, 0, 0 });
        public static Filter G = new Filter("G", new double[] { 0, 0, 0, 0.83, 0.993, 0.0072, 0, 0, 0, 0, 0, 0, 0, 0 });
        public static Filter B = new Filter("B", new double[] { 0, 0, 0.987, 0.959, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 });

        public static Filter SU = new Filter("Sloan U", new double[] { 0.85, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 });
        public static Filter SG = new Filter("Sloan G", new double[] { 0.9, 1, 1, 1, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0 });
        public static Filter SR = new Filter("Sloan R", new double[] { 0, 0, 0, 0, 0.5, 1, 1, 1, 0, 0, 0, 0, 0, 0 });
        public static Filter SZ = new Filter("Sloan Z", new double[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0.9, 1, 1.1, 0, 0 });
        public static Filter SI = new Filter("Sloan I", new double[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 1 });
        public static Filter None = new Filter("No filter", new double[] { 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1 });

        public Filter(string filterName, double[] transmissionArray)
        {
            FilterName = filterName;
            ArrayTransmission = transmissionArray;
        }
    }

    [JsonObject(MemberSerialization.OptIn)]
    public class Telescope : BaseINPC {

        private string _telescopeName;
        [JsonProperty]
        public string TelescopeName {
            get => _telescopeName;
            set {
                _telescopeName = value;
                RaisePropertyChanged();
            }
        }

        private double _apertureD;
        [JsonProperty]
        public double ApertureD {
            get => _apertureD;
            set {
                _apertureD = value;
                RaisePropertyChanged();
            }
        }

        private double _obstructionD;
        [JsonProperty]
        public double ObstructionD {
            get => _obstructionD;
            set {
                _obstructionD = value;
                RaisePropertyChanged();
            }
        }

        private double _focallength;
        [JsonProperty]
        public double Focallength {
            get => _focallength;
            set {
                _focallength = value;
                RaisePropertyChanged();
            }
        }

                public Telescope(string telescopeName, double apertureD, double obstructionD, double focalLength) {
            TelescopeName = telescopeName;
            ApertureD = apertureD;
            ObstructionD = obstructionD;
            Focallength = focalLength;
        }
    }

    [JsonObject(MemberSerialization.OptIn)]
    public class Barlow : BaseINPC {

        private string _barlowName;
        [JsonProperty]
        public string BarlowName {
            get => _barlowName;
            set {
                _barlowName = value;
                RaisePropertyChanged();
            }
        }

        private double _barlowFactor;
        [JsonProperty]
        public double BarlowFactor {
            get => _barlowFactor;
            set {
                _barlowFactor = value;
                RaisePropertyChanged();
            }
        }

                public Barlow(string barlowName, double barlowFactor) {
            BarlowName = barlowName;
            BarlowFactor = barlowFactor;
        }
    }
}