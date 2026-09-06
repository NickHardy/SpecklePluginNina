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
using NINA.Core.Model.Equipment;
using NINA.Core.Utility;
using NINA.Plugin.Speckle.Model;
using NINA.Plugin.Speckle.Sequencer.Utility;
using NINA.Plugin.Speckle.Services;
using NINA.Sequencer.SequenceItem;
using NINA.Sequencer.Validations;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

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
        private double exposureTime;
        private BinningMode binning;
        private double exposureCount;
        private double intendedSNR;

        private ISpeckleOptionsProvider optionsProvider;
        private IExposureCalibrationService exposureCalibrationService;
        private Speckle speckle;

        [ImportingConstructor]
        public CalculateExposure(ISpeckleOptionsProvider optionsProvider, IExposureCalibrationService exposureCalibrationService)
        {
            this.optionsProvider = optionsProvider;
            this.exposureCalibrationService = exposureCalibrationService;
            speckle = optionsProvider.Current;
        }

        private CalculateExposure(CalculateExposure cloneMe) : this(cloneMe.optionsProvider, cloneMe.exposureCalibrationService)
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

        public double ExposureTime { get => exposureTime; set { exposureTime = value; RaisePropertyChanged(); } }
        public BinningMode Binning { get => binning; set { binning = value; RaisePropertyChanged(); } }
        public double ExposureCount { get => exposureCount; set { exposureCount = value; RaisePropertyChanged(); } }
        public double IntendedSNR { get => intendedSNR; set { intendedSNR = value; RaisePropertyChanged(); } }

        public override async Task Execute(IProgress<ApplicationStatus> progress, CancellationToken token)
        {
            var speckleTarget = ItemUtility.RetrieveSpeckleTarget(Parent);
            if (speckleTarget.NoEC != 0)
                return;

            Telescope telescope = speckle.Telescope;
            Camera camera = Camera.qhy600mPro;
            Barlow barlow = speckle.Barlow;

            try {
                var request = new ExposureEstimateRequest {
                    Target = speckleTarget,
                    IsRef = ItemUtility.RetrieveSpeckleContainer(Parent).IsRef,
                    Telescope = telescope,
                    Camera = camera,
                    Barlow = barlow,
                    IntendedSNR = IntendedSNR
                };
                ExposureTime = exposureCalibrationService.EstimateExposureTime(request);
                if (ExposureTime == 0) return;
                progress.Report(new ApplicationStatus() { Status = "Calculated exposure time: " + ExposureTime });
                ItemUtility.RetrieveSpeckleContainer(Parent).Items.ToList().ForEach(item => {
                    if (item is TakeRoiExposures takeRoiExposures)
                    {
                        Logger.Debug("Setting exposure time of " + ExposureTime + "..");
                        takeRoiExposures.ExposureTime = ExposureTime;
                        Logger.Debug("takeRoiExposures.ExposureTime is now " + takeRoiExposures.ExposureTime);
                    }
                    else if (item is TakeLiveExposures takeLiveExposures)
                    {
                        Logger.Debug("Setting exposure time of " + ExposureTime + "..");
                        takeLiveExposures.ExposureTime = ExposureTime;
                        Logger.Debug("takeLiveExposures.ExposureTime is now " + takeLiveExposures.ExposureTime);
                    }
                });
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
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

        public bool Validate()
        {
            var issues = new List<string>();
            if (ItemUtility.RetrieveSpeckleContainer(Parent) == null && ItemUtility.RetrieveSpeckleListContainer(Parent) == null)
            {
                issues.Add("This instruction only works within a SpeckleTargetContainer.");
            }
            return issues.Count == 0;
        }
    }

}
