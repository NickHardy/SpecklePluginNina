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
using NINA.Sequencer.SequenceItem;
using NINA.Sequencer.SequenceItem.Autofocus;
using NINA.Sequencer.Validations;
using NINA.Equipment.Interfaces.Mediator;
using NINA.WPF.Base.Interfaces.ViewModel;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel.Composition;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NINA.WPF.Base.Interfaces.Mediator;
using NINA.Core.Locale;
using NINA.Sequencer.Utility;
using NINA.Core.Utility;
using NINA.Sequencer.Interfaces;
using NINA.Image.ImageAnalysis;
using NINA.WPF.Base.Interfaces;
using NINA.Sequencer.Trigger;
using NINA.Plugin.Speckle.Sequencer.SequenceItem;
using NINA.Equipment.Interfaces;
using NINA.PlateSolving.Interfaces;
using NINA.Core.Utility.WindowService;
using NINA.Sequencer.SequenceItem.Telescope;

namespace NINA.Plugin.Speckle.Sequencer.Trigger {

    [ExportMetadata("Name", "AF on StarCluster after time")]
    [ExportMetadata("Description", "Lbl_SequenceTrigger_AutofocusAfterTimeTrigger_Description")]
    [ExportMetadata("Icon", "AutoFocusAfterTimeSVG")]
    [ExportMetadata("Category", "Lbl_SequenceCategory_Focuser")]
    [Export(typeof(ISequenceTrigger))]
    [JsonObject(MemberSerialization.OptIn)]
    public class AutofocusStarClusterAfterTimeTrigger : SequenceTrigger, IValidatable {
        private IProfileService profileService;
        private IImageHistoryVM history;
        private ICameraMediator cameraMediator;
        private IFilterWheelMediator filterWheelMediator;
        private IFocuserMediator focuserMediator;
        private IAutoFocusVMFactory autoFocusVMFactory;
        private ITelescopeMediator telescopeMediator;
        private IImagingMediator imagingMediator;
        private IGuiderMediator guiderMediator;
        private IDomeMediator domeMediator;
        private IDomeFollower domeFollower;
        private IPlateSolverFactory plateSolverFactory;
        private IWindowServiceFactory windowServiceFactory;
        private DateTime initialTime;
        private bool initialized = false;

        [ImportingConstructor]
        public AutofocusStarClusterAfterTimeTrigger(IProfileService profileService, 
                    IImageHistoryVM history, 
                    ICameraMediator cameraMediator, 
                    IFilterWheelMediator filterWheelMediator, 
                    IFocuserMediator focuserMediator, 
                    IAutoFocusVMFactory autoFocusVMFactory,
                    ITelescopeMediator telescopeMediator,
                    IImagingMediator imagingMediator,
                    IGuiderMediator guiderMediator,
                    IDomeMediator domeMediator,
                    IDomeFollower domeFollower,
                    IPlateSolverFactory plateSolverFactory,
                    IWindowServiceFactory windowServiceFactory) : base() {
            this.profileService = profileService;
            this.history = history;
            this.cameraMediator = cameraMediator;
            this.filterWheelMediator = filterWheelMediator;
            this.focuserMediator = focuserMediator;
            this.autoFocusVMFactory = autoFocusVMFactory;
            this.telescopeMediator = telescopeMediator;
            this.imagingMediator = imagingMediator;
            this.guiderMediator = guiderMediator;
            this.domeMediator = domeMediator;
            this.domeFollower = domeFollower;
            this.plateSolverFactory = plateSolverFactory;
            this.windowServiceFactory = windowServiceFactory;
            
            Amount = 30;
            // center on star cluster
            var centerOnStarCluster = new CenterOnStarCluster(profileService, telescopeMediator, imagingMediator, filterWheelMediator, guiderMediator, domeMediator, domeFollower, plateSolverFactory, windowServiceFactory);
            centerOnStarCluster.SlewBackToTarget = false;
            TriggerRunner.Add(centerOnStarCluster);

            // Run autofocus
            TriggerRunner.Add(new RunAutofocus(profileService, history, cameraMediator, filterWheelMediator, focuserMediator, autoFocusVMFactory));

            // Slew back to target
            TriggerRunner.Add(new SlewScopeToRaDec(telescopeMediator, guiderMediator));
        }

        private AutofocusStarClusterAfterTimeTrigger(AutofocusStarClusterAfterTimeTrigger cloneMe) : this(cloneMe.profileService, cloneMe.history, cloneMe.cameraMediator, cloneMe.filterWheelMediator, cloneMe.focuserMediator, cloneMe.autoFocusVMFactory, cloneMe.telescopeMediator, cloneMe.imagingMediator, cloneMe.guiderMediator, cloneMe.domeMediator, cloneMe.domeFollower, cloneMe.plateSolverFactory, cloneMe.windowServiceFactory) {
            CopyMetaData(cloneMe);
        }

        public override object Clone() {
            return new AutofocusStarClusterAfterTimeTrigger(this) {
                Amount = Amount,
                TriggerRunner = (SequentialContainer)TriggerRunner.Clone()
            };
        }

        private IList<string> issues = new List<string>();

        public IList<string> Issues {
            get => issues;
            set {
                issues = ImmutableList.CreateRange(value);
                RaisePropertyChanged();
            }
        }

        private double amount;

        [JsonProperty]
        public double Amount {
            get => amount;
            set {
                amount = value;
                RaisePropertyChanged();
            }
        }

        private double elapsed;

        public double Elapsed {
            get => elapsed;
            private set {
                elapsed = value;
                RaisePropertyChanged();
            }
        }

        public override async Task Execute(ISequenceContainer context, IProgress<ApplicationStatus> progress, CancellationToken token) {
            TriggerRunner.AttachNewParent(Parent);
            await TriggerRunner.Run(progress, token);
        }

        public override void SequenceBlockInitialize() {
            if (!initialized) {
                initialTime = DateTime.Now;
                initialized = true;
            }
        }

        public override bool ShouldTrigger(ISequenceItem previousItem, ISequenceItem nextItem) {
            if (nextItem == null) { return false; }
            if (!(nextItem is IExposureItem)) { return false; }

            bool shouldTrigger = false;
            var lastAF = history.AutoFocusPoints.LastOrDefault();
            if (lastAF == null) {
                Elapsed = Math.Round((DateTime.Now - initialTime).TotalMinutes, 2);
                shouldTrigger = (DateTime.Now - initialTime) >= TimeSpan.FromMinutes(Amount);
            } else {
                Elapsed = Math.Round((DateTime.Now - lastAF.AutoFocusPoint.Time).TotalMinutes, 2);
                shouldTrigger = (DateTime.Now - lastAF.AutoFocusPoint.Time) >= TimeSpan.FromMinutes(Amount);
            }

            return shouldTrigger;
        }

        public override string ToString() {
            return $"Trigger: {nameof(AutofocusStarClusterAfterTimeTrigger)}, Amount: {Amount}s";
        }

        public bool Validate() {
            var i = new List<string>();
            var cameraInfo = cameraMediator.GetInfo();
            var focuserInfo = focuserMediator.GetInfo();

            if (!cameraInfo.Connected) {
                i.Add(Loc.Instance["LblCameraNotConnected"]);
            }
            if (!focuserInfo.Connected) {
                i.Add(Loc.Instance["LblFocuserNotConnected"]);
            }
            if (Utility.ItemUtility.RetrieveSpeckleContainer(Parent) == null) {
                i.Add("This instruction only works within a SpeckleTargetContainer.");
            }

            Issues = i;
            return i.Count == 0;
        }
    }
}