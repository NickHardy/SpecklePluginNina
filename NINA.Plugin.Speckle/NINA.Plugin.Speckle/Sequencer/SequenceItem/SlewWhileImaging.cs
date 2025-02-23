#region "copyright"

/*
    Copyright © 2016 - 2024 Stefan Berg <isbeorn86+NINA@googlemail.com> and the N.I.N.A. contributors

    This file is part of N.I.N.A. - Nighttime Imaging 'N' Astronomy.

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using Newtonsoft.Json;
using NINA.Core.Model;
using NINA.Core.Utility;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Equipment.Model;
using NINA.Profile.Interfaces;
using NINA.Sequencer.Container;
using NINA.Sequencer.SequenceItem;
using NINA.Sequencer.SequenceItem.Telescope;
using NINA.Sequencer.Utility;
using NINA.WPF.Base.Interfaces.Mediator;
using NINA.WPF.Base.Interfaces.ViewModel;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Documents;

namespace NINA.Plugin.Speckle.Sequencer.SequenceItem {

    [ExportMetadata("Name", "Slew while imaging")]
    [ExportMetadata("Description", "Take images while slewing the mount")]
    [ExportMetadata("Icon", "SlewToRaDecSVG")]
    [ExportMetadata("Category", "Speckle Interferometry")]
    [Export(typeof(ISequenceItem))]
    [Export(typeof(ISequenceContainer))]
    [JsonObject(MemberSerialization.OptIn)]
    public class SlewWhileImaging : SequentialContainer, IImmutableContainer {
        private IProfileService profileService;
        private IImagingMediator imagingMediator;
        private IGuiderMediator guiderMediator;

        [OnDeserializing]
        public void OnDeserializing(StreamingContext context) {
            this.Items.Clear();
            this.Conditions.Clear();
            this.Triggers.Clear();
        }

        [ImportingConstructor]
        public SlewWhileImaging(IProfileService profileService, ICameraMediator cameraMediator, IImagingMediator imagingMediator, IImageSaveMediator imageSaveMediator, IImageHistoryVM imageHistoryVM, ITelescopeMediator telescopeMediator, IGuiderMediator guiderMediator) :
                this(
                    null,
                    profileService,
                    imagingMediator,
                    new SlewScopeToRaDec(telescopeMediator, guiderMediator)
                    ) {
        }

        private InstructionErrorBehavior errorBehavior = InstructionErrorBehavior.ContinueOnError;

        [JsonProperty]
        public override InstructionErrorBehavior ErrorBehavior {
            get => errorBehavior;
            set {
                errorBehavior = value;
                foreach (var item in Items) {
                    item.ErrorBehavior = errorBehavior;
                }
                RaisePropertyChanged();
            }
        }

        private int attempts = 1;

        [JsonProperty]
        public override int Attempts {
            get => attempts;
            set {
                if (value > 0) {
                    attempts = value;
                    foreach (var item in Items) {
                        item.Attempts = attempts;
                    }
                    RaisePropertyChanged();
                }
            }
        }

        private double exposureTime = 1;

        [JsonProperty]
        public double ExposureTime { get => exposureTime; set { exposureTime = value; RaisePropertyChanged(); } }

        /// <summary>
        /// Clone Constructor
        /// </summary>
        private SlewWhileImaging(
                SlewWhileImaging cloneMe,
                IProfileService profileService,
                IImagingMediator imagingMediator,
                SlewScopeToRaDec slewScopeToRaDec) {
            this.profileService = profileService;
            this.imagingMediator = imagingMediator;

            Items.Add(slewScopeToRaDec);

            IsExpanded = false;

            if (cloneMe != null) {
                CopyMetaData(cloneMe);
            }
        }

        public SlewScopeToRaDec GetSlewScopeToRaDec() {
            return (Items[0] as SlewScopeToRaDec);
        }

        public override async Task Execute(IProgress<ApplicationStatus> progress, CancellationToken token) {
            IProgress<ApplicationStatus> localProgress = new Progress<ApplicationStatus>(x => {
                x.Source = "Slew while imaging";
                progress?.Report(x);
            });
            try {
                var capture = new CaptureSequence() {
                    ExposureTime = ExposureTime
                };
                var imageParams = new PrepareImageParameters(true, false);
                var target = Utility.ItemUtility.RetrieveInputTarget(Parent);
                var localCTS = CancellationTokenSource.CreateLinkedTokenSource(token);
                var exposures = 1;
                var slewScopeToRaDec = GetSlewScopeToRaDec();
                slewScopeToRaDec.Coordinates = target.InputCoordinates;
                var imagingTask = Task.Run(async () => {
                    while (localCTS != null && !localCTS.IsCancellationRequested) {
                        capture.ExposureTime = ExposureTime;
                        await imagingMediator.CaptureAndPrepareImage(capture, imageParams, localCTS.Token, progress);
                        localProgress?.Report(new ApplicationStatus() { Status = $"Images {exposures++}" });
                    }
                }, token);
                await slewScopeToRaDec.Execute(progress, token);
                localCTS.Cancel();
            }
            finally {
                await CoreUtil.Wait(TimeSpan.FromMilliseconds(2000));
                localProgress?.Report(new ApplicationStatus() { });
            }
        }

        public override bool Validate() {
            var item = GetSlewScopeToRaDec();
            var valid = item.Validate();
            Issues = item.Issues;
            if (Utility.ItemUtility.RetrieveSpeckleContainer(Parent) == null) {
                Issues.Add("This instruction only works within a SpeckleTargetContainer.");
            }
            RaisePropertyChanged(nameof(Issues));
            return valid;
        }

        public override object Clone() {
            var clone = new SlewWhileImaging(
                    this,
                    profileService,
                    imagingMediator,
                    (SlewScopeToRaDec)this.GetSlewScopeToRaDec().Clone());
            clone.ExposureTime = this.ExposureTime;
            return clone;
        }

        /// <summary>
        /// When an inner instruction interrupts this set, it should reroute the interrupt to the real parent set
        /// </summary>
        /// <returns></returns>
        public override Task Interrupt() {
            return this.Parent?.Interrupt();
        }
    }
}