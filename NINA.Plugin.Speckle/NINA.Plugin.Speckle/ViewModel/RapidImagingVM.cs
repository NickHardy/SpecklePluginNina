#region "copyright"

/*
    Copyright © 2016 - 2021 Stefan Berg <isbeorn86+NINA@googlemail.com> and the N.I.N.A. contributors

    This file is part of N.I.N.A. - Nighttime Imaging 'N' Astronomy.

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Equipment.Equipment.MyCamera;
using NINA.Profile.Interfaces;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using NINA.Equipment.Equipment.MyTelescope;
using NINA.Equipment.Equipment.MyFilterWheel;
using NINA.Equipment.Equipment.MyFocuser;
using NINA.Equipment.Equipment.MyRotator;
using NINA.Equipment.Equipment.MyWeatherData;
using Dasync.Collections;
using NINA.Equipment.Utility;
using NINA.Equipment.Interfaces.Mediator;
using NINA.WPF.Base.Interfaces.Mediator;
using NINA.Core.Model;
using NINA.Image.Interfaces;
using NINA.Image.ImageData;
using NINA.Equipment.Model;
using NINA.Core.Locale;
using NINA.Core.Utility;
using NINA.Equipment.Exceptions;
using NINA.Core.Utility.Notification;
using NINA.Equipment.Interfaces.ViewModel;
using NINA.Equipment.Equipment;
using NINA.WPF.Base.ViewModel;
using NINA.WPF.Base.Interfaces.ViewModel;

namespace Speckle.Photometry.ViewModel {

    internal class RapidImagingVM : BaseVM, IImagingVM {

        // This was a test. It's not used and doesn't work.
        //Instantiate a Singleton of the Semaphore with a value of 1. This means that only 1 thread can be granted access at a time.
        private static SemaphoreSlim semaphoreSlim = new SemaphoreSlim(1, 1);

        private IImageControlVM _imageControl;

        private Task<IRenderedImage> _imageProcessingTask;

        private ApplicationStatus _status;

        private IApplicationStatusMediator applicationStatusMediator;

        private CameraInfo cameraInfo;

        private ICameraMediator cameraMediator;

        private IProgress<ApplicationStatus> progress;

        private static int _exposuerId = 0;

        public RapidImagingVM(IProfileService profileService,
                ICameraMediator cameraMediator,
                IApplicationStatusMediator applicationStatusMediator,
                IImageControlVM imageControlVM
        ) : base(profileService) {
            this.cameraMediator = cameraMediator;
            this.cameraMediator.RegisterConsumer(this);

            this.applicationStatusMediator = applicationStatusMediator;

            progress = new Progress<ApplicationStatus>(p => Status = p);

            ImageControl = imageControlVM;
        }

        public CameraInfo CameraInfo {
            get {
                return cameraInfo ?? DeviceInfo.CreateDefaultInstance<CameraInfo>();
            }
            set {
                cameraInfo = value;
                RaisePropertyChanged();
            }
        }

        public IImageControlVM ImageControl {
            get { return _imageControl; }
            set { _imageControl = value; RaisePropertyChanged(); }
        }

        public ApplicationStatus Status {
            get {
                return _status;
            }
            set {
                _status = value;
                _status.Source = Loc.Instance["LblImaging"]; ;
                RaisePropertyChanged();

                applicationStatusMediator.StatusUpdate(_status);
            }
        }

        private void AddMetaData(
                ImageMetaData metaData,
                CaptureSequence sequence,
                DateTime start,
                string targetName) {
            metaData.Image.Id = this.ExposureId;
            metaData.Image.ExposureStart = start;
            metaData.Image.Binning = sequence.Binning.Name;
            metaData.Image.ExposureNumber = sequence.ProgressExposureCount;
            metaData.Image.ExposureTime = sequence.ExposureTime;
            metaData.Image.ImageType = sequence.ImageType;
            metaData.Target.Name = targetName;
        }

        private Task<IExposureData> CaptureImage(
                CaptureSequence sequence,
                PrepareImageParameters parameters,
                CancellationToken token,
                string targetName = "",
                bool skipProcessing = false
                ) {
            return Task.Run(async () => {
                try {
                    IExposureData data = null;
                    //Asynchronously wait to enter the Semaphore. If no-one has been granted access to the Semaphore, code execution will proceed, otherwise this thread waits here until the semaphore is released
                    await semaphoreSlim.WaitAsync(token);

                    try {
                        if (CameraInfo.Connected != true) {
                            Notification.ShowWarning(Loc.Instance["LblNoCameraConnected"]);
                            throw new CameraConnectionLostException();
                        }

                        /*Capture*/
                        var exposureStart = DateTime.Now;
                        await cameraMediator.Capture(sequence, token, progress);

                        /*Download Image */
                        data = await Download(token, progress);

                        token.ThrowIfCancellationRequested();

                        if (data == null) {
                            Logger.Error(new CameraDownloadFailedException(sequence));
                            Notification.ShowError(string.Format(Loc.Instance["LblCameraDownloadFailed"], sequence.ExposureTime, sequence.ImageType, sequence.Gain, sequence.FilterType?.Name ?? string.Empty));
                            return null;
                        }

                        AddMetaData(data.MetaData, sequence, exposureStart, targetName);
                    } catch (OperationCanceledException) {
                        cameraMediator.AbortExposure();
                        throw;
                    } catch (CameraExposureFailedException ex) {
                        Logger.Error(ex.Message);
                        Notification.ShowError(ex.Message);
                        throw;
                    } catch (CameraConnectionLostException ex) {
                        Logger.Error(ex);
                        Notification.ShowError(Loc.Instance["LblCameraConnectionLost"]);
                        throw;
                    } catch (Exception ex) {
                        Notification.ShowError(Loc.Instance["LblUnexpectedError"] + Environment.NewLine + ex.Message);
                        Logger.Error(ex);
                        cameraMediator.AbortExposure();
                        throw;
                    } finally {
                        progress.Report(new ApplicationStatus() { Status = "" });
                        semaphoreSlim.Release();
                    }
                    return data;
                } finally {
                    // nothing
                }
            });
        }

        private Task<IExposureData> Download(CancellationToken token, IProgress<ApplicationStatus> progress) {
            return cameraMediator.Download(token);
        }

        public async Task<IRenderedImage> CaptureAndPrepareImage(
            CaptureSequence sequence,
            PrepareImageParameters parameters,
            CancellationToken token,
            IProgress<ApplicationStatus> progress) {
            var iarr = await CaptureImage(sequence, parameters, token, string.Empty);
            if (iarr != null) {
                return await _imageProcessingTask;
            } else {
                return null;
            }
        }

        public Task<IExposureData> CaptureImage(CaptureSequence sequence, CancellationToken token, IProgress<ApplicationStatus> progress, string targetName = "") {
            return CaptureImage(sequence, new PrepareImageParameters(), token, targetName, true);
        }

        public void DestroyImage() {
            ImageControl.Image = null;
            ImageControl.RenderedImage = null;
        }

        public void Dispose() {
            this.cameraMediator.RemoveConsumer(this);
        }

        public Task<IRenderedImage> PrepareImage(
            IExposureData data,
            PrepareImageParameters parameters,
            CancellationToken cancelToken) {
            _imageProcessingTask = Task.Run(async () => {
                var imageData = await data.ToImageData(progress, cancelToken);
                var processedData = await ImageControl.PrepareImage(imageData, parameters, cancelToken);
                return processedData;
            }, cancelToken);
            return _imageProcessingTask;
        }

        public Task<IRenderedImage> PrepareImage(
            IImageData data,
            PrepareImageParameters parameters,
            CancellationToken cancelToken) {
            _imageProcessingTask = Task.Run(async () => {
                try {
                    var processedData = await ImageControl.PrepareImage(data, parameters, cancelToken);
                    return processedData;
                } catch (OperationCanceledException) {
                    throw;
                } catch (Exception e) {
                    Logger.Error(e, "Failed to prepare image");
                    Notification.ShowError($"Failed to prepare image for display: {e.Message}");
                    throw;
                }
            }, cancelToken);
            return _imageProcessingTask;
        }

        public void SetImage(BitmapSource img) {
            ImageControl.Image = img;
        }

        public async Task<bool> StartLiveView(CancellationToken ct) {
            //todo: see if this is necessary
            //ImageControl.IsLiveViewEnabled = true;
            try {
                var liveViewEnumerable = cameraMediator.LiveView(ct);
                await liveViewEnumerable.ForEachAsync(async exposureData => {
                    var imageData = await exposureData.ToImageData(progress, ct);
                    await ImageControl.PrepareImage(imageData, new PrepareImageParameters(), ct);
                });
            } catch (OperationCanceledException) {
            } finally {
                //ImageControl.IsLiveViewEnabled = false;
            }

            return true;
        }

        public void UpdateDeviceInfo(CameraInfo cameraStatus) {
            CameraInfo = cameraStatus;
        }

        public void UpdateEndAutoFocusRun(AutoFocusInfo info) {
            ;
        }

        public void UpdateUserFocused(FocuserInfo info) {
            ;
        }

        public void UpdateDeviceInfo(TelescopeInfo deviceInfo) {
            ;
        }

        public void UpdateDeviceInfo(FilterWheelInfo deviceInfo) {
            ;
        }

        public void UpdateDeviceInfo(FocuserInfo deviceInfo) {
            ;
        }

        public void UpdateDeviceInfo(RotatorInfo deviceInfo) {
            ;
        }

        public void UpdateDeviceInfo(WeatherDataInfo deviceInfo) {
            ;
        }

        private int ExposureId { get { return Interlocked.Increment(ref _exposuerId); } }
    }
}