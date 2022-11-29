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
using NINA.Image.ImageData;
using NINA.Equipment.Equipment.MyTelescope;

namespace NINA.Plugin.Speckle.Sequencer.SequenceItem {

    [ExportMetadata("Name", "Take Roi Exposures")]
    [ExportMetadata("Description", "Lbl_SequenceItem_Imaging_TakeExposure_Description")]
    [ExportMetadata("Icon", "CameraSVG")]
    [ExportMetadata("Category", "Speckle Interferometry")]
    [Export(typeof(ISequenceItem))]
    [JsonObject(MemberSerialization.OptIn)]
    public class TakeRoiExposures : NINA.Sequencer.SequenceItem.SequenceItem, IExposureItem, IValidatable {
        private ICameraMediator cameraMediator;
        private IImagingMediator imagingMediator;
        private IImageSaveMediator imageSaveMediator;
        private IProfileService profileService;
        private IApplicationStatusMediator applicationStatusMediator;
        private IImageControlVM imageControlVM;
        private IFilterWheelMediator filterWheelMediator;
        private ITelescopeMediator telescopeMediator;
        private Speckle speckle;

        [ImportingConstructor]
        public TakeRoiExposures(IProfileService profileService, ICameraMediator cameraMediator, IImagingMediator imagingMediator, IImageSaveMediator imageSaveMediator, IApplicationStatusMediator applicationStatusMediator, IImageControlVM imageControlVM, IFilterWheelMediator filterWheelMediator, ITelescopeMediator telescopeMediator) {
            Gain = -1;
            Offset = -1;
            ExposureTimeMultiplier = 1;
            ImageType = CaptureSequence.ImageTypes.LIGHT;
            this.cameraMediator = cameraMediator;
            this.imagingMediator = imagingMediator;
            this.imageSaveMediator = imageSaveMediator;
            this.applicationStatusMediator = applicationStatusMediator;
            this.profileService = profileService;
            this.imageControlVM = imageControlVM;
            this.filterWheelMediator = filterWheelMediator;
            this.telescopeMediator = telescopeMediator;
            CameraInfo = this.cameraMediator.GetInfo();
            speckle = new Speckle();
        }

        private TakeRoiExposures(TakeRoiExposures cloneMe) : this(cloneMe.profileService, cloneMe.cameraMediator, cloneMe.imagingMediator, cloneMe.imageSaveMediator, cloneMe.applicationStatusMediator, cloneMe.imageControlVM, cloneMe.filterWheelMediator, cloneMe.telescopeMediator) {
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
                ExposureTime = ExposureTime * ExposureTimeMultiplier,
                Binning = Binning,
                Gain = Gain,
                Offset = Offset,
                ImageType = ImageType,
                ProgressExposureCount = ExposureCount,
                TotalExposureCount = TotalExposureCount,
                EnableSubSample = true,
                SubSambleRectangle = ItemUtility.RetrieveSpeckleTargetRoi(Parent)
            };

            var imageParams = new PrepareImageParameters(true, false);

            var target = RetrieveTarget(Parent);
            var title = ItemUtility.RetrieveSpeckleTitle(Parent);
            TelescopeInfo = this.telescopeMediator.GetInfo();

            try {
                Stopwatch seqDuration = Stopwatch.StartNew();
                while (ExposureCount <= TotalExposureCount) {
                    Stopwatch roiDuration = Stopwatch.StartNew();
                    var exposureStart = DateTime.Now;
                    await cameraMediator.Capture(capture, token, progress);
                    Logger.Debug("Capture: " + roiDuration.ElapsedMilliseconds);
                    progress.Report(new ApplicationStatus() { Status = "Taking Roi image: " + ExposureCount });
                    token.ThrowIfCancellationRequested();
                    IExposureData exposureData = await cameraMediator.Download(token);
                    Logger.Debug("Download: " + roiDuration.ElapsedMilliseconds);
                    token.ThrowIfCancellationRequested();

                    var imageData = await exposureData.ToImageData(progress, token);
                    Logger.Debug("ImageData: " + roiDuration.ElapsedMilliseconds);

                    if (target != null) {
                        imageData.MetaData.Target.Name = target.DeepSkyObject.NameAsAscii;
                        imageData.MetaData.Target.Coordinates = target.InputCoordinates.Coordinates;
                        imageData.MetaData.Target.Rotation = target.Rotation;
                    }

                    // Only show first and last image in Imaging window
                    if (ExposureCount == 1 || ExposureCount % speckle.ShowEveryNthImage == 0 || ExposureCount == TotalExposureCount) {
                        _ = Task.Run(async () => {
                            var renderedImage = await imagingMediator.PrepareImage(imageData, imageParams, token);
                            //imagingMediator.SetImage(ItemUtility.GetDFTImage(renderedImage.Image));
                        });
                    }

                    if (filterWheelMediator.GetInfo().Connected)
                        imageData.MetaData.FilterWheel.Filter = filterWheelMediator.GetInfo().SelectedFilter.Name;

                    imageData.MetaData.Sequence.Title = title;
                    imageData.MetaData.Image.ExposureStart = exposureStart;
                    imageData.MetaData.Image.ExposureNumber = ExposureCount;
                    imageData.MetaData.Image.ExposureTime = ExposureTime * ExposureTimeMultiplier;

                    ItemUtility.FromTelescopeInfo(imageData.MetaData, TelescopeInfo);

                    imageData.MetaData.GenericHeaders.Add(new DoubleMetaDataHeader("ROIX", capture.SubSambleRectangle.X, "X-position of the ROI"));
                    imageData.MetaData.GenericHeaders.Add(new DoubleMetaDataHeader("ROIY", capture.SubSambleRectangle.Y, "Y-position of the ROI"));
                    imageData.MetaData.GenericHeaders.Add(new DoubleMetaDataHeader("XORGSUBF", capture.SubSambleRectangle.X, "X-position of the ROI"));
                    imageData.MetaData.GenericHeaders.Add(new DoubleMetaDataHeader("YORGSUBF", capture.SubSambleRectangle.Y, "Y-position of the ROI"));

                    //_ = imageData.SaveToDisk(new FileSaveInfo(profileService), token);
                    Logger.Debug("Metadata: " + roiDuration.ElapsedMilliseconds);
                    _ = Task.Run(async () => {
                        //var result = imageData.SaveToDisk(new FileSaveInfo(profileService), token);
                        List<ImagePattern> customPatterns = new List<ImagePattern>();
                        customPatterns.Add(new ImagePattern(speckle.notePattern.Key, speckle.notePattern.Description, speckle.notePattern.Category) {
                            Value = string.Empty
                        });
                        FileSaveInfo fileSaveInfo = new FileSaveInfo(profileService);
                        string tempPath = await imageData.PrepareSave(fileSaveInfo);
                        _ = imageData.FinalizeSave(tempPath, fileSaveInfo.FilePattern, customPatterns);

                        Logger.Debug("SaveToDisk: " + roiDuration.ElapsedMilliseconds);
                    });
                    Logger.Debug("Task save: " + roiDuration.ElapsedMilliseconds);

                    capture.ProgressExposureCount = ExposureCount;
                    ExposureCount++;
                }
                ExposureCount--;
                double fps = ExposureCount / (((double)seqDuration.ElapsedMilliseconds) / 1000);
                Logger.Info("Captured " + ExposureCount + " times " + ExposureTime * ExposureTimeMultiplier + "s images in " + seqDuration.ElapsedMilliseconds + " ms. : " + Math.Round(fps, 2) + " fps");
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

            if (ItemUtility.RetrieveSpeckleContainer(Parent) == null && ItemUtility.RetrieveSpeckleListContainer(Parent) == null) {
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
            return TimeSpan.FromSeconds(this.ExposureTime * ExposureTimeMultiplier * this.totalExposureCount);
        }

        public override string ToString() {
            return $"Category: {Category}, Item: {nameof(TakeRoiExposures)}, ExposureTime {ExposureTime * ExposureTimeMultiplier}, Gain {Gain}, Offset {Offset}, ImageType {ImageType}, Binning {Binning?.Name}";
        }

    }
}