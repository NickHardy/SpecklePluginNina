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
    [ExportMetadata("Description", "Runs the ASD-based exposure time calculation for a given speckle target, provided both the primary and secondary magnitudes exist in the target list. (V1.1)")]
    [ExportMetadata("Icon", "CalculatorSVG")]
    [ExportMetadata("Category", "Speckle Interferometry")]
    [Export(typeof(ISequenceItem))]
    [JsonObject(MemberSerialization.OptIn)]

    
    public class CalculateExposure : NINA.Sequencer.SequenceItem.SequenceItem, IValidatable
    {
        


        // General:
        private double exposureTime; // The calculated exposureTime
        private double exposureTimePrecision; // For tuning later in GUI settings in case it runs too slowly, or higher precision is needed with future brighter magnitudes
        private BinningMode binning; //todo
        private double exposureCount; // todo
        private double fluxRatio; // Between magnitudeTruePrimary and magnitudeTrueSecondary
        private double magnitudePrimary; // Magnitude of primary taken from target list
        private double magnitudeSecondary; // Magnitude of secondary taken from target list

        // The "true" primary and secondary stars, i.e. the brighter and fainter ones. In case the secondary is brighter (rarely in reality, or very likely switched by accident) the flux ratio calculation isn't broken.
        private double magnitudeTruePrimary; 
        private double magnitudeTrueSecondary;

        private double airMass;
        private double elevation;
        private double skybackground;
        private double asdSNR; // See documentation 
        private double combinedMagnitude;
        private double azerosum; // Sum of arrayZeroMagA0Starin50nmBW values
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
        private ITelescopeConsumer telescopeConsumer;
        private Speckle speckle;
        [ImportingConstructor]
        public CalculateExposure(IProfileService profileService, IApplicationStatusMediator applicationStatusMediator, IFilterWheelMediator filterWheelMediator, ITelescopeConsumer telescopeConsumer)
        {
            this.applicationStatusMediator = applicationStatusMediator;
            this.profileService = profileService;
            this.filterWheelMediator = filterWheelMediator;
            speckle = new Speckle();
            if(this.profileService.ActiveProfile.AstrometrySettings.Elevation == 0) // If someone's at sealevel it breaks the calculation. Negative and positive is okay.
            {
                elevation = 1.0;
            }
            else elevation = this.profileService.ActiveProfile.AstrometrySettings.Elevation;
        }
        private CalculateExposure(CalculateExposure cloneMe) : this(cloneMe.profileService, cloneMe.applicationStatusMediator, cloneMe.filterWheelMediator, cloneMe.telescopeConsumer)
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
        public double ExposureTime { get => exposureTime; set { exposureTime = value; RaisePropertyChanged(); } }
        public BinningMode Binning { get => binning; set { binning = value; RaisePropertyChanged(); } }
        public double ExposureCount { get => exposureCount; set { exposureCount = value; RaisePropertyChanged(); } } // This should be incorporated in future.
        public double ExposureTimePrecision { get => exposureTimePrecision; set { exposureTimePrecision = value; RaisePropertyChanged(); } }
        public double FluxRatio { get => fluxRatio; set { fluxRatio = value; RaisePropertyChanged(); } }
        public double MagnitudePrimary { get => magnitudePrimary; set { magnitudePrimary = value; RaisePropertyChanged(); } }
        public double MagnitudeSecondary { get => magnitudeSecondary; set { magnitudeSecondary = value; RaisePropertyChanged(); } }
        public double MagnitudeTruePrimary { get => magnitudeTruePrimary; set { magnitudeTruePrimary = value; RaisePropertyChanged(); } }
        public double MagnitudeTrueSecondary { get => magnitudeTrueSecondary; set { magnitudeTrueSecondary = value; RaisePropertyChanged(); } }
        public double CombinedMagnitude { get => combinedMagnitude; set { combinedMagnitude = value; RaisePropertyChanged(); } }
        public double AirMass { get => airMass; set { airMass = value; RaisePropertyChanged(); } }
        public double Elevation { get => elevation; set { elevation = value; RaisePropertyChanged(); } }
        public double[] ArrayWavelength { get => arrayWavelength; set { arrayWavelength = value; RaisePropertyChanged(); } }
        public double IntendedSNR { get => intendedSNR; set { intendedSNR = value; RaisePropertyChanged(); } }
        public double[] ZeroMagA0FluxSDensity { get => zeroMagA0FluxSDensity; set { zeroMagA0FluxSDensity = value; RaisePropertyChanged(); } }
        public double[] ArrayZeroMagA0Fluxin50nmBW { get => arrayZeroMagA0Fluxin50nmBW; set { arrayZeroMagA0Fluxin50nmBW = value; RaisePropertyChanged(); } }
        public double[] ArrayPalomarExtinction { get => arrayPalomarExtinction; set { arrayPalomarExtinction = value; RaisePropertyChanged(); } }
        public double[] ArrayAtmosphericTransmission { get => arrayAtmosphericTransmission; set { arrayAtmosphericTransmission = value; RaisePropertyChanged(); } }
        public double[] ArrayZeroMagA0Starin50nmBW { get => arrayZeroMagA0Starin50nmBW; set { arrayZeroMagA0Starin50nmBW = value; RaisePropertyChanged(); } }

        // --- End of GetSets
        public override async Task Execute(IProgress<ApplicationStatus> progress, CancellationToken token)
        {
            // Once a GUI is added, these would point towards what the user has selected. For now they are bound to this.
            Telescope telescope = Telescope.pw1000;
            Camera camera = Camera.qhy600mPro;
            Barlow barlow = Barlow.twox;

            AirMass = RetrieveAirmass();
            Elevation = RetrieveElevation();
            skybackground = RetrieveSkyBackground();
            ExposureTimePrecision = 0.002;
            //Binning = 1;

            // Check if the calculation should be used for the target before calculating anything
            if (Utility.ItemUtility.RetrieveSpeckleTarget(Parent).NoCalculation == "1") { throw new SequenceEntityFailedException(); }

            // Calculate Atmospheric values:
            CalculateAtmosphere(telescope, camera, RetrieveCurrentFilter());

            try
            {
             ExposureTime = Calculate(telescope, camera, RetrieveCurrentFilter(), barlow);
             progress.Report(new ApplicationStatus() {Status = "Calculated exposure time: "+ExposureTime});
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
            Filter activeFilter;
            string activeFilterName = "";
            if (filterWheelMediator.GetInfo().Connected) { activeFilterName = filterWheelMediator.GetInfo().SelectedFilter.Name; }
            switch (activeFilterName)
            {
                case "Sloan U":
                    activeFilter = Filter.U;
                    break;
                case "Sloan G":
                    activeFilter = Filter.G;
                    break;
                case "Sloan R":
                    activeFilter = Filter.R;
                    break;
                case "Sloan Z":
                    activeFilter = Filter.Z;
                    break;
                case "Sloan I":
                    activeFilter = Filter.I;
                    break;
                default:
                    activeFilter = Filter.None;
                    Logger.Debug("Warning: No filter stored in the calculation matches the currently active filter's name in NINA. Assuming no filter is being used.");
                    break;
            }
            Logger.Debug("Active filter for the calculation is now '"+activeFilter+"'.");
            return activeFilter;
        }
        public double RetrieveSkyBackground()
        {
            // This should be replaced by a table in the GUI later on where each lunar phase (Full, Waxing/Waning Gibbous), Quarter, Waxing/Waning Crescent, New)
            // corresponds to a user measured (and entered) sky background. This method can then get the current lunar phase and pick the respective skybackground.
            return 21.0;
        }
        public double RetrieveAirmass()
        {
            var speckleTarget = Utility.ItemUtility.RetrieveSpeckleTarget(Parent);
            var lat = profileService.ActiveProfile.AstrometrySettings.Latitude;
            var longt = profileService.ActiveProfile.AstrometrySettings.Longitude;
            return speckleTarget.Coordinates().Transform(Angle.ByDegree(lat), Angle.ByDegree(longt), DateTime.Now).Altitude.Degree;
        }

        public double RetrieveElevation()
        {
            return profileService.ActiveProfile.AstrometrySettings.Elevation;
        }

        public double CalculateAtmosphere(Telescope telescope, Camera camera, Filter filter)
        {
            // Constant Atmospheric values:
            arrayWavelength = this.ArrayWavelength = new double[] { 350.0, 400.0, 450.0, 500.0, 550.0, 600.0, 650.0, 700.0, 750.0, 800.0, 850.0, 900.0, 950.0, 1000.0 }; // Add Wavelengths
            arrayPalomarExtinction = this.ArrayPalomarExtinction = new double[] { 0.67, 0.36, 0.24, 0.18, 0.15, 0.13, 0.1, 0.07, 0.06, 0.05, 0.04, 0.04, 0.04, 0.03 };
            ZeroMagA0FluxSDensity = this.ZeroMagA0FluxSDensity = new double[] { 3.5, 7.5, 6.5, 4.9, 3.55, 2.8, 2.1, 1.7, 1.45, 1.1, 1.0, 0.9, 0.75, 0.65 };
            Logger.Debug("Elevation: " + Elevation);
            Logger.Debug("Airmass: " + AirMass);
            Logger.Debug("Palomar: " + ArrayPalomarExtinction[0]);

            for (int i = 0; i < ArrayPalomarExtinction.Length; i++) // Fill arrayAtmosphericTransmission
            {

                ArrayAtmosphericTransmission[i] = Math.Pow(10, (-0.4 * AirMass * ArrayPalomarExtinction[i] * Math.Exp(-Elevation / 2500.0) / Math.Exp(-2000.0 / 2500.0)));
            }
            for (int i = 0; i < arrayPalomarExtinction.Length; i++) // Fill ArrayZeroMagA0Fluxin50nmBW
            {
                ArrayZeroMagA0Fluxin50nmBW[i] = 500.0 * (ZeroMagA0FluxSDensity[i] * 0.000000001) / (2.0 * Math.Pow(10.0, -18.0) / (ArrayWavelength[i] * 0.000000001));

            }
            for (int i = 0; i < ArrayZeroMagA0Starin50nmBW.Length; i++) // Fill ArrayZeroMagA0Starin50nmBW
            {
                ArrayZeroMagA0Starin50nmBW[i] = (ArrayZeroMagA0Fluxin50nmBW[i] * ArrayAtmosphericTransmission[i] * 0.6 * filter.ArrayTransmission[i] * camera.ArrayQE[i] * ((Math.Pow(telescope.Focallength / 10.0, 2.0) - Math.Pow(telescope.ObstructionD / 10.0, 2.0)) * 0.785)) / 45.92362983;

            }
            // ^ sum of this:
            foreach (double value in arrayZeroMagA0Starin50nmBW)
            {
                azerosum += value;
            }
            Logger.Debug("The sum of arrayZeroMagA0Starin50nmBW is " + azerosum+".");
            return azerosum;

        }
        // Main iterative calculation method:
        public double Calculate(Telescope telescope, Camera camera, Filter filter, Barlow barlow)
        {
            var speckleTarget = Utility.ItemUtility.RetrieveSpeckleTarget(Parent);

            MagnitudeTruePrimary = Math.Min(speckleTarget.Magnitude, speckleTarget.Magnitude2);
            if (MagnitudeTruePrimary < 7 || MagnitudeTruePrimary > 15) 
            { 
                throw new SequenceEntityFailedException("Calculation requested for "+speckleTarget.Target+", but primary mag is not in range of ASD simulations. Falling back to user's time in list."); 
            } 
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
            double exposureTime = 0.002;
            double SNR = 0;
            double minExposure = 0.02; // Minimum exposure time of 20ms; can be changed by user later
            double imagescale = ((206.265 * camera.PixelSize) / (telescope.Focallength));
            double RNinPE = camera.ReadNoise * Math.Sqrt(Math.Pow(30.0, 2.0) * 0.785);

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

            // Debug logs:
            if(barlow.BarlowFactor == 1) { Logger.Debug("Using pixelsize of " + camera.PixelSize + " microns and FL of " + telescope.Focallength + "mm."); }
            else { Logger.Debug("Using pixelsize of " + camera.PixelSize + " microns and FL of " + telescope.Focallength*barlow.BarlowFactor + "mm due to the "+barlow.BarlowFactor+"x barlow."); }
            Logger.Debug("Iteration starting with: SNR: " + SNR + ", RNinPE: " + RNinPE + ", " +
                    "imagescale: " + imagescale + "arcsec/px, magtp: " + magnitudeTruePrimary + ", magts: " + magnitudeTrueSecondary + ", fluxr: " + FluxRatio+", precision: "+ExposureTimePrecision+"s.");

            // Calculation by iteration:
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
                    else { return exposureTime + ExposureTimePrecision; }
                }

                exposureTime += ExposureTimePrecision;
                Logger.Debug("Debug iteration: exposureTime: " + exposureTime + ", tempsignal: " + tempsignal+ ", " +
                    "skyglownoise: " + skyglownoise + ", darkcurrent: " + darkcurrent + ", photonshot: " + photonshotnoise + ", total: " + totalnoise+".");
                // This "Debug iteration" fills up the logs if the target is faint. It should really be removed later, but is the most useful thing for when checking if it's working properly in the first ~1 week of live testing.

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
    }


    // Equipment classes (Camera, Telescope, Barlow, Filter). Can be removed with the addition of a selection GUI.

    public class Camera
    {
        private string cameraName;
        private double pixelSize;
        private double readNoise;
        private double darkCurrent;
        private double[] arrayQE;
        public string CameraName { get => cameraName; set { cameraName = value; } }
        public double PixelSize { get => pixelSize; set { pixelSize = value; } }
        public double ReadNoise { get => readNoise; set { readNoise = value; } }
        public double DarkCurrent { get => darkCurrent; set { darkCurrent = value; } }
        public double[] ArrayQE { get => arrayQE; set { arrayQE = value; } }

        // Some defaults
        public static Camera qhy600mPro = new Camera("QHY600M-Pro", 3.59, 1, 0.0005, new double[] { 0.51, 0.51, 0.78, 0.87, 0.89, 0.84, 0.74, 0.61, 0.54, 0.1, 0.1, 0.1, 0.1, 0.1 });
        public static Camera qhy268mPro = new Camera("QHY268M-Pro", 3.59, 1, 0.0005, new double[] { 0.51, 0.51, 0.78, 0.87, 0.89, 0.84, 0.74, 0.61, 0.54, 0.1, 0.1, 0.1, 0.1, 0.1 });

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
    public class Filter
    {
        private string filterName;
        private double[] arrayTransmission;
        public string FilterName { get => filterName; set { filterName = value; } }
        public double[] ArrayTransmission { get => arrayTransmission; set { arrayTransmission = value; } }

        // Some defaults
        public static Filter U = new Filter("Sloan U", new double[] { 0.85, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 });
        public static Filter G = new Filter("Sloan G", new double[] { 0.9, 1, 1, 1, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0 });
        public static Filter R = new Filter("Sloan R", new double[] { 0, 0, 0, 0, 0.5, 1, 1, 1, 0, 0, 0, 0, 0, 0 });
        public static Filter Z = new Filter("Sloan Z", new double[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0.9, 1, 1.1, 0, 0 });
        public static Filter I = new Filter("Sloan I", new double[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 1 });
        public static Filter None = new Filter("No filter", new double[] { 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1 });

        // Constructor for Filter
        [ImportingConstructor]
        public Filter(string filterName, double[] transmissionArray)
        {
            this.filterName = filterName;
            this.arrayTransmission = transmissionArray;
        }
    }
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

        // Some defaults
        public static Telescope pw1000 = new Telescope("PW1000", 1000, 470, 6000);
        public static Telescope cdk700 = new Telescope("CDK700", 700, 329, 4540);
        public static Telescope cdk24 = new Telescope("CDK24", 610, 286.7, 3974);
        public static Telescope cdk17 = new Telescope("CDK17", 432, 209.952, 2939);
        public static Telescope cdk14 = new Telescope("CDK14", 356, 172.66, 2563);
        public static Telescope edgehd14 = new Telescope("EdgeHD-14", 356, 114, 3910);
        public static Telescope gsoRc10 = new Telescope("GSO-RC10", 254, 119.38, 2039);

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
    public class Barlow
    {
        private string barlowname; // barlow Name
        private double barlowfactor; // barlow factor
        [JsonProperty]
        public string BarlowName { get => barlowname; set { barlowname = value; } }
        [JsonProperty]
        public double BarlowFactor { get => barlowfactor; set { barlowfactor = value; } }

        // Some defaults
        public static Barlow oneonehalfx = new Barlow("1.5x Barlow", 1.5);
        public static Barlow twox = new Barlow("2x Barlow", 2);
        public static Barlow twoonehalfx = new Barlow("2.5x Barlow", 2.5);
        public static Barlow threex = new Barlow("3x Barlow", 3);
        public static Barlow threeonehalfx = new Barlow("3.5x Barlow", 3.5);

        // Constructor for Telescope
        [ImportingConstructor]
        public Barlow(string barlowName, double BarlowFactor)
        {
            this.barlowname = barlowName;
            this.barlowfactor = BarlowFactor;
        }
    }
}