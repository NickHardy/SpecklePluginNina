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
using NINA.Core.Locale;
using NINA.Core.Model;
using NINA.Core.Model.Equipment;
using NINA.Core.Utility;
using NINA.Core.Utility.Notification;
using NINA.Equipment.Equipment.MyCamera;
using NINA.Equipment.Equipment.MyTelescope;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Equipment.Model;
using NINA.Plugin.Speckle.Sequencer.Utility;
using NINA.Plugin.Speckle.Services;
using NINA.Profile.Interfaces;
using NINA.Sequencer.Interfaces;
using NINA.Sequencer.SequenceItem;
using NINA.Sequencer.Validations;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel.Composition;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Plugin.Speckle.Sequencer.SequenceItem {

    [ExportMetadata("Name", "Take Roi Exposures")]
    [ExportMetadata("Description", "Lbl_SequenceItem_Imaging_TakeExposure_Description")]
    [ExportMetadata("Icon", "CameraSVG")]
    [ExportMetadata("Category", "Speckle Interferometry")]
    [Export(typeof(ISequenceItem))]
    [JsonObject(MemberSerialization.OptIn)]
    public class TakeRoiExposures : NINA.Sequencer.SequenceItem.SequenceItem, IExposureItem, IValidatable {
        private ICameraMediator cameraMediator;
        private IProfileService profileService;
        private ITelescopeMediator telescopeMediator;
        private ISpeckleOptionsProvider optionsProvider;
        private ISpeckleAcquisitionService acquisitionService;
        private Speckle speckle;

        [ImportingConstructor]
        public TakeRoiExposures(IProfileService profileService, ICameraMediator cameraMediator, ITelescopeMediator telescopeMediator, ISpeckleOptionsProvider optionsProvider, ISpeckleAcquisitionService acquisitionService) {
            Gain = -1;
            Offset = -1;
            ExposureTimeMultiplier = 1;
            AutoUpdate = true;
            ImageType = CaptureSequence.ImageTypes.LIGHT;
            this.cameraMediator = cameraMediator;
            this.profileService = profileService;
            this.telescopeMediator = telescopeMediator;
            this.optionsProvider = optionsProvider;
            this.acquisitionService = acquisitionService;
            CameraInfo = this.cameraMediator.GetInfo();
            speckle = optionsProvider.Current;
        }

        private TakeRoiExposures(TakeRoiExposures cloneMe) : this(cloneMe.profileService, cloneMe.cameraMediator, cloneMe.telescopeMediator, cloneMe.optionsProvider, cloneMe.acquisitionService) {
            CopyMetaData(cloneMe);
        }

        public override object Clone() {
            var clone = new TakeRoiExposures(this) {
                ExposureTime = ExposureTime,
                ExposureCount = 0,
                TotalExposureCount = TotalExposureCount,
                Binning = Binning,
                Gain = Gain,
                Offset = Offset,
                ImageType = ImageType,
                ExposureTimeMultiplier = ExposureTimeMultiplier,
                AutoUpdate = AutoUpdate,
            };

            if (clone.Binning == null) {
                clone.Binning = new BinningMode((short)1, (short)1);
            }

            return clone;
        }

        private IList<string> issues = new List<string>();

        public IList<string> Issues {
            get => issues;
            set {
                issues = value;
                RaisePropertyChanged();
            }
        }

        private double exposureTime;

        [JsonProperty]
        public double ExposureTime {
            get => exposureTime;
            set {
                exposureTime = value;
                RaisePropertyChanged();
            }
        }

        private double exposureTimeMultiplier;

        [JsonProperty]
        public double ExposureTimeMultiplier {
            get => exposureTimeMultiplier;
            set {
                exposureTimeMultiplier = value;
                RaisePropertyChanged();
            }
        }

        private bool autoUpdate;

        [JsonProperty]
        public bool AutoUpdate { get => autoUpdate; set { autoUpdate = value; RaisePropertyChanged(); } }

        private int gain;

        [JsonProperty]
        public int Gain { get => gain; set { gain = value; RaisePropertyChanged(); } }

        private int offset;

        [JsonProperty]
        public int Offset { get => offset; set { offset = value; RaisePropertyChanged(); } }

        private BinningMode binning;

        [JsonProperty]
        public BinningMode Binning { get => binning; set { binning = value; RaisePropertyChanged(); } }

        private string imageType;

        [JsonProperty]
        public string ImageType { get => imageType; set { imageType = value; RaisePropertyChanged(); } }

        private int exposureCount;

        [JsonProperty]
        public int ExposureCount { get => exposureCount; set { exposureCount = value; RaisePropertyChanged(); } }

        private int totalExposureCount;

        [JsonProperty]
        public int TotalExposureCount { get => totalExposureCount; set { totalExposureCount = value; RaisePropertyChanged(); } }

        private CameraInfo cameraInfo;

        public CameraInfo CameraInfo {
            get => cameraInfo;
            private set {
                cameraInfo = value;
                RaisePropertyChanged();
            }
        }

        private TelescopeInfo telescopeInfo;

        public TelescopeInfo TelescopeInfo {
            get => telescopeInfo;
            private set {
                telescopeInfo = value;
                RaisePropertyChanged();
            }
        }

        private ObservableCollection<string> _imageTypes;

        public ObservableCollection<string> ImageTypes {
            get {
                if (_imageTypes == null) {
                    _imageTypes = new ObservableCollection<string>();

                    Type type = typeof(CaptureSequence.ImageTypes);
                    foreach (var field in type.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)) {
                        var imageType = field.GetValue(null);
                        _imageTypes.Add(imageType.ToString());
                    }
                }
                return _imageTypes;
            }
            set {
                _imageTypes = value;
                RaisePropertyChanged();
            }
        }

        public override async Task Execute(IProgress<ApplicationStatus> progress, CancellationToken token) {
            var targetContainer = ItemUtility.RetrieveSpeckleContainer(Parent);
            ExposureCount = 1;
            var speckleTarget = ItemUtility.RetrieveSpeckleTarget(Parent);
            var genericHeaders = SpeckleMetadataBuilder.ResolveGenericHeaders(speckleTarget, targetContainer.IsRef, speckle);
            TelescopeInfo = this.telescopeMediator.GetInfo();

            var request = new RoiSeriesRequest {
                Mode = RoiSeriesMode.Sequenced,
                ExposureTime = ExposureTime * ExposureTimeMultiplier,
                TotalExposureCount = TotalExposureCount,
                Gain = Gain,
                Offset = Offset,
                Binning = Binning,
                ImageType = ImageType,
                EnableSubSample = targetContainer.EnableSubSample,
                SubSampleRectangle = ItemUtility.RetrieveSpeckleTargetRoi(Parent),
                Target = targetContainer.Target,
                Title = targetContainer.Title,
                SpeckleRun = targetContainer.SpeckleRun,
                GenericHeaders = genericHeaders,
                OnFrame = count => ExposureCount = count
            };

            try {
                var result = await acquisitionService.RunRoiSeriesAsync(request, progress, token);
                ExposureCount = result.FramesCaptured;
                await result.PendingSaves;
            } catch (OperationCanceledException) {
                throw;
            } catch (Exception ex) {
                Notification.ShowError(Loc.Instance["LblUnexpectedError"] + Environment.NewLine + ex.Message);
                Logger.Error(ex);
                throw;
            } finally {
                progress.Report(new ApplicationStatus() { Status = "" });
            }
        }

        public override void AfterParentChanged() {
            Validate();
        }

        public bool Validate() {
            var issues = new List<string>();
            CameraInfo = this.cameraMediator.GetInfo();
            if (!CameraInfo.Connected) {
                issues.Add(Loc.Instance["LblCameraNotConnected"]);
            } else {
                if (CameraInfo.CanSetGain && Gain > -1 && (Gain < CameraInfo.GainMin || Gain > CameraInfo.GainMax)) {
                    issues.Add(string.Format(Loc.Instance["Lbl_SequenceItem_Imaging_TakeExposure_Validation_Gain"], CameraInfo.GainMin, CameraInfo.GainMax, Gain));
                }
                if (CameraInfo.CanSetOffset && Offset > -1 && (Offset < CameraInfo.OffsetMin || Offset > CameraInfo.OffsetMax)) {
                    issues.Add(string.Format(Loc.Instance["Lbl_SequenceItem_Imaging_TakeExposure_Validation_Offset"], CameraInfo.OffsetMin, CameraInfo.OffsetMax, Offset));
                }
            }

            if (ItemUtility.RetrieveSpeckleContainer(Parent) == null && ItemUtility.RetrieveSpeckleListContainer(Parent) == null) {
                issues.Add("This instruction only works within a SpeckleTargetContainer.");
            }

            var fileSettings = profileService.ActiveProfile.ImageFileSettings;

            if (string.IsNullOrWhiteSpace(fileSettings.FilePath)) {
                issues.Add(Loc.Instance["Lbl_SequenceItem_Imaging_TakeExposure_Validation_FilePathEmpty"]);
            } else if (!Directory.Exists(fileSettings.FilePath)) {
                issues.Add(Loc.Instance["Lbl_SequenceItem_Imaging_TakeExposure_Validation_FilePathInvalid"]);
            }

            Issues = issues;
            return issues.Count == 0;
        }

        public override TimeSpan GetEstimatedDuration() {
            return TimeSpan.FromSeconds(this.ExposureTime * ExposureTimeMultiplier * this.totalExposureCount);
        }

        public override string ToString() {
            return $"Category: {Category}, Item: {nameof(TakeRoiExposures)}, ExposureTime {ExposureTime * ExposureTimeMultiplier}, Gain {Gain}, Offset {Offset}, ImageType {ImageType}, Binning {Binning?.Name}";
        }

    }
}
