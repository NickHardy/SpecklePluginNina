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

namespace NINA.Plugin.Speckle.Sequencer.SequenceItem {

    [ExportMetadata("Name", "Calculate exposure time")]
    [ExportMetadata("Description", "Lbl_SequenceItem_Imaging_TakeExposure_Description")]
    [ExportMetadata("Icon", "CameraSVG")]
    [ExportMetadata("Category", "Speckle Interferometry")]
    [Export(typeof(ISequenceItem))]
    [JsonObject(MemberSerialization.OptIn)]
    public class CalculateRoiExposureTime : NINA.Sequencer.SequenceItem.SequenceItem, IExposureItem, IValidatable {
        private ICameraMediator cameraMediator;
        private IImagingMediator imagingMediator;
        private IImageSaveMediator imageSaveMediator;
        private IProfileService profileService;
        private IApplicationStatusMediator applicationStatusMediator;
        private IImageControlVM imageControlVM;
        private Speckle speckle;
        private Task<IRenderedImage> _imageProcessingTask;

        [ImportingConstructor]
        public CalculateRoiExposureTime(IProfileService profileService, ICameraMediator cameraMediator, IImagingMediator imagingMediator, IImageSaveMediator imageSaveMediator, IApplicationStatusMediator applicationStatusMediator, IImageControlVM imageControlVM) {
            Gain = -1;
            Offset = -1;
            TargetADU = 0.33d;
            ExposureStepTime = 0.01d;
            ImageType = CaptureSequence.ImageTypes.LIGHT;
            this.cameraMediator = cameraMediator;
            this.imagingMediator = imagingMediator;
            this.imageSaveMediator = imageSaveMediator;
            this.applicationStatusMediator = applicationStatusMediator;
            this.profileService = profileService;
            this.imageControlVM = imageControlVM;
            CameraInfo = this.cameraMediator.GetInfo();
            speckle = new Speckle();
        }

        private CalculateRoiExposureTime(CalculateRoiExposureTime cloneMe) : this(cloneMe.profileService, cloneMe.cameraMediator, cloneMe.imagingMediator, cloneMe.imageSaveMediator, cloneMe.applicationStatusMediator, cloneMe.imageControlVM) {
            CopyMetaData(cloneMe);
        }

        public override object Clone() {
            var clone = new CalculateRoiExposureTime(this) {
                ExposureTime = ExposureTime,
                ExposureCount = 0,
                TotalExposureCount = TotalExposureCount,
                Binning = Binning,
                Gain = Gain,
                Offset = Offset,
                ImageType = ImageType,
                TargetADU = TargetADU,
                ExposureTimeMax = ExposureTimeMax,
                ExposureStepTime = ExposureStepTime
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
        public double ExposureTime { get => exposureTime; set { exposureTime = value; RaisePropertyChanged(); } }

        private double exposureTimeMax;

        [JsonProperty]
        public double ExposureTimeMax { get => exposureTimeMax; set { exposureTimeMax = value; RaisePropertyChanged(); } }

        private double exposureStepTime;

        [JsonProperty]
        public double ExposureStepTime { get => exposureStepTime; set { exposureStepTime = value; RaisePropertyChanged(); } }

        public double targetADU;

        [JsonProperty]
        public double TargetADU { get => targetADU; set { targetADU = value; RaisePropertyChanged(); } }

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

        private ObservableCollection<string> _imageTypes;

        public ObservableCollection<string> ImageTypes {
            get {
                if (_imageTypes == null) {
                    _imageTypes = new ObservableCollection<string>();

                    Type type = typeof(CaptureSequence.ImageTypes);
                    foreach (var p in type.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)) {
                        var v = p.GetValue(null);
                        _imageTypes.Add(v.ToString());
                    }
                }
                return _imageTypes;
            }
            set {
                _imageTypes = value;
                RaisePropertyChanged();
            }
        }

        public double CameraMaxAdu { get; private set; }

        public override async Task Execute(IProgress<ApplicationStatus> progress, CancellationToken token) {
            ExposureCount = 1;
            ExposureTime = ExposureTimeMax;
            CameraMaxAdu = HistogramMath.CameraBitDepthToAdu(CameraInfo.BitDepth);
            var capture = new CaptureSequence() {
                ExposureTime = ExposureTime,
                Binning = Binning,
                Gain = Gain,
                Offset = Offset,
                ImageType = ImageType,
                ProgressExposureCount = ExposureCount,
                EnableSubSample = true,
                SubSambleRectangle = ItemUtility.RetrieveSpeckleTargetRoi(Parent)
            };

            var imageParams = new PrepareImageParameters(true, false);
            try {
                while (ExposureTime > 0) {
                    while (ExposureTime > 0) {
                        capture.ExposureTime = ExposureTime;
                        await cameraMediator.Capture(capture, token, progress);
                        progress.Report(new ApplicationStatus() { Status = "Calculating Roi exposureTime: " + ExposureTime });
                        token.ThrowIfCancellationRequested();
                        IExposureData exposureData = await cameraMediator.Download(token);
                        token.ThrowIfCancellationRequested();

                        var imageData = await exposureData.ToImageData(progress, token);
/*                        imageData.MetaData.Sequence.Title = "Calculations";
                        IRenderedImage renderedImage = await imagingMediator.PrepareImage(imageData, imageParams, token);

                        _ = imageData.SaveToDisk(new FileSaveInfo(profileService), token);*/

                        var stats = imageData.Statistics.Task.Result;

                        capture.ProgressExposureCount = ExposureCount;
                        Logger.Trace("ExposureTime " + ExposureTime + " Stats: Max " + stats.Max + " Mean " + stats.Mean + " CameraMaxAdu " + CameraMaxAdu);
                        if (stats.Max < CameraMaxAdu * TargetADU) break;
                        //if (stats.Max > stats.Mean * 3) break;
                        ExposureCount++;
                        ExposureTime = Math.Round(ExposureTime - ExposureStepTime, 3);
                    }
                    List<IImageStatistics> statsArray = new List<IImageStatistics>();
                    for (int i = 1; i < 10; i++) {
                        await cameraMediator.Capture(capture, token, progress);
                        progress.Report(new ApplicationStatus() { Status = "Calculating Roi exposureTime: " + ExposureTime });
                        token.ThrowIfCancellationRequested();
                        IExposureData exposureData = await cameraMediator.Download(token);
                        token.ThrowIfCancellationRequested();

                        var imageData = await exposureData.ToImageData(progress, token);

                        IRenderedImage renderedImage = await imagingMediator.PrepareImage(imageData, imageParams, token);

                        statsArray.Add(imageData.Statistics.Task.Result);
                    }
                    var avgMax = statsArray.Average(x => x.Max);
                    var avgMean = statsArray.Average(x => x.Mean);
                    Logger.Debug("Calulated averages: Max " + avgMax + " Mean " + avgMean);
                    if (avgMax < CameraMaxAdu * TargetADU) break;
                    //if (avgMax > avgMean * 3) break;
                    ExposureCount++;
                    ExposureTime = Math.Round(ExposureTime - ExposureStepTime, 3);
                }
                if (ExposureTime > ExposureTimeMax) ExposureTime = ExposureTimeMax;
                ItemUtility.RetrieveSpeckleContainer(Parent).Items.ToList().ForEach(x => {
                    if (x is TakeRoiExposures takeRoiExposures) {
                        takeRoiExposures.ExposureTime = ExposureTime;
                    }
                    if (x is TakeLiveExposures takeLiveExposures) {
                        takeLiveExposures.ExposureTime = ExposureTime;
                    }
                });

            } catch (OperationCanceledException) {
                cameraMediator.AbortExposure();
                throw;
            } catch (Exception ex) {
                Notification.ShowError(Loc.Instance["LblUnexpectedError"] + Environment.NewLine + ex.Message);
                Logger.Error(ex);
                cameraMediator.AbortExposure();
                throw;
            } finally {
                progress.Report(new ApplicationStatus() { Status = "" });
            }
        }

        public override void AfterParentChanged() {
            Validate();
        }

        private InputTarget RetrieveTarget(ISequenceContainer parent) {
            if (parent != null) {
                var container = parent as IDeepSkyObjectContainer;
                if (container != null) {
                    return container.Target;
                } else {
                    return RetrieveTarget(parent.Parent);
                }
            } else {
                return null;
            }
        }

        public bool Validate() {
            var i = new List<string>();
            CameraInfo = this.cameraMediator.GetInfo();
            if (!CameraInfo.Connected) {
                i.Add(Loc.Instance["LblCameraNotConnected"]);
            } else {
                if (CameraInfo.CanSetGain && Gain > -1 && (Gain < CameraInfo.GainMin || Gain > CameraInfo.GainMax)) {
                    i.Add(string.Format(Loc.Instance["Lbl_SequenceItem_Imaging_TakeExposure_Validation_Gain"], CameraInfo.GainMin, CameraInfo.GainMax, Gain));
                }
                if (CameraInfo.CanSetOffset && Offset > -1 && (Offset < CameraInfo.OffsetMin || Offset > CameraInfo.OffsetMax)) {
                    i.Add(string.Format(Loc.Instance["Lbl_SequenceItem_Imaging_TakeExposure_Validation_Offset"], CameraInfo.OffsetMin, CameraInfo.OffsetMax, Offset));
                }
            }

            if (ItemUtility.RetrieveSpeckleContainer(Parent) == null) {
                i.Add("This instruction only works within a SpeckleTargetContainer.");
            }

            var fileSettings = profileService.ActiveProfile.ImageFileSettings;

            if (string.IsNullOrWhiteSpace(fileSettings.FilePath)) {
                i.Add(Loc.Instance["Lbl_SequenceItem_Imaging_TakeExposure_Validation_FilePathEmpty"]);
            } else if (!Directory.Exists(fileSettings.FilePath)) {
                i.Add(Loc.Instance["Lbl_SequenceItem_Imaging_TakeExposure_Validation_FilePathInvalid"]);
            }

            Issues = i;
            return i.Count == 0;
        }

        public override TimeSpan GetEstimatedDuration() {
            return TimeSpan.FromSeconds(this.ExposureTime * this.totalExposureCount);
        }

        public override string ToString() {
            return $"Category: {Category}, Item: {nameof(CalculateRoiExposureTime)}, ExposureTime {ExposureTime}, Gain {Gain}, Offset {Offset}, ImageType {ImageType}, Binning {Binning?.Name}";
        }

    }
}