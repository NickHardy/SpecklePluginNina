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
using NINA.Core.Enum;
using NINA.Profile.Interfaces;
using NINA.Sequencer.Conditions;
using NINA.Sequencer.Container.ExecutionStrategy;
using NINA.Sequencer.SequenceItem;
using NINA.Sequencer.Trigger;
using NINA.Core.Utility;
using NINA.Astrometry;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel.Composition;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using NINA.WPF.Base.Interfaces.Mediator;
using NINA.Equipment.Exceptions;
using NINA.Core.Utility.Notification;
using NINA.Core.Locale;
using NINA.Astrometry.Interfaces;
using NINA.Equipment.Interfaces;
using NINA.WPF.Base.Interfaces.ViewModel;
using System.Windows;
using NINA.Sequencer.Container;
using NINA.Sequencer;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Equipment.Equipment.MyCamera;
using NINA.Plugin.Speckle.Model;

namespace NINA.Plugin.Speckle.Sequencer.Container {

    [ExportMetadata("Name", "Speckle Target Container")]
    [ExportMetadata("Description", "Lbl_SequenceContainer_DeepSkyObjectContainer_Description")]
    [ExportMetadata("Icon", "TelescopeSVG")]
    [ExportMetadata("Category", "Speckle Interferometry")]
    [Export(typeof(ISequenceItem))]
    [Export(typeof(ISequenceContainer))]
    [JsonObject(MemberSerialization.OptIn)]
    public class SpeckleTargetContainer : SequenceContainer, IDeepSkyObjectContainer, ICameraConsumer {
        private readonly IProfileService profileService;
        private ICameraMediator cameraMediator;
        private readonly IFramingAssistantVM framingAssistantVM;
        private readonly IPlanetariumFactory planetariumFactory;
        private readonly IApplicationMediator applicationMediator;
        private INighttimeCalculator nighttimeCalculator;
        private InputTarget target;
        private Speckle speckle;

        [ImportingConstructor]
        public SpeckleTargetContainer(
                IProfileService profileService,
                ICameraMediator cameraMediator,
                INighttimeCalculator nighttimeCalculator,
                IFramingAssistantVM framingAssistantVM,
                IApplicationMediator applicationMediator,
                IPlanetariumFactory planetariumFactory) : base(new SequentialStrategy()) {
            this.profileService = profileService;
            this.cameraMediator = cameraMediator;
            this.nighttimeCalculator = nighttimeCalculator;
            this.applicationMediator = applicationMediator;
            this.framingAssistantVM = framingAssistantVM;
            this.planetariumFactory = planetariumFactory;
            cameraMediator.RegisterConsumer(this);
            speckle = new Speckle();
            SubSampleRectangle = new ObservableRectangle(0, 0, 320, 240);
            Task.Run(() => NighttimeData = nighttimeCalculator.Calculate());
            Target = new InputTarget(Angle.ByDegree(profileService.ActiveProfile.AstrometrySettings.Latitude), Angle.ByDegree(profileService.ActiveProfile.AstrometrySettings.Longitude), profileService.ActiveProfile.AstrometrySettings.Horizon);
            CoordsToFramingCommand = new GalaSoft.MvvmLight.Command.RelayCommand(SendCoordinatesToFraming);
            CoordsFromPlanetariumCommand = new GalaSoft.MvvmLight.Command.RelayCommand(GetCoordsFromPlanetarium);
            DropTargetCommand = new GalaSoft.MvvmLight.Command.RelayCommand<object>(DropTarget);

            WeakEventManager<IProfileService, EventArgs>.AddHandler(profileService, nameof(profileService.LocationChanged), ProfileService_LocationChanged);
            WeakEventManager<IProfileService, EventArgs>.AddHandler(profileService, nameof(profileService.HorizonChanged), ProfileService_HorizonChanged);
        }

        private void SendCoordinatesToFraming() {
            _ = CoordsToFraming();
        }

        private void GetCoordsFromPlanetarium() {
            _ = CoordsFromPlanetarium();
        }

        private void ProfileService_HorizonChanged(object sender, EventArgs e) {
            Target?.DeepSkyObject?.SetCustomHorizon(profileService.ActiveProfile.AstrometrySettings.Horizon);
        }

        private void ProfileService_LocationChanged(object sender, EventArgs e) {
            Target?.SetPosition(Angle.ByDegree(profileService.ActiveProfile.AstrometrySettings.Latitude), Angle.ByDegree(profileService.ActiveProfile.AstrometrySettings.Longitude));
        }

        private void DropTarget(object obj) {
            var p = obj as NINA.Sequencer.DragDrop.DropIntoParameters;
            if (p != null) {
                var con = p.Source as TargetSequenceContainer;
                if (con != null) {
                    var dropTarget = con.Container.Target;
                    if (dropTarget != null) {
                        this.Name = dropTarget.TargetName;
                        this.Target.TargetName = dropTarget.TargetName;
                        this.Target.InputCoordinates = dropTarget.InputCoordinates.Clone();
                        this.Target.PositionAngle = dropTarget.PositionAngle;
                    }
                }
            }
        }

        public NighttimeData NighttimeData { get; private set; }

        public ICommand CoordsToFramingCommand { get; set; }
        public ICommand CoordsFromPlanetariumCommand { get; set; }
        public ICommand DropTargetCommand { get; set; }

        private string _title;
        [JsonProperty]
        public string Title {
            get => _title;
            set {
                _title = value;
                RaisePropertyChanged();
            }
        }

        private bool _isRef;
        [JsonProperty]
        public bool IsRef {
            get => _isRef;
            set {
                _isRef = value;
                RaisePropertyChanged();
            }
        }

        private SpeckleTarget _speckleTarget;

        [JsonProperty]
        public SpeckleTarget SpeckleTarget {
            get => _speckleTarget;
            set {
                _speckleTarget = value;
                RaisePropertyChanged();
            }
        }

        [JsonProperty]
        public InputTarget Target {
            get => target;
            set {
                if (Target != null) {
                    WeakEventManager<InputTarget, EventArgs>.RemoveHandler(Target, nameof(Target.CoordinatesChanged), Target_OnCoordinatesChanged);
                }
                target = value;
                if (Target != null) {
                    WeakEventManager<InputTarget, EventArgs>.AddHandler(Target, nameof(Target.CoordinatesChanged), Target_OnCoordinatesChanged);
                }
                RaisePropertyChanged();
            }
        }

        private void Target_OnCoordinatesChanged(object sender, EventArgs e) {
            AfterParentChanged();
        }

        private CameraInfo cameraInfo;

        public CameraInfo CameraInfo {
            get => cameraInfo;
            private set {
                cameraInfo = value;
                RaisePropertyChanged();
            }
        }

        public void UpdateDeviceInfo(CameraInfo deviceInfo) {
            CameraInfo = deviceInfo;
        }

        public void Dispose() {
            cameraMediator.RemoveConsumer(this);
        }

        private ObservableRectangle subSampleRectangle;

        [JsonProperty]
        public ObservableRectangle SubSampleRectangle {
            get => subSampleRectangle;
            set {
                subSampleRectangle = value;
                RaisePropertyChanged();
            }
        }

        public double X {
            get => SubSampleRectangle.X;
            set {
                SubSampleRectangle.X = value;
                RaisePropertyChanged();
            }
        }

        public double Y {
            get => SubSampleRectangle.Y;
            set {
                SubSampleRectangle.Y = value;
                RaisePropertyChanged();
            }
        }

        public double Width {
            get => SubSampleRectangle.Width;
            set {
                SubSampleRectangle.Width = value;
                RaiseAllPropertiesChanged();
            }
        }

        public double Height {
            get => SubSampleRectangle.Height;
            set {
                SubSampleRectangle.Height = value;
                RaiseAllPropertiesChanged();
            }
        }

        public override object Clone() {
            var clone = new SpeckleTargetContainer(profileService, cameraMediator, nighttimeCalculator, framingAssistantVM, applicationMediator, planetariumFactory) {
                Icon = Icon,
                Name = Name,
                Category = Category,
                Description = Description,
                Items = new ObservableCollection<ISequenceItem>(Items.Select(i => i.Clone() as ISequenceItem)),
                Triggers = new ObservableCollection<ISequenceTrigger>(Triggers.Select(t => t.Clone() as ISequenceTrigger)),
                Conditions = new ObservableCollection<ISequenceCondition>(Conditions.Select(t => t.Clone() as ISequenceCondition)),
                Target = new InputTarget(Angle.ByDegree(profileService.ActiveProfile.AstrometrySettings.Latitude), Angle.ByDegree(profileService.ActiveProfile.AstrometrySettings.Longitude), profileService.ActiveProfile.AstrometrySettings.Horizon),
                Title = Title
            };

            clone.Target.TargetName = this.Target.TargetName;
            clone.Target.InputCoordinates.Coordinates = this.Target.InputCoordinates.Coordinates.Transform(Epoch.J2000);
            clone.Target.PositionAngle = this.Target.PositionAngle;

            clone.SubSampleRectangle.X = SubSampleRectangle.X;
            clone.SubSampleRectangle.Y = SubSampleRectangle.Y;
            clone.SubSampleRectangle.Width = SubSampleRectangle.Width;
            clone.SubSampleRectangle.Height = SubSampleRectangle.Height;

            foreach (var item in clone.Items) {
                item.AttachNewParent(clone);
            }

            foreach (var condition in clone.Conditions) {
                condition.AttachNewParent(clone);
            }

            foreach (var trigger in clone.Triggers) {
                trigger.AttachNewParent(clone);
            }

            return clone;
        }

        public override string ToString() {
            var baseString = base.ToString();
            return $"{baseString}, {Title} Target: {Target?.TargetName}";
        }

        private async Task<bool> CoordsToFraming() {
            if (Target.DeepSkyObject?.Coordinates != null) {
                var dso = new DeepSkyObject(Target.DeepSkyObject.Name, Target.DeepSkyObject.Coordinates, profileService.ActiveProfile.ApplicationSettings.SkyAtlasImageRepository, profileService.ActiveProfile.AstrometrySettings.Horizon);
                dso.RotationPositionAngle = Target.PositionAngle;
                applicationMediator.ChangeTab(ApplicationTab.FRAMINGASSISTANT);
                return await framingAssistantVM.SetCoordinates(dso);
            }
            return false;
        }

        private async Task<bool> CoordsFromPlanetarium() {
            IPlanetarium s = planetariumFactory.GetPlanetarium();
            DeepSkyObject resp = null;

            try {
                resp = await s.GetTarget();

                if (resp != null) {
                    Target.InputCoordinates.Coordinates = resp.Coordinates;
                    Target.TargetName = resp.Name;
                    this.Name = resp.Name;

                    Target.PositionAngle = 0;

                    if (s.CanGetRotationAngle) {
                        double PositionAngleAngle = await s.GetRotationAngle();

                        if (!double.IsNaN(PositionAngleAngle)) {
                            Target.PositionAngle = PositionAngleAngle;
                        }
                    }

                    Notification.ShowSuccess(string.Format(Loc.Instance["LblPlanetariumCoordsOk"], s.Name));
                }
            } catch (PlanetariumObjectNotSelectedException) {
                Logger.Error($"Attempted to get coordinates from {s.Name} when no object was selected");
                Notification.ShowError(string.Format(Loc.Instance["LblPlanetariumObjectNotSelected"], s.Name));
            } catch (PlanetariumFailedToConnect ex) {
                Logger.Error($"Unable to connect to {s.Name}: {ex}");
                Notification.ShowError(string.Format(Loc.Instance["LblPlanetariumFailedToConnect"], s.Name));
            } catch (Exception ex) {
                Logger.Error($"Failed to get coordinates from {s.Name}: {ex}");
                Notification.ShowError(string.Format(Loc.Instance["LblPlanetariumCoordsError"], s.Name));
            }

            return resp != null;
        }
    }
}