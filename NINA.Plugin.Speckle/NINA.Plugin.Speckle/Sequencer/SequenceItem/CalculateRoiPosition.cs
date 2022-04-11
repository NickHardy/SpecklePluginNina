﻿#region "copyright"

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
using NINA.PlateSolving;
using NINA.Profile.Interfaces;
using NINA.Sequencer.Validations;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Core.Utility.WindowService;
using NINA.ViewModel;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NINA.Core.Locale;
using NINA.Equipment.Model;
using NINA.Core.Model.Equipment;
using NINA.WPF.Base.ViewModel;
using NINA.PlateSolving.Interfaces;
using NINA.Sequencer.SequenceItem;
using NINA.Astrometry;
using NINA.Core.Utility;
using NINA.Image.ImageAnalysis;
using NINA.Core.Enum;
using NINA.Image.Interfaces;
using NINA.Core.Utility.Notification;
using static NINA.Astrometry.Coordinates;
using NINA.Plugin.Speckle.Sequencer.Utility;
using System.Windows;

namespace NINA.Plugin.Speckle.Sequencer.SequenceItem {

    [ExportMetadata("Name", "Calculate Roi Position")]
    [ExportMetadata("Description", "Platesolve an image locate the target and center the ROI position on the target.")]
    [ExportMetadata("Icon", "CrosshairSVG")]
    [ExportMetadata("Category", "Speckle Interferometry")]
    [Export(typeof(ISequenceItem))]
    [JsonObject(MemberSerialization.OptIn)]
    public class CalculateRoiPosition : NINA.Sequencer.SequenceItem.SequenceItem, IValidatable {
        private IProfileService profileService;
        private ITelescopeMediator telescopeMediator;
        private IImagingMediator imagingMediator;
        private IFilterWheelMediator filterWheelMediator;
        private IPlateSolverFactory plateSolverFactory;
        private IWindowServiceFactory windowServiceFactory;
        public PlateSolvingStatusVM PlateSolveStatusVM { get; } = new PlateSolvingStatusVM();

        [ImportingConstructor]
        public CalculateRoiPosition(IProfileService profileService,
                            ITelescopeMediator telescopeMediator,
                            IImagingMediator imagingMediator,
                            IFilterWheelMediator filterWheelMediator,
                            IPlateSolverFactory plateSolverFactory,
                            IWindowServiceFactory windowServiceFactory) {
            this.profileService = profileService;
            this.telescopeMediator = telescopeMediator;
            this.imagingMediator = imagingMediator;
            this.filterWheelMediator = filterWheelMediator;
            this.plateSolverFactory = plateSolverFactory;
            this.windowServiceFactory = windowServiceFactory;
        }

        private CalculateRoiPosition(CalculateRoiPosition cloneMe) : this(cloneMe.profileService,
                                                          cloneMe.telescopeMediator,
                                                          cloneMe.imagingMediator,
                                                          cloneMe.filterWheelMediator,
                                                          cloneMe.plateSolverFactory,
                                                          cloneMe.windowServiceFactory) {
            CopyMetaData(cloneMe);
        }

        public override object Clone() {
            return new CalculateRoiPosition(this);
        }

        private IList<string> issues = new List<string>();

        public IList<string> Issues {
            get => issues;
            set {
                issues = value;
                RaisePropertyChanged();
            }
        }

        public override async Task Execute(IProgress<ApplicationStatus> progress, CancellationToken token) {
            var plateSolver = plateSolverFactory.GetPlateSolver(profileService.ActiveProfile.PlateSolveSettings);

            var parameter = new CaptureSolverParameter() {
                Attempts = profileService.ActiveProfile.PlateSolveSettings.NumberOfAttempts,
                Binning = profileService.ActiveProfile.PlateSolveSettings.Binning,
                Coordinates = telescopeMediator.GetCurrentPosition(),
                DownSampleFactor = profileService.ActiveProfile.PlateSolveSettings.DownSampleFactor,
                FocalLength = profileService.ActiveProfile.TelescopeSettings.FocalLength,
                MaxObjects = profileService.ActiveProfile.PlateSolveSettings.MaxObjects,
                PixelSize = profileService.ActiveProfile.CameraSettings.PixelSize,
                ReattemptDelay = TimeSpan.FromMinutes(profileService.ActiveProfile.PlateSolveSettings.ReattemptDelay),
                Regions = profileService.ActiveProfile.PlateSolveSettings.Regions,
                SearchRadius = profileService.ActiveProfile.PlateSolveSettings.SearchRadius,
                BlindFailoverEnabled = profileService.ActiveProfile.PlateSolveSettings.BlindFailoverEnabled
            };
            Logger.Debug("PlateSolve parameters: " + JsonConvert.SerializeObject(parameter));

            var seq = new CaptureSequence(
                profileService.ActiveProfile.PlateSolveSettings.ExposureTime,
                CaptureSequence.ImageTypes.SNAPSHOT,
                profileService.ActiveProfile.PlateSolveSettings.Filter,
                new BinningMode(profileService.ActiveProfile.PlateSolveSettings.Binning, profileService.ActiveProfile.PlateSolveSettings.Binning),
                1
            );
            
            Logger.Debug("Capturing image for platesolve.");
            var exposureData = await imagingMediator.CaptureImage(seq, token, progress);
            var imageData = await exposureData.ToImageData(progress, token);

            var prepareTask = imagingMediator.PrepareImage(imageData, new PrepareImageParameters(true, false), token);
            var image = prepareTask.Result;

            var imageSolver = new ImageSolver(plateSolver, null);

            Logger.Debug("Solving image");
            var plateSolveResult = await imageSolver.Solve(image.RawImageData, parameter, progress, token);
            if (!plateSolveResult.Success) {
                throw new SequenceEntityFailedException(Loc.Instance["LblPlatesolveFailed"]);
            }

            Logger.Debug("Calculating target position");
            var arcsecPerPix = AstroUtil.ArcsecPerPixel(profileService.ActiveProfile.CameraSettings.PixelSize * profileService.ActiveProfile.PlateSolveSettings.Binning, profileService.ActiveProfile.TelescopeSettings.FocalLength);
            var width = image.Image.PixelWidth;
            var height = image.Image.PixelHeight;
            var center = new Point(width / 2, height / 2);

            //Translate your coordinates to x/y in relation to center coordinates
            var inputTarget = ItemUtility.RetrieveInputTarget(Parent);
            Point targetPoint = inputTarget.InputCoordinates.Coordinates.XYProjection(plateSolveResult.Coordinates, center, arcsecPerPix, arcsecPerPix, plateSolveResult.Orientation, ProjectionType.Stereographic);
            Logger.Debug("Found target at " + targetPoint.X + "x" + targetPoint.Y);

            // Check if the target is in the image
            if (targetPoint.X < 0 || targetPoint.X > width || targetPoint.Y < 0 || targetPoint.Y > height) {
                Notification.ShowError("TargetPoint is not within the image.");
                throw new SequenceEntityFailedException("Calculation failed. Target outside of image");
            }

            // Place the Roi around the star but within the image.
            var speckleContainer = ItemUtility.RetrieveSpeckleContainer(Parent);
            speckleContainer.X = Math.Min(Math.Max(Math.Round(targetPoint.X - (speckleContainer.Width / 2), 0), 0), image.Image.PixelWidth - (speckleContainer.Width / 2));
            speckleContainer.Y = Math.Min(Math.Max(Math.Round(targetPoint.Y - (speckleContainer.Height / 2), 0), 0), image.Image.PixelHeight - (speckleContainer.Height / 2));
            Logger.Debug("Setting roi position to " + speckleContainer.X + "x" + speckleContainer.Y);

            var speckleTarget = ItemUtility.RetrieveSpeckleTarget(Parent);
            if (speckleTarget != null) {
                speckleTarget.Orientation = plateSolveResult.Orientation;
                speckleTarget.ArcsecPerPix = arcsecPerPix;
            }
        }

        public virtual bool Validate() {
            var i = new List<string>();
            if (!telescopeMediator.GetInfo().Connected) {
                i.Add(Loc.Instance["LblTelescopeNotConnected"]);
            }
            if (ItemUtility.RetrieveSpeckleContainer(Parent) == null) {
                i.Add("This instruction only works within a SpeckleTargetContainer.");
            }

            Issues = i;
            return i.Count == 0;
        }

        public override string ToString() {
            return $"Category: {Category}, Item: {nameof(CalculateRoiPosition)}";
        }
    }
}