using NINA.Astrometry;
using NINA.Core.Enum;
using NINA.Core.Model;
using NINA.Core.Utility;
using NINA.Core.Utility.Notification;
using NINA.Equipment.Interfaces;
using NINA.Plugin.Speckle.Web;
using NINA.Profile.Interfaces;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Plugin.Speckle.ManualMount {

    public class ManualTelescope : BaseINPC, ITelescope {
        private static readonly TimeSpan HostWatchdogWindow = TimeSpan.FromMinutes(9.5);

        private readonly IProfileService profileService;
        private readonly IOperatorPageService operatorPage;
        private readonly OperatorState state;
        private bool connected;
        private double siteLatitude;
        private double siteLongitude;
        private double siteElevation;

        public ManualTelescope(IProfileService profileService, IOperatorPageService operatorPage) {
            this.profileService = profileService;
            this.operatorPage = operatorPage;
            state = operatorPage.State;
        }

        public const string DeviceId = "Speckle_Manual_Telescope";

        public string Id => DeviceId;

        public string Name => "Speckle Manual Telescope";

        public string DisplayName => Name;

        public string Category => "Speckle";

        public string Description => "Operator-confirmed manual telescope. Slews complete when a human confirms On Target.";

        public string DriverInfo => "Speckle Interferometry plugin";

        public string DriverVersion => Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0";

        public bool HasSetupDialog => false;

        public bool Connected {
            get => connected;
            private set {
                connected = value;
                RaisePropertyChanged();
            }
        }

        public Task<bool> Connect(CancellationToken token) {
            if (token.IsCancellationRequested) {
                return Task.FromResult(false);
            }
            var astrometry = profileService.ActiveProfile.AstrometrySettings;
            siteLatitude = astrometry.Latitude;
            siteLongitude = astrometry.Longitude;
            siteElevation = astrometry.Elevation;
            if (state.Reported == null) {
                var zenith = new InputTopocentricCoordinates(Angle.ByDegree(siteLatitude), Angle.ByDegree(siteLongitude));
                zenith.AltDegrees = 90;
                state.Reported = zenith.Coordinates.Transform(Epoch.JNOW);
            }
            if (!operatorPage.Acquire("mount")) {
                Logger.Error("Speckle Manual Telescope not connected because the operator page could not be started");
                Notification.ShowError("The Speckle operator page could not be started, so a manual slew could never be confirmed. Check the operator page port in the Speckle options.");
                return Task.FromResult(false);
            }
            state.ManualMountConnected = true;
            Connected = true;
            Logger.Info("Speckle Manual Telescope connected");
            return Task.FromResult(true);
        }

        public void Disconnect() {
            state.Abort(OperatorRequestKind.MountSlew);
            state.ManualMountConnected = false;
            operatorPage.Release("mount");
            Connected = false;
            Logger.Info("Speckle Manual Telescope disconnected");
        }

        public void SetupDialog() {
        }

        public IList<string> SupportedActions => new List<string>();

        public string Action(string actionName, string actionParameters) {
            throw new NotImplementedException();
        }

        public string SendCommandString(string command, bool raw = true) {
            throw new NotImplementedException();
        }

        public bool SendCommandBool(string command, bool raw = true) {
            throw new NotImplementedException();
        }

        public void SendCommandBlind(string command, bool raw = true) {
            throw new NotImplementedException();
        }

        public Coordinates Coordinates => state.Reported ?? new Coordinates(Angle.ByHours(0), Angle.ByDegree(0), Epoch.JNOW);

        public double RightAscension => Coordinates.RA;

        public string RightAscensionString => AstroUtil.HoursToHMS(RightAscension);

        public double Declination => Coordinates.Dec;

        public string DeclinationString => AstroUtil.DegreesToDMS(Declination);

        public double SiderealTime => AstroUtil.GetLocalSiderealTimeNow(SiteLongitude);

        public string SiderealTimeString => AstroUtil.HoursToHMS(SiderealTime);

        public double Altitude => Topocentric().Altitude.Degree;

        public string AltitudeString => AstroUtil.DegreesToDMS(Altitude);

        public double Azimuth => Topocentric().Azimuth.Degree;

        public string AzimuthString => AstroUtil.DegreesToDMS(Azimuth);

        public double HoursToMeridian => NINA.Astrometry.MeridianFlip
            .TimeToMeridian(Coordinates, Angle.ByHours(SiderealTime)).TotalHours;

        public string HoursToMeridianString => AstroUtil.HoursToHMS(HoursToMeridian);

        public double TimeToMeridianFlip => double.NaN;

        public string TimeToMeridianFlipString => string.Empty;

        public double PrimaryMovingRate { get; set; }

        public double SecondaryMovingRate { get; set; }

        public PierSide SideOfPier => PierSide.pierUnknown;

        public bool CanSetTrackingEnabled => false;

        public bool TrackingEnabled {
            get => true;
            set { }
        }

        public IList<TrackingMode> TrackingModes => new List<TrackingMode> { TrackingMode.Sidereal };

        public TrackingRate TrackingRate => new TrackingRate { TrackingMode = TrackingMode.Sidereal };

        public TrackingMode TrackingMode {
            get => TrackingMode.Sidereal;
            set { }
        }

        public double SiteLatitude {
            get => siteLatitude;
            set {
                siteLatitude = value;
                RaisePropertyChanged();
            }
        }

        public double SiteLongitude {
            get => siteLongitude;
            set {
                siteLongitude = value;
                RaisePropertyChanged();
            }
        }

        public double SiteElevation {
            get => siteElevation;
            set {
                siteElevation = value;
                RaisePropertyChanged();
            }
        }

        public bool AtHome => false;

        public bool CanFindHome => false;

        public bool AtPark => false;

        public bool CanPark => false;

        public bool CanUnpark => false;

        public bool CanSetPark => false;

        public Epoch EquatorialSystem => Epoch.JNOW;

        public bool HasUnknownEpoch => false;

        public Coordinates TargetCoordinates => state.Pending?.Target;

        public PierSide? TargetSideOfPier => null;

        public bool Slewing => state.Pending?.Kind == OperatorRequestKind.MountSlew;

        public double GuideRateRightAscensionArcsecPerSec => 0d;

        public double GuideRateDeclinationArcsecPerSec => 0d;

        public bool CanMovePrimaryAxis => false;

        public bool CanMoveSecondaryAxis => false;

        public bool CanSetDeclinationRate => false;

        public bool CanSetRightAscensionRate => false;

        public AlignmentMode AlignmentMode => AlignmentMode.GermanPolar;

        public bool CanPulseGuide => false;

        public bool IsPulseGuiding => false;

        public bool CanSetPierSide => false;

        public bool CanSlew => true;

        public bool CanSlewAltAz => false;

        public DateTime UTCDate => DateTime.UtcNow;

        public IList<(double, double)> GetAxisRates(TelescopeAxes axis) {
            return new List<(double, double)>();
        }

        public Task<bool> MeridianFlip(Coordinates targetCoordinates, CancellationToken token) {
            return Task.FromResult(false);
        }

        public Task<bool> SlewToAltAz(TopocentricCoordinates coordinates, CancellationToken token) {
            Logger.Warning("Speckle manual telescope was asked to slew to alt az, which the operator page does not support");
            return Task.FromResult(false);
        }

        public void MoveAxis(TelescopeAxes axis, double rate) {
        }

        public void PulseGuide(GuideDirections direction, int duration) {
        }

        public Task Park(CancellationToken token) {
            return Task.CompletedTask;
        }

        public void Setpark() {
        }

        public async Task<bool> SlewToCoordinates(Coordinates coordinates, CancellationToken token) {
            var target = coordinates.Transform(Epoch.JNOW);
            var request = new OperatorRequest {
                Kind = OperatorRequestKind.MountSlew,
                Target = target,
                TargetName = operatorPage.CurrentTargetName,
                CanSkip = false,
                StartedUtc = DateTime.UtcNow,
                Message = "Slew to target, then press ON TARGET"
            };
            var started = DateTime.UtcNow;
            try {
                var wait = state.WaitAsync(request, token);
                RaiseSlewChanged();
                var confirmed = await wait.ConfigureAwait(false);
                if (!confirmed) {
                    var stopped = "The manual slew to " + Describe(target) + " was stopped before On Target was confirmed";
                    Logger.Warning(stopped);
                    Notification.ShowWarning(stopped);
                    return false;
                }
                state.Reported = target;
                RaisePropertyChanged(nameof(Coordinates));
                return true;
            } catch (OperationCanceledException) when (DateTime.UtcNow - started >= HostWatchdogWindow) {
                var abandoned = "The manual slew to " + Describe(target) + " was abandoned by N.I.N.A. after ten minutes without an On Target confirmation";
                Logger.Error(abandoned);
                Notification.ShowError(abandoned);
                throw new SequenceEntityFailedException(abandoned);
            } finally {
                RaiseSlewChanged();
            }
        }

        public void StopSlew() {
            state.Abort(OperatorRequestKind.MountSlew);
        }

        public bool Sync(Coordinates coordinates) {
            state.Reported = coordinates.Transform(Epoch.JNOW);
            RaisePropertyChanged(nameof(Coordinates));
            return true;
        }

        public Task Unpark(CancellationToken token) {
            return Task.CompletedTask;
        }

        public void SetCustomTrackingRate(double rightAscensionRate, double declinationRate) {
        }

        public Task FindHome(CancellationToken token) {
            return Task.CompletedTask;
        }

        public PierSide DestinationSideOfPier(Coordinates coordinates) {
            return PierSide.pierUnknown;
        }

        private TopocentricCoordinates Topocentric() {
            return Coordinates.Transform(Angle.ByDegree(SiteLatitude), Angle.ByDegree(SiteLongitude), SiteElevation);
        }

        private static string Describe(Coordinates coordinates) {
            return coordinates.RAString + " " + coordinates.DecString;
        }

        private void RaiseSlewChanged() {
            RaisePropertyChanged(nameof(Slewing));
            RaisePropertyChanged(nameof(TargetCoordinates));
        }
    }
}
