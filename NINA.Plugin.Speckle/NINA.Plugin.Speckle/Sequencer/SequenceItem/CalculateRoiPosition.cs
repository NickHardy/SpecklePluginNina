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
using NINA.Core.Locale;
using NINA.Core.Model;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Plugin.Speckle.Sequencer.Utility;
using NINA.Plugin.Speckle.Services;
using NINA.Sequencer.SequenceItem;
using NINA.Sequencer.Validations;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Plugin.Speckle.Sequencer.SequenceItem {

    [ExportMetadata("Name", "Calculate Roi Position")]
    [ExportMetadata("Description", "Platesolve an image locate the target and center the ROI position on the target.")]
    [ExportMetadata("Icon", "CrosshairSVG")]
    [ExportMetadata("Category", "Speckle Interferometry")]
    [Export(typeof(ISequenceItem))]
    [JsonObject(MemberSerialization.OptIn)]
    public class CalculateRoiPosition : NINA.Sequencer.SequenceItem.SequenceItem, IValidatable {
        private ITelescopeMediator telescopeMediator;
        private IRoiPositioningService roiPositioningService;

        [ImportingConstructor]
        public CalculateRoiPosition(ITelescopeMediator telescopeMediator, IRoiPositioningService roiPositioningService) {
            this.telescopeMediator = telescopeMediator;
            this.roiPositioningService = roiPositioningService;
            PlatesolveFirst = true;
            ExposureTime = 5;
        }

        private CalculateRoiPosition(CalculateRoiPosition cloneMe) : this(cloneMe.telescopeMediator, cloneMe.roiPositioningService) {
            CopyMetaData(cloneMe);
        }

        public override object Clone() {
            var clone = new CalculateRoiPosition(this);
            clone.ImageFlippedX = ImageFlippedX;
            clone.ImageFlippedY = ImageFlippedY;
            clone.PlatesolveFirst = PlatesolveFirst;
            clone.ExposureTime = ExposureTime;
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

        private bool _ImageFlippedX;
        [JsonProperty]
        public bool ImageFlippedX { get => _ImageFlippedX; set { _ImageFlippedX = value; RaisePropertyChanged(); } }

        private bool _ImageFlippedY;
        [JsonProperty]
        public bool ImageFlippedY { get => _ImageFlippedY; set { _ImageFlippedY = value; RaisePropertyChanged(); } }

        private bool _PlatesolveFirst;
        [JsonProperty]
        public bool PlatesolveFirst { get => _PlatesolveFirst; set { _PlatesolveFirst = value; RaisePropertyChanged(); } }

        private double _ExposureTime;
        [JsonProperty]
        public double ExposureTime { get => _ExposureTime; set { _ExposureTime = value; RaisePropertyChanged(); } }

        public override async Task Execute(IProgress<ApplicationStatus> progress, CancellationToken token) {
            var speckleContainer = ItemUtility.RetrieveSpeckleContainer(Parent);
            var speckleTarget = ItemUtility.RetrieveSpeckleTarget(Parent);

            var request = new RoiPositionRequest {
                Target = ItemUtility.RetrieveInputTarget(Parent),
                SpeckleTarget = speckleTarget,
                Title = speckleContainer.Title,
                RoiWidth = speckleContainer.Width,
                RoiHeight = speckleContainer.Height,
                PlatesolveFirst = PlatesolveFirst,
                ImageFlippedX = ImageFlippedX,
                ImageFlippedY = ImageFlippedY,
                FallbackExposureTime = ExposureTime
            };

            var result = await roiPositioningService.LocateTargetAsync(request, progress, token);

            if (result.RoiX.HasValue) {
                speckleContainer.X = result.RoiX.Value;
            }
            if (result.RoiY.HasValue) {
                speckleContainer.Y = result.RoiY.Value;
            }
            if (speckleTarget != null) {
                if (result.Orientation.HasValue) {
                    speckleTarget.Orientation = result.Orientation.Value;
                }
                if (result.ArcsecPerPix.HasValue) {
                    speckleTarget.ArcsecPerPix = result.ArcsecPerPix.Value;
                }
                if (result.Note != null) {
                    speckleTarget.Note2 = result.Note;
                }
            }
        }

        public virtual bool Validate() {
            var issues = new List<string>();
            if (!telescopeMediator.GetInfo().Connected) {
                issues.Add(Loc.Instance["LblTelescopeNotConnected"]);
            }
            if (ItemUtility.RetrieveSpeckleContainer(Parent) == null && ItemUtility.RetrieveSpeckleListContainer(Parent) == null) {
                issues.Add("This instruction only works within a SpeckleTargetContainer.");
            }

            Issues = issues;
            return issues.Count == 0;
        }

        public override string ToString() {
            return $"Category: {Category}, Item: {nameof(CalculateRoiPosition)}";
        }
    }
}
