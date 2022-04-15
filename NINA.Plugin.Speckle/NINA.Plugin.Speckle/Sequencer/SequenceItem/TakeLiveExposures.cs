#region "copyright"

/*
    Copyright © 2016 - 2022 Stefan Berg <isbeorn86+NINA@googlemail.com> and the N.I.N.A. contributors

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
using NINA.ViewModel.Interfaces;
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
using Dasync.Collections;
using System.Diagnostics;
using NINA.Image.FileFormat;
using NINA.Sequencer.SequenceItem;
using NINA.Plugin.Speckle.Sequencer.Utility;

namespace NINA.Plugin.Speckle.Sequencer.SequenceItem {

    [ExportMetadata("Name", "QHY live exposures")]
    [ExportMetadata("Description", "Lbl_SequenceItem_Imaging_TakeExposure_Description")]
    [ExportMetadata("Icon", "CameraSVG")]
    [ExportMetadata("Category", "Speckle Interferometry")]
    [Export(typeof(ISequenceItem))]
    [JsonObject(MemberSerialization.OptIn)]
    public class TakeLiveExposures : NINA.Sequencer.SequenceItem.SequenceItem, IExposureItem, IValidatable {
        private ICameraMediator cameraMediator;
        private IImagingMediator imagingMediator;
        private IImageSaveMediator imageSaveMediator;
        private IImageHistoryVM imageHistoryVM;
        private IProfileService profileService;
        private IFilterWheelMediator filterWheelMediator;
        private Speckle speckle;

        [ImportingConstructor]
        public TakeLiveExposures(IProfileService profileService, ICameraMediator cameraMediator, IImagingMediator imagingMediator, IImageSaveMediator imageSaveMediator, IImageHistoryVM imageHistoryVM, IFilterWheelMediator filterWheelMediator) {
            Gain = -1;
            Offset = -1;
            ImageType = CaptureSequence.ImageTypes.LIGHT;
            this.cameraMediator = cameraMediator;
            this.imagingMediator = imagingMediator;
            this.imageSaveMediator = imageSaveMediator;
            this.imageHistoryVM = imageHistoryVM;
            this.profileService = profileService;
            this.filterWheelMediator = filterWheelMediator;
            CameraInfo = this.cameraMediator.GetInfo();
            speckle = new Speckle();
        }

        private TakeLiveExposures(TakeLiveExposures cloneMe) : this(cloneMe.profileService, cloneMe.cameraMediator, cloneMe.imagingMediator, cloneMe.imageSaveMediator, cloneMe.imageHistoryVM, cloneMe.filterWheelMediator) {
            CopyMetaData(cloneMe);
        }

        public override object Clone() {
            var clone = new TakeLiveExposures(this) {
                ExposureTime = ExposureTime,
                ExposureCount = 0,
                Binning = Binning,
                Gain = Gain,
                Offset = Offset,
                ImageType = ImageType,
                TotalExposureCount = TotalExposureCount,
                WaitForCameraReconnect = WaitForCameraReconnect
            };

            if (clone.Binning == null) {
                clone.Binning = new BinningMode(1, 1);
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

        private bool waitForCameraReconnect;

        [JsonProperty]
        public bool WaitForCameraReconnect { get => waitForCameraReconnect; set { waitForCameraReconnect = value; RaisePropertyChanged(); } }

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

        public override async Task Execute(IProgress<ApplicationStatus> progress, CancellationToken token) {
            ExposureCount = 1;
            var capture = new CaptureSequence() {
                ExposureTime = ExposureTime,
                Binning = Binning,
                Gain = Gain,
                Offset = Offset,
                ImageType = ImageType,
                ProgressExposureCount = ExposureCount,
                TotalExposureCount = TotalExposureCount,
                EnableSubSample = true,
                SubSambleRectangle = ItemUtility.RetrieveSpeckleTargetRoi(Parent),
            };

            var imageParams = new PrepareImageParameters(null, false);
            if (IsLightSequence()) {
                imageParams = new PrepareImageParameters(true, false);
            }

            var target = RetrieveTarget(Parent);
            var title = ItemUtility.RetrieveSpeckleTitle(Parent);

            var localCTS = CancellationTokenSource.CreateLinkedTokenSource(token);

            var liveViewEnumerable = cameraMediator.LiveView(capture, localCTS.Token);
            Stopwatch seqDuration = Stopwatch.StartNew();
            await liveViewEnumerable.ForEachAsync(async exposureData => {
                if (ExposureCount == 1) { seqDuration = Stopwatch.StartNew(); }
                var imageData = await exposureData.ToImageData(progress, localCTS.Token);

                if (target != null) {
                    imageData.MetaData.Target.Name = target.DeepSkyObject.NameAsAscii;
                    imageData.MetaData.Target.Coordinates = target.InputCoordinates.Coordinates;
                    imageData.MetaData.Target.Rotation = target.Rotation;
                }

                if (filterWheelMediator.GetInfo().Connected)
                    imageData.MetaData.FilterWheel.Filter = filterWheelMediator.GetInfo().SelectedFilter.Name;

                imageData.MetaData.Sequence.Title = title;
                imageData.MetaData.Image.ExposureStart = DateTime.Now;
                imageData.MetaData.Image.ExposureNumber = ExposureCount;
                imageData.MetaData.Image.ExposureTime = ExposureTime;

                // Only show first and last image in Imaging window
                if (ExposureCount == 1 || ExposureCount % speckle.ShowEveryNthImage == 0 || ExposureCount == TotalExposureCount) {
                    _ = imagingMediator.PrepareImage(imageData, imageParams, token);
                }

                _ = imageData.SaveToDisk(new FileSaveInfo(profileService), token);

                if (ExposureCount >= TotalExposureCount) {
                    double fps = ExposureCount / (((double)seqDuration.ElapsedMilliseconds) / 1000);
                    Logger.Info("Captured " + ExposureCount + " times " + ExposureTime + "s live images in " + seqDuration.ElapsedMilliseconds + " ms. : " + Math.Round(fps, 2) + " fps");
                    localCTS.Cancel();
                } else { ExposureCount++; }
            });

            // wait till camera reconnects
            if (WaitForCameraReconnect) {
                Thread.Sleep(100);
                while (!cameraInfo.Connected) {
                    token.ThrowIfCancellationRequested();
                    Thread.Sleep(100);
                }
            }
        }

        private bool IsLightSequence() {
            return ImageType == CaptureSequence.ImageTypes.SNAPSHOT || ImageType == CaptureSequence.ImageTypes.LIGHT;
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
            return TimeSpan.FromSeconds(this.ExposureTime);
        }

        public override string ToString() {
            return $"Category: {Category}, Item: {nameof(TakeLiveExposures)}, ExposureTime {ExposureTime}, Gain {Gain}, Offset {Offset}, ImageType {ImageType}, Binning {Binning?.Name}";
        }
    }
}