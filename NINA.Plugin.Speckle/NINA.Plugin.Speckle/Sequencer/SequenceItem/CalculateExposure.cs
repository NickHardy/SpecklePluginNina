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
using NINA.Profile.Interfaces;
using NINA.Sequencer.Container;
using NINA.Sequencer.Validations;
using NINA.Core.Utility;
using NINA.Equipment.Interfaces.Mediator;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NINA.WPF.Base.Interfaces.Mediator;
using NINA.Core.Model.Equipment;
using NINA.Core.Locale;
using NINA.Equipment.Model;
using NINA.Astrometry;
using NINA.Equipment.Equipment.MyCamera;
using NINA.WPF.Base.Interfaces.ViewModel;
using NINA.Sequencer.Interfaces;
using NINA.Sequencer.SequenceItem;
using NINA.Equipment.Interfaces.ViewModel;
using NINA.Plugin.Speckle.Sequencer.Utility;
using NINA.Image.Interfaces;
using NINA.Image.FileFormat;
using NINA.Core.Utility.Notification;
using System.Diagnostics;
using Accord.Statistics.Models.Regression.Linear;
using NINA.Image.ImageAnalysis;
// using Internal; no idea why this throws an error
using NINA.Plugin.Speckle.Sequencer.Container;

namespace NINA.Plugin.Speckle.Sequencer.SequenceItem
{
    [ExportMetadata("Name", "Speckle Exposure Time Calculation")]
    [ExportMetadata("Description", "Runs the ASD-based exposure time calculation for a given speckle target, provided both the primary and secondary magnitudes exist in the target list.")]
    [ExportMetadata("Icon", "CalculatorSVG")]
    [ExportMetadata("Category", "Speckle Interferometry")]
    [Export(typeof(ISequenceItem))]
    [JsonObject(MemberSerialization.OptIn)]
    public class CalculateExposure : NINA.Sequencer.SequenceItem.SequenceItem, IValidatable
    {
        // General:
        private double exposureTime;
        public double targetADU;
        private double gain; // Camera Gain
        private double offset;
        private BinningMode binning; // !! future todo
        private string imageType;
        private double exposureCount; // !! future todo
        private double fluxRatio; // flux ratio between magnitudeTruePrimary and magnitudeTrueSecondary
        private double magnitudePrimary; // magnitude of primary taken from target list
        private double magnitudeSecondary;
        private double magnitudeTruePrimary;
        private double magnitudeTrueSecondary;
        private double airMass;
        private double elevation;
        private double skybackground;
        private double asdSNR;
        private double combinedMagnitude;
        private double azerosum;
        private double[] arrayWavelength;
        private double intendedSNR;
        // Atmosphere:
        private double[] zeroMagA0FluxSDensity = new double[14];
        private double[] arrayZeroMagA0Fluxin50nmBW = new double[14];
        private double[] arrayPalomarExtinction = new double[14];
        private double[] arrayAtmosphericTransmission = new double[14];
        private double[] arrayZeroMagA0Starin50nmBW = new double[14];

        private IApplicationStatusMediator applicationStatusMediator;
        private IProfileService profileService;
        private IFilterWheelMediator filterWheelMediator;
        private Speckle speckle;
        [ImportingConstructor]
        public CalculateExposure(IProfileService profileService, IApplicationStatusMediator applicationStatusMediator, IFilterWheelMediator filterWheelMediator)
        {
            this.applicationStatusMediator = applicationStatusMediator;
            this.profileService = profileService;
            this.filterWheelMediator = filterWheelMediator;
            speckle = new Speckle();
            
            if(this.profileService.ActiveProfile.AstrometrySettings.Elevation == 0) // If someone's at sealevel it breaks the calculation
            {
                elevation = 1.0;
            }
            else elevation = this.profileService.ActiveProfile.AstrometrySettings.Elevation;

            // Create some of the basic equipment we use
            //public Camera(string cameraName, double readNoise, double darkCurrent, int[] arrayQE)
            Camera qhy600mPro = new Camera("QHY600M-Pro", 3.59, 1, 0.0005, new double[] { 0.51, 0.51, 0.78, 0.87, 0.89, 0.84, 0.74, 0.61, 0.54, 0.1, 0.1, 0.1, 0.1, 0.1 });
            Camera qhy268mPro = new Camera("QHY268M-Pro", 3.59, 1, 0.0005, new double[] { 0.51, 0.51, 0.78, 0.87, 0.89, 0.84, 0.74, 0.61, 0.54, 0.1, 0.1, 0.1, 0.1, 0.1 });
            

            Telescope pw1000 = new Telescope("PW1000", 1000, 470, 6000);
            Telescope cdk700 = new Telescope("CDK700", 700, 329, 4540);
            Telescope cdk24 = new Telescope("CDK24", 610, 286.7, 3974);
            Telescope cdk17 = new Telescope("CDK17", 432, 209.952, 2939);
            Telescope cdk14 = new Telescope("CDK14", 356, 172.66, 2563);
            Telescope edgehd14 = new Telescope("EdgeHD-14", 356, 114, 3910);
            Telescope gsoRc10 = new Telescope("GSO-RC10", 254, 119.38, 2039);
        }
        private CalculateExposure(CalculateExposure cloneMe) : this(cloneMe.profileService, cloneMe.applicationStatusMediator, cloneMe.filterWheelMediator)
        {
            CopyMetaData(cloneMe);
        }

        public override object Clone()
        {
            var clone = new CalculateExposure(this);

            return clone;
        }

        private IList<string> issues = new List<string>();

        public IList<string> Issues
        {
            get => issues;
            set
            {
                issues = value;
                RaisePropertyChanged();
            }
        }

        // --- Start of GetSets

        // General :
        [JsonProperty]
        public double ExposureTime { get => exposureTime; set { exposureTime = value; RaisePropertyChanged(); } }

        [JsonProperty]
        public double TargetADU { get => targetADU; set { targetADU = value; RaisePropertyChanged(); } }

        [JsonProperty]
        public double Gain { get => gain; set { gain = value; RaisePropertyChanged(); } }

        [JsonProperty]
        public double Offset { get => offset; set { offset = value; RaisePropertyChanged(); } }

        [JsonProperty]
        public BinningMode Binning { get => binning; set { binning = value; RaisePropertyChanged(); } }

        [JsonProperty]
        public string ImageType { get => imageType; set { imageType = value; RaisePropertyChanged(); } }

        [JsonProperty]
        public double ExposureCount { get => exposureCount; set { exposureCount = value; RaisePropertyChanged(); } }

        [JsonProperty]
        public double FluxRatio { get => fluxRatio; set { fluxRatio = value; RaisePropertyChanged(); } }

        [JsonProperty]
        public double MagnitudePrimary { get => magnitudePrimary; set { magnitudePrimary = value; RaisePropertyChanged(); } }

        [JsonProperty]
        public double MagnitudeSecondary { get => magnitudeSecondary; set { magnitudeSecondary = value; RaisePropertyChanged(); } }

        [JsonProperty]
        public double MagnitudeTruePrimary { get => magnitudeTruePrimary; set { magnitudeTruePrimary = value; RaisePropertyChanged(); } }

        [JsonProperty]
        public double MagnitudeTrueSecondary { get => magnitudeTrueSecondary; set { magnitudeTrueSecondary = value; RaisePropertyChanged(); } }

        [JsonProperty]
        public double AirMass { get => airMass; set { airMass = value; RaisePropertyChanged(); } }

        [JsonProperty]
        public double Elevation { get => elevation; set { elevation = value; RaisePropertyChanged(); } }

        [JsonProperty]
        public double CombinedMagnitude { get => combinedMagnitude; set { combinedMagnitude = value; RaisePropertyChanged(); } }

        //[JsonProperty]
        public double[] ArrayWavelength { get => arrayWavelength; set { arrayWavelength = value; RaisePropertyChanged(); } }

        [JsonProperty]
        public double IntendedSNR { get => intendedSNR; set { intendedSNR = value; RaisePropertyChanged(); } }

        // For the Atmosphere:
        //[JsonProperty]
        public double[] ZeroMagA0FluxSDensity { get => zeroMagA0FluxSDensity; set { zeroMagA0FluxSDensity = value; RaisePropertyChanged(); } }

        //[JsonProperty]
        public double[] ArrayZeroMagA0Fluxin50nmBW { get => arrayZeroMagA0Fluxin50nmBW; set { arrayZeroMagA0Fluxin50nmBW = value; RaisePropertyChanged(); } }

        //[JsonProperty]
        public double[] ArrayPalomarExtinction { get => arrayPalomarExtinction; set { arrayPalomarExtinction = value; RaisePropertyChanged(); } }

        //[JsonProperty]
        public double[] ArrayAtmosphericTransmission { get => arrayAtmosphericTransmission; set { arrayAtmosphericTransmission = value; RaisePropertyChanged(); } }

        //[JsonProperty]
        public double[] ArrayZeroMagA0Starin50nmBW { get => arrayZeroMagA0Starin50nmBW; set { arrayZeroMagA0Starin50nmBW = value; RaisePropertyChanged(); } }

        // --- End of GetSets

        private ObservableCollection<string> _imageTypes;
        public ObservableCollection<string> ImageTypes
        {
            get
            {
                if (_imageTypes == null)
                {
                    _imageTypes = new ObservableCollection<string>();

                    // Get the type of the CaptureSequence.ImageTypes class
                    Type type = typeof(CaptureSequence.ImageTypes);
                    // Iterate through the public static fields of the ImageTypes class
                    foreach (var p in type.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
                    {
                        // Get the value of the current field
                        var v = p.GetValue(null);
                        // Add the string representation of the value to the _imageTypes ObservableCollection
                        _imageTypes.Add(v.ToString());
                    }
                }
                // Return the ObservableCollection of image types
                return _imageTypes;
            }
            set
            {
                // Set the value of the _imageTypes field
                _imageTypes = value;
                RaisePropertyChanged();
            }
        }
        public override async Task Execute(IProgress<ApplicationStatus> progress, CancellationToken token)
        {
            Telescope pw1000 = new Telescope("PW1000", 1000.0, 470.0, 6000.0);
            Camera qhy600mPro = new Camera("QHY600M-Pro", 3.59, 1.0, 0.0005, new double[] { 0.51, 0.51, 0.78, 0.87, 0.89, 0.84, 0.74, 0.61, 0.54, 0.1, 0.1, 0.1, 0.1, 0.1 });
            Barlow twox = new Barlow("2x", 2);
            
            // Check if the calculation should be used for the target before calculating anything
            if (Utility.ItemUtility.RetrieveSpeckleTarget(Parent).NoCalculation == "1") { throw new SequenceEntityFailedException(); }

            // Calculate Atmospheric values:
            CalculateAtmosphere(pw1000, qhy600mPro, RetrieveCurrentFilter());

            try
            {
             ExposureTime = Calculate(pw1000, qhy600mPro, RetrieveCurrentFilter(), twox);
                ItemUtility.RetrieveSpeckleContainer(Parent).Items.ToList().ForEach(x => {
                    if (x is TakeRoiExposures takeRoiExposures)
                    {
                        Logger.Debug("Setting exposure time of "+ExposureTime+"..");
                        takeRoiExposures.ExposureTime = ExposureTime;
                        Logger.Debug("takeRoiExposures.ExposureTime is now "+takeRoiExposures.ExposureTime);

                    }
                    if (x is TakeLiveExposures takeLiveExposures)
                    {
                        Logger.Debug("Setting exposure time of "+ExposureTime + "..");
                        takeLiveExposures.ExposureTime = ExposureTime;
                        Logger.Debug("takeLiveExposures.ExposureTime is now "+takeLiveExposures.ExposureTime);
                    }
                });
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Notification.ShowError("Exposure Calculation error: "+Environment.NewLine + ex.Message);
                Logger.Error(ex);
                throw;
            }
            finally
            {
                progress.Report(new ApplicationStatus() { Status = "" });
            }
        }

        public override void AfterParentChanged()
        {
            Validate();
        }
        // --- Start of retrieve methods:
        public double RetrieveASDSNR(double Fluxratio, double MagnitudePrimary)
        {
            double[,] prim7 = { { 0.05, 90 }, { 0.1, 80 }, { 0.25, 60 }, { 0.5, 45 }, { 0.75, 35 }, { 1, 25 } };
            double[,] prim8 = { { 0.05, 100 }, { 0.1, 90 }, { 0.25, 70 }, { 0.5, 50 }, { 0.75, 30 }, { 1, 20 } };
            double[,] prim9 = { { 0.05, 100 }, { 0.1, 80 }, { 0.25, 70 }, { 0.5, 50 }, { 0.75, 35 }, { 1, 25 } };
            double[,] prim10 = { { 0.05, 100 }, { 0.1, 90 }, { 0.25, 70 }, { 0.5, 50 }, { 0.75, 40 }, { 1, 30 } };
            double[,] prim11 = { { 0.05, 130 }, { 0.1, 100 }, { 0.25, 70 }, { 0.5, 50 }, { 0.75, 35 }, { 1, 25 } };
            double[,] prim12 = { { 0.05, 130 }, { 0.1, 90 }, { 0.25, 70 }, { 0.5, 55 }, { 0.75, 40 }, { 1, 30 } };
            double[,] prim13 = { { 0.05, 135 }, { 0.1, 90 }, { 0.25, 75 }, { 0.5, 50 }, { 0.75, 40 }, { 1, 20 } };
            double[,] prim14 = { { 0.05, 130 }, { 0.1, 110 }, { 0.25, 70 }, { 0.5, 50 }, { 0.75, 40 }, { 1, 30 } };
            double[,] prim15 = { { 0.05, 120 }, { 0.1, 120 }, { 0.25, 90 }, { 0.5, 60 }, { 0.75, 50 }, { 1, 40 } };
            double roundedRatio = Math.Round(FluxRatio * 4) / 4;
            Logger.Debug("The fluxratio is "+FluxRatio+", rounded to "+roundedRatio+".");


            Dictionary<int, double[,]> primArrays = new Dictionary<int, double[,]>
            {
                { 7, prim7 },
                { 8, prim8 },
                { 9, prim9 },
                { 10, prim10 },
                { 11, prim11 },
                { 12, prim12 },
                { 13, prim13 },
                { 14, prim14 },
                { 15, prim15 },
            };

            // 2D array of doubles set to the value in the "primArrays" dictionary at the key specified by the MagnitudePrimary parameter
            double[,] selectedArray = primArrays[(int)MagnitudePrimary];
            
            // Iterate through rows of selectedArray
            for (int i = 0; i < selectedArray.GetLength(0); i++)
            {
                if (selectedArray[i, 0] == roundedRatio)
                {
                    // Return the value in the second column of the current row if the value in the first column of the current row is equal to "roundedRatio"
                    asdSNR = selectedArray[i, 1];
                    Logger.Debug("Found an asdSNR of "+asdSNR+" in "+selectedArray+" for the given primary magnitude of "+MagnitudePrimary+" and fluxratio.");
                    return selectedArray[i, 1];
                }
            }
            Notification.ShowError("Couldn't find a fluxratio of "+FluxRatio+" for the asdSNR. Please verify the target list.");
            return 0;
        }
        public Filter RetrieveCurrentFilter()
        {
            Filter U = new Filter("Sloan U", new double[] { 0.85, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 });
            Filter G = new Filter("Sloan G", new double[] { 0.9, 1, 1, 1, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0 });
            Filter R = new Filter("Sloan R", new double[] { 0, 0, 0, 0, 0.5, 1, 1, 1, 0, 0, 0, 0, 0, 0 });
            Filter Z = new Filter("Sloan Z", new double[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0.9, 1, 1.1, 0, 0 });
            Filter I = new Filter("Sloan I", new double[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 1 });
            Filter None = new Filter("No filter", new double[] { 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1 });
            Filter activeFilter;
            string activeFilterName = "";
            if (filterWheelMediator.GetInfo().Connected) { activeFilterName = filterWheelMediator.GetInfo().SelectedFilter.Name; }
            switch (activeFilterName)
            {
                case "Sloan U":
                    activeFilter = U;
                    break;
                case "Sloan G":
                    activeFilter = G;
                    break;
                case "Sloan R":
                    activeFilter = R;
                    break;
                case "Sloan Z":
                    activeFilter = Z;
                    break;
                case "Sloan I":
                    activeFilter = I;
                    break;
                default:
                    activeFilter = None;
                    Logger.Debug("Warning: No filter stored in the calculation matches the currently active filter's name in NINA. Assuming no filter is being used.");
                    break;
            }
            Logger.Debug("Active filter for the calculation is now '"+activeFilter+"'.");
            return activeFilter;
        }
        /*
        This retrieves the InputTarget object from an IDeepSkyObjectContainer ancestor of the given parent container.
        If no ancestor container is an IDeepSkyObjectContainer, it returns null.
        */
        private InputTarget RetrieveTarget(ISequenceContainer parent)
        {
            if (parent != null)
            {
                var container = parent as IDeepSkyObjectContainer;
                if (container != null)
                {
                    return container.Target;
                }
                else
                {
                    return RetrieveTarget(parent.Parent);
                }
            }
            else{ return null; }
        }

        public double RetrieveSkyBackground()
        {
            // This should be replaced by a table in the GUI later on where each lunar phase (Full, Waxing/Waning Gibbous), Quarter, Waxing/Waning Crescent, New)
            // corresponds to a user measured (and entered) sky background. This method can then get the current lunar phase and pick the respective skybackground.
            return 21.0;
        }

        // --- End of retrieve methods
        // --- Start of calculation methods:
        public double CalculateAtmosphere(Telescope telescope, Camera camera, Filter filter)
        {
            // Constant Atmospheric values:
            arrayWavelength = this.ArrayWavelength = new double[] { 350.0, 400.0, 450.0, 500.0, 550.0, 600.0, 650.0, 700.0, 750.0, 800.0, 850.0, 900.0, 950.0, 1000.0 }; // Add Wavelengths
            arrayPalomarExtinction = this.ArrayPalomarExtinction = new double[] { 0.67, 0.36, 0.24, 0.18, 0.15, 0.13, 0.1, 0.07, 0.06, 0.05, 0.04, 0.04, 0.04, 0.03 };
            ZeroMagA0FluxSDensity = this.ZeroMagA0FluxSDensity = new double[] { 3.5, 7.5, 6.5, 4.9, 3.55, 2.8, 2.1, 1.7, 1.45, 1.1, 1.0, 0.9, 0.75, 0.65 };
            AirMass = 1;
            Logger.Debug("Elevation: " + Elevation);
            Logger.Debug("Airmass: " + AirMass);
            Logger.Debug("Palomar: " + ArrayPalomarExtinction[0]);
            //Logger.Debug("arrayatmosphericTransmission[i]:");
            //Logger.Debug(filter.FilterName+", "+filter.ArrayTransmission[0]);

            for (int i = 0; i < ArrayPalomarExtinction.Length; i++) // Fill arrayAtmosphericTransmission
            {

                ArrayAtmosphericTransmission[i] = Math.Pow(10, (-0.4 * AirMass * ArrayPalomarExtinction[i] * Math.Exp(-Elevation / 2500.0) / Math.Exp(-2000.0 / 2500.0)));
                //Logger.Debug("" + ArrayAtmosphericTransmission[i]);
            }

            //Logger.Debug("------------");
            //Logger.Debug("ArrayZeroMagA0Fluxin50nmBW[i]:");
            for (int i = 0; i < arrayPalomarExtinction.Length; i++) // Fill ArrayZeroMagA0Fluxin50nmBW
            {
                ArrayZeroMagA0Fluxin50nmBW[i] = 500.0 * (ZeroMagA0FluxSDensity[i] * 0.000000001) / (2.0 * Math.Pow(10.0, -18.0) / (ArrayWavelength[i] * 0.000000001));
                //Logger.Debug("" + ArrayZeroMagA0Fluxin50nmBW[i]);

            }

            //Logger.Debug("------------");
            //Logger.Debug("ArrayZeroMagA0STARin50nmBW[i]:");
            for (int i = 0; i < ArrayZeroMagA0Starin50nmBW.Length; i++) // Fill ArrayZeroMagA0Starin50nmBW
            {
                ArrayZeroMagA0Starin50nmBW[i] = (ArrayZeroMagA0Fluxin50nmBW[i] * ArrayAtmosphericTransmission[i] * 0.6 * filter.ArrayTransmission[i] * camera.ArrayQE[i] * ((Math.Pow(telescope.Focallength / 10.0, 2.0) - Math.Pow(telescope.ObstructionD / 10.0, 2.0)) * 0.785)) / 45.92362983;
                //Logger.Debug("" + ArrayZeroMagA0Starin50nmBW[i]);

            }
            // ^ sum of this:
            foreach (double value in arrayZeroMagA0Starin50nmBW)
            {
                azerosum += value;
            }
            //Logger.Debug("azerosum is "+azerosum);
            return azerosum;

        }
        // Main iterative calculation method:
        public double Calculate(Telescope telescope, Camera camera, Filter filter, Barlow barlow)
        {
            var speckleTarget = Utility.ItemUtility.RetrieveSpeckleTarget(Parent);

            MagnitudeTruePrimary = Math.Min(speckleTarget.Magnitude, speckleTarget.Magnitude2);
            if (MagnitudeTruePrimary < 7 || MagnitudeTruePrimary > 15) { throw new SequenceEntityFailedException("Calculation requested for " + Utility.ItemUtility.RetrieveSpeckleTarget(Parent).Target + ", but primary mag is not in range of ASD simulations. Falling back to user's time in list."); } 
            Logger.Debug("True Primary is "+MagnitudeTruePrimary);
            
            MagnitudeTrueSecondary = Math.Max(speckleTarget.Magnitude, speckleTarget.Magnitude2);

            Logger.Debug("True Secondary is " + MagnitudeTrueSecondary);
            FluxRatio = Math.Pow(100, (MagnitudeTruePrimary - MagnitudeTrueSecondary) / 5.0);
            CombinedMagnitude = 2.5 * Math.Log10(1.0 / (Math.Pow(10.0, ((MagnitudeTrueSecondary - MagnitudeTruePrimary) / 2.5)) + 1.0)) + MagnitudeTrueSecondary;

            double photonshotnoise = 0; 
            double darkcurrent = 0;  
            double skyglownoise = 0; 
            double totalnoise = 0;  
            double tempsignal = 0;
            double tempSNR = 0;
            skybackground = RetrieveSkyBackground();
            double exposureTime = 0.002;
            double SNR = 0;
            double minExposure = 0.02; // Minimum exposure time of 20ms; can be changed by user later
            if (intendedSNR != 0) 
            { 
                SNR = IntendedSNR; // in case user wants to override SNR with an intended SNR
                Logger.Debug("User override using intended SNR of " + IntendedSNR + ".");
            } 
            else
            { 
                SNR = RetrieveASDSNR(FluxRatio, MagnitudeTruePrimary);
                Logger.Debug("retrieveASDSNR returned "+ RetrieveASDSNR(FluxRatio, MagnitudeTruePrimary)+" for "+speckleTarget.Target+".");
            }
            if(barlow.BarlowFactor == 1) { Logger.Debug("Using pixelsize of " + camera.PixelSize + " microns and FL of " + telescope.Focallength + "mm."); }
            else { Logger.Debug("Using pixelsize of " + camera.PixelSize + " microns and FL of " + telescope.Focallength*barlow.BarlowFactor + "mm due to the "+barlow.BarlowFactor+"x barlow."); }
            
            double imagescale = ((206.265 * camera.PixelSize) / (telescope.Focallength));
            double RNinPE = camera.ReadNoise * Math.Sqrt(Math.Pow(30.0,2.0) * 0.785); // const 30, for pw1000

            Logger.Debug("Iteration starting with: SNR: " + SNR + ", RNinPE: " + RNinPE + ", " +
                    "imagescale: " + imagescale + ", magtp: " + magnitudeTruePrimary + ", magts: " + magnitudeTrueSecondary + ", fluxr: " + FluxRatio+".");
            Logger.Debug("azerosum is currently "+azerosum);
            Logger.Debug("darkcurrent is currently " + camera.DarkCurrent);

            do
            {
                tempsignal = 1.0 * azerosum * Math.Pow(10.0, (-0.4 * CombinedMagnitude)) * exposureTime;
                skyglownoise = Math.Sqrt(Math.Pow((imagescale * 35.0), 2.0) * 0.785 * azerosum * Math.Pow(10,(-0.4 * skybackground)) * exposureTime);
                darkcurrent = Math.Sqrt(camera.DarkCurrent * exposureTime * Math.Pow(35.0,2.0) * 0.785); // const 35
                photonshotnoise = Math.Sqrt(tempsignal);//* Math.Pow(10.0, (skybackground / 20.0)); Shot Noise Penalty not considered
                totalnoise = Math.Sqrt(Math.Pow(photonshotnoise, 2.0) + Math.Pow(camera.ReadNoise, 2.0) + Math.Pow(skybackground,2) + Math.Pow(camera.DarkCurrent,2.0));

                tempSNR = Math.Round(tempsignal / totalnoise);

                if (tempSNR > SNR)
                {
                    Logger.Debug("FINISHED: tempSNR " +tempSNR+ " is greater than " +SNR+ ", returning exp. time: "+ exposureTime+" with primmag. of " +MagnitudeTruePrimary+" and secmag. of "+MagnitudeTrueSecondary+".");
                    if (exposureTime + 0.00 < minExposure) { return minExposure; }
                    else { return exposureTime + 0.002; }
                }

                exposureTime += 0.002;

                //progress.Report(new ApplicationStatus() { Status = "Calculating exposure time: " + ExposureTime });
                Logger.Debug("Debug iteration: exposureTime: " + exposureTime + ", tempsignal: " + tempsignal+ ", " +
                    "skyglownoise: " + skyglownoise + ", darkcurrent: " + darkcurrent + ", photonshot: " + photonshotnoise + ", total: " + totalnoise+".");

            } while (exposureTime < 5.0);
            throw new SequenceEntityFailedException("Calculation failed for " +speckleTarget.Target+" as exposure time exceeded the maximum (5s).");
        }
        // --- End of calculation methods

        public bool Validate()
        {
            var i = new List<string>();
            
            // If the current object instance is not within a SpeckleTargetContainer or SpeckleListContainer
            if (ItemUtility.RetrieveSpeckleContainer(Parent) == null && ItemUtility.RetrieveSpeckleListContainer(Parent) == null)
            {
                i.Add("This instruction only works within a SpeckleTargetContainer.");
            }
            return i.Count == 0;
        }
        public override string ToString()
        {
            return $"Category: {Category}, Item: {nameof(CalculateExposure)}, ExposureTime {ExposureTime}, Gain {Gain}, Offset {Offset}, ImageType {ImageType}, Binning {Binning?.Name}";
        }


        
    }

    // Equipment classes (Camera, Telescope, Barlow, Filter):
    public class Camera
    {

        private string cameraName;
        private double pixelSize;
        private double readNoise;
        private double darkCurrent;
        private double[] arrayQE;
        [JsonProperty]
        public string CameraName { get => cameraName; set { cameraName = value; } }
        [JsonProperty]
        public double PixelSize { get => pixelSize; set { pixelSize = value; } }
        [JsonProperty]
        public double ReadNoise { get => readNoise; set { readNoise = value; } }
        [JsonProperty]
        public double DarkCurrent { get => darkCurrent; set { darkCurrent = value; } }
        [JsonProperty]
        public double[] ArrayQE { get => arrayQE; set { arrayQE = value; } }

        // Constructor for Camera
        [ImportingConstructor]
        public Camera(string cameraName, double pixelSize, double readNoise, double darkCurrent, double[] arrayQE)
        {
            this.cameraName = cameraName;
            this.pixelSize = pixelSize;
            this.readNoise = readNoise;
            this.darkCurrent = darkCurrent;
            this.arrayQE = arrayQE;
        }
    }

    /* Filter class for information about a specific filter, its name and transmission array.
     *
     *
     */
    public class Filter
    {
        private string filterName;
        private double[] arrayTransmission;
        [JsonProperty]
        public string FilterName { get => filterName; set { filterName = value; } }
        [JsonProperty]
        public double[] ArrayTransmission { get => arrayTransmission; set { arrayTransmission = value; } }

        // Constructor for Filter
        [ImportingConstructor]
        public Filter(string filterName, double[] transmissionArray)
        {
            this.filterName = filterName;
            this.arrayTransmission = transmissionArray;
        }
    }

    /* Telescope class for information about a specific telescope, its name, aperture size, obstruction size, and focal length.
     *
     *
     */
    public class Telescope
    {
        private string telescopeName;
        private double apertureD;
        private double obstructionD;
        private double focallength;
        [JsonProperty]
        public string TelescopeName { get => telescopeName; set { telescopeName = value; } }
        [JsonProperty]
        public double ApertureD { get => apertureD; set { apertureD = value; } }
        [JsonProperty]
        public double ObstructionD { get => obstructionD; set { obstructionD = value; } }
        [JsonProperty]
        public double Focallength { get => focallength; set { focallength = value; } }

        // Constructor for Telescope
        [ImportingConstructor]
        public Telescope(string telescopeName, double apertureD, double obstructionD, double focalLength)
        {
            this.telescopeName = telescopeName;
            this.apertureD = apertureD;
            this.obstructionD = obstructionD;
            this.focallength = focalLength;
        }
    }

    /* Barlow class for information about a specific barlow, its name, power factor.
     *
     *
     */
    public class Barlow
    {
        private string barlowname; // barlow Name
        private double barlowfactor; // barlow factor
        [JsonProperty]
        public string BarlowName { get => barlowname; set { barlowname = value; } }
        [JsonProperty]
        public double BarlowFactor { get => barlowfactor; set { barlowfactor = value; } }

        // Constructor for Telescope
        [ImportingConstructor]
        public Barlow(string barlowName, double BarlowFactor)
        {
            this.barlowname = barlowName;
            this.barlowfactor = BarlowFactor;
        }
    }
}