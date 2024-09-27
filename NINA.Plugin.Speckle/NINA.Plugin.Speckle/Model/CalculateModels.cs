#region "copyright"
/*
    Copyright © 2016 - 2021 Stefan Berg <isbeorn86+NINA@googlemail.com> and the N.I.N.A. contributors

    This file is part of N.I.N.A. - Nighttime Imaging 'N' Astronomy.

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/
#endregion "copyright"

using Google.Protobuf.WellKnownTypes;
using Newtonsoft.Json;
using NINA.Core.Utility;


namespace NINA.Plugin.Speckle.Model {

    [JsonObject(MemberSerialization.OptIn)]

    // Equipment classes (Camera, Telescope, Barlow, Filter). Can be removed with the addition of a selection GUI
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

        // Some defaults
        public static Camera qhy600mPro = new Camera("QHY600M-Pro", 3.59, 1, 0.0005, new double[] { 0.51, 0.51, 0.78, 0.87, 0.89, 0.84, 0.74, 0.61, 0.54, 0.1, 0.1, 0.1, 0.1, 0.1 });
        public static Camera qhy268mPro = new Camera("QHY268M-Pro", 3.59, 1, 0.0005, new double[] { 0.51, 0.51, 0.78, 0.87, 0.89, 0.84, 0.74, 0.61, 0.54, 0.1, 0.1, 0.1, 0.1, 0.1 });

        // Constructor for Camera
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

        // Some defaults
        // Wavelengths  350    400    450    500    550    600    650    700    750    800    850    900    950    1000
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

        // Constructor for Filter
        public Filter(string filterName, double[] transmissionArray)
        {
            FilterName = filterName;
            ArrayTransmission = transmissionArray;
        }
    }
    public class Telescope : BaseINPC {

        private string _telescopeName;
        [JsonProperty]
        public string TelescopeName {
            get => _telescopeName;
            set {
                _telescopeName = value;
                RaisePropertyChanged();
                Update();
            }
        }

        private double _apertureD;
        [JsonProperty]
        public double ApertureD {
            get => _apertureD;
            set {
                _apertureD = value;
                RaisePropertyChanged();
                Update();
            }
        }

        private double _obstructionD;
        [JsonProperty]
        public double ObstructionD {
            get => _obstructionD;
            set {
                _obstructionD = value;
                RaisePropertyChanged();
                Update();
            }
        }

        private double _focallength;
        [JsonProperty]
        public double Focallength {
            get => _focallength;
            set {
                _focallength = value;
                RaisePropertyChanged();
                Update();
            }
        }

        // Some defaults
/*        public static Telescope pw1000 = new Telescope("PW1000", 1000, 470, 6000);
        public static Telescope cdk700 = new Telescope("CDK700", 700, 329, 4540);
        public static Telescope cdk24 = new Telescope("CDK24", 610, 286.7, 3974);
        public static Telescope cdk17 = new Telescope("CDK17", 432, 209.952, 2939);
        public static Telescope cdk14 = new Telescope("CDK14", 356, 172.66, 2563);
        public static Telescope edgehd14 = new Telescope("EdgeHD-14", 356, 114, 3910);
        public static Telescope gsoRc10 = new Telescope("GSO-RC10", 254, 119.38, 2039);*/

        // Constructor for Telescope
        public Telescope(string telescopeName, double apertureD, double obstructionD, double focalLength)
        {
            TelescopeName = telescopeName;
            ApertureD = apertureD;
            ObstructionD = obstructionD;
            Focallength = focalLength;
        }

        private void Update() {
            Properties.Settings.Default.Telescope = JsonConvert.SerializeObject(this);
            CoreUtil.SaveSettings(Properties.Settings.Default);
        }
    }
    public class Barlow : BaseINPC {

        private string _barlowName;
        [JsonProperty]
        public string BarlowName {
            get => _barlowName;
            set {
                _barlowName = value;
                RaisePropertyChanged();
                Update();
            }
        }

        private double _barlowFactor;
        [JsonProperty]
        public double BarlowFactor {
            get => _barlowFactor;
            set {
                _barlowFactor = value;
                RaisePropertyChanged();
                Update();
            }
        }

        // Some defaults
/*        public static Barlow onex = new Barlow("No Barlow", 1);
        public static Barlow oneonehalfx = new Barlow("1.5x Barlow", 1.5);
        public static Barlow twox = new Barlow("2x Barlow", 2);
        public static Barlow twoonehalfx = new Barlow("2.5x Barlow", 2.5);
        public static Barlow threex = new Barlow("3x Barlow", 3);
        public static Barlow threeonehalfx = new Barlow("3.5x Barlow", 3.5);*/

        // Constructor for Telescope
        public Barlow(string barlowName, double barlowFactor)
        {
            BarlowName = barlowName;
            BarlowFactor = barlowFactor;
        }

        private void Update() {
            Properties.Settings.Default.Barlow = JsonConvert.SerializeObject(this);
            CoreUtil.SaveSettings(Properties.Settings.Default);
        }
    }
}