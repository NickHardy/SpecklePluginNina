using NINA.Astrometry;
using NINA.Core.Model;
using NINA.Core.Utility;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Plugin.Speckle.Model;
using NINA.Plugin.Speckle.Services;
using NINA.Plugin.Speckle.Workflow;
using NINA.Profile.Interfaces;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using RelayCommand = GalaSoft.MvvmLight.Command.RelayCommand;

namespace NINA.Plugin.Speckle.Dockables.Kepler {

    public class KeplerSkyVM : BaseINPC {
        private const double SurfaceMargin = 28d;
        private const double HitRadius = 14d;
        private const double NameZoomThreshold = 1.6d;
        private const double ChartAzAtBottom = KeplerProjection.SouthAtBottom;
        private const double FallbackSlitWidth = 10d;
        private const int HorizonSampleCount = 180;
        private const int NamedHeadCount = 6;
        private static readonly double[] FaintRingAltitudes = { 30d, 60d };
        private static readonly TimeSpan AnchorLifetime = TimeSpan.FromMinutes(10);
        private static readonly TimeSpan TrackSpan = TimeSpan.FromHours(6);
        private static readonly TimeSpan OrderInterval = TimeSpan.FromSeconds(60);

        private readonly IProfileService profileService;
        private readonly ISpeckleWorkflowCoordinator coordinator;
        private readonly ISpeckleOptionsProvider optionsProvider;
        private readonly ITelescopeMediator telescopeMediator;
        private readonly IDomeMediator domeMediator;
        private readonly Action<SpeckleTarget> imageNext;
        private readonly KeplerSlitProfile slitProfile = new KeplerSlitProfile();
        private readonly KeplerSlitProfile slitOutlineProfile = new KeplerSlitProfile();

        private DispatcherTimer timer;
        private bool isActive;
        private double anchorSidereal;
        private DateTime anchorTaken;
        private bool anchorValid;
        private bool anchorPending;
        private DateTime orderTaken;
        private double[] horizonSamples;
        private CustomHorizon horizonSource;
        private bool? renderedSlitSouth;

        private double surfaceWidth;
        private double surfaceHeight;
        private double scale = 1d;
        private double panX;
        private double panY;
        private Point center;
        private double radius;

        private Geometry horizonGeometry = Geometry.Empty;
        private Geometry faintRingGeometry = Geometry.Empty;
        private Geometry limitRingGeometry = Geometry.Empty;
        private Geometry slitGeometry = Geometry.Empty;
        private Geometry slitOutlineGeometry = Geometry.Empty;
        private Geometry horizonBlockGeometry = Geometry.Empty;
        private Geometry trackGeometry = Geometry.Empty;
        private bool slitDashed;
        private string slitLabel = string.Empty;
        private string nextLabel = string.Empty;
        private string hoverText = string.Empty;
        private string clockText = string.Empty;
        private bool telescopeVisible;
        private double telescopeX;
        private double telescopeY;
        private KeplerMarker hovered;

        public KeplerSkyVM(
            IProfileService profileService,
            ISpeckleWorkflowCoordinator coordinator,
            ISpeckleOptionsProvider optionsProvider,
            ITelescopeMediator telescopeMediator,
            IDomeMediator domeMediator,
            Action<SpeckleTarget> imageNext) {
            this.profileService = profileService;
            this.coordinator = coordinator;
            this.optionsProvider = optionsProvider;
            this.telescopeMediator = telescopeMediator;
            this.domeMediator = domeMediator;
            this.imageNext = imageNext;

            Markers = new AsyncObservableCollection<KeplerMarker>();
            Cardinals = new[] {
                new KeplerLabel("N"),
                new KeplerLabel("E"),
                new KeplerLabel("S"),
                new KeplerLabel("W")
            };
            ResetViewCommand = new RelayCommand(ResetView);
            SlitSouthCommand = new RelayCommand(() => SetSlitSouth(true));
            SlitNorthCommand = new RelayCommand(() => SetSlitSouth(false));
        }

        public AsyncObservableCollection<KeplerMarker> Markers { get; }

        public IReadOnlyList<KeplerLabel> Cardinals { get; }

        public ICommand ResetViewCommand { get; }

        public ICommand SlitSouthCommand { get; }

        public ICommand SlitNorthCommand { get; }

        public bool DomeSlitSouth => optionsProvider.Current.DomeSlitSouth;

        public bool DomeSlitNorth => !optionsProvider.Current.DomeSlitSouth;

        public double SurfaceWidth {
            get => surfaceWidth;
            set { surfaceWidth = value; RaisePropertyChanged(); Recompute(); }
        }

        public double SurfaceHeight {
            get => surfaceHeight;
            set { surfaceHeight = value; RaisePropertyChanged(); Recompute(); }
        }

        public double Scale {
            get => scale;
            private set { scale = value; RaisePropertyChanged(); }
        }

        public double PanX {
            get => panX;
            private set { panX = value; RaisePropertyChanged(); }
        }

        public double PanY {
            get => panY;
            private set { panY = value; RaisePropertyChanged(); }
        }

        public Geometry HorizonGeometry {
            get => horizonGeometry;
            private set { horizonGeometry = value; RaisePropertyChanged(); }
        }

        public Geometry FaintRingGeometry {
            get => faintRingGeometry;
            private set { faintRingGeometry = value; RaisePropertyChanged(); }
        }

        public Geometry LimitRingGeometry {
            get => limitRingGeometry;
            private set { limitRingGeometry = value; RaisePropertyChanged(); }
        }

        public Geometry SlitGeometry {
            get => slitGeometry;
            private set { slitGeometry = value; RaisePropertyChanged(); }
        }

        public Geometry SlitOutlineGeometry {
            get => slitOutlineGeometry;
            private set { slitOutlineGeometry = value; RaisePropertyChanged(); }
        }

        public Geometry HorizonBlockGeometry {
            get => horizonBlockGeometry;
            private set { horizonBlockGeometry = value; RaisePropertyChanged(); }
        }

        public Geometry TrackGeometry {
            get => trackGeometry;
            private set { trackGeometry = value; RaisePropertyChanged(); }
        }

        public bool SlitDashed {
            get => slitDashed;
            private set { slitDashed = value; RaisePropertyChanged(); }
        }

        public string SlitLabel {
            get => slitLabel;
            private set { slitLabel = value; RaisePropertyChanged(); RaisePropertyChanged(nameof(SlitLabelVisible)); }
        }

        public bool SlitLabelVisible => !string.IsNullOrEmpty(slitLabel);

        public string NextLabel {
            get => nextLabel;
            private set { nextLabel = value; RaisePropertyChanged(); }
        }

        public string HoverText {
            get => hoverText;
            private set { hoverText = value; RaisePropertyChanged(); RaisePropertyChanged(nameof(HoverVisible)); }
        }

        public bool HoverVisible => !string.IsNullOrEmpty(hoverText);

        public string ClockText {
            get => clockText;
            private set { clockText = value; RaisePropertyChanged(); }
        }

        public bool TelescopeVisible {
            get => telescopeVisible;
            private set { telescopeVisible = value; RaisePropertyChanged(); }
        }

        public double TelescopeX {
            get => telescopeX;
            private set { telescopeX = value; RaisePropertyChanged(); }
        }

        public double TelescopeY {
            get => telescopeY;
            private set { telescopeY = value; RaisePropertyChanged(); }
        }

        public void Start() {
            UiThread.Post(() => {
                isActive = true;
                EnsureHorizonSamples();
                EnsureTimer();
                timer?.Start();
                Anchor(true);
                SyncMarkers();
                RefreshOrder();
                Recompute();
            });
        }

        public void Stop() {
            UiThread.Post(() => {
                isActive = false;
                timer?.Stop();
                SetHovered(null);
            });
        }

        public void Dispose() {
            Stop();
        }

        public void Refresh() {
            if (!isActive) {
                return;
            }
            UiThread.Post(() => {
                if (!isActive) {
                    return;
                }
                EnsureHorizonSamples();
                SyncMarkers();
                RefreshOrder();
                Recompute();
            });
        }

        public void Pan(double dx, double dy) {
            PanX = panX + dx;
            PanY = panY + dy;
            Recompute();
        }

        public void Zoom(int wheelDelta, Point position) {
            var factor = wheelDelta > 0 ? 1.15d : 1d / 1.15d;
            var targetScale = Math.Clamp(scale * factor, KeplerProjection.MinScale, KeplerProjection.MaxScale);
            if (Math.Abs(targetScale - scale) < 1e-6) {
                return;
            }
            var zoomRatio = targetScale / scale;
            var cursorOffsetX = position.X - surfaceWidth / 2d;
            var cursorOffsetY = position.Y - surfaceHeight / 2d;
            Scale = targetScale;
            PanX = cursorOffsetX - (cursorOffsetX - panX) * zoomRatio;
            PanY = cursorOffsetY - (cursorOffsetY - panY) * zoomRatio;
            Recompute();
        }

        public void ResetView() {
            Logger.Info("UI: Kepler reset view clicked");
            Scale = 1d;
            PanX = 0d;
            PanY = 0d;
            Recompute();
        }

        public KeplerMarker HitTest(Point position) {
            KeplerMarker best = null;
            var bestDistance = HitRadius;
            foreach (var marker in Markers) {
                if (!marker.IsAboveHorizon || !marker.IsSelectable) {
                    continue;
                }
                var dx = marker.X - position.X;
                var dy = marker.Y - position.Y;
                var distance = Math.Sqrt(dx * dx + dy * dy);
                if (distance <= bestDistance) {
                    bestDistance = distance;
                    best = marker;
                }
            }
            return best;
        }

        public KeplerMarker Hover(Point? position) {
            var marker = position.HasValue ? HitTest(position.Value) : null;
            SetHovered(marker);
            return marker;
        }

        public void Select(KeplerMarker marker) {
            if (marker == null || !marker.IsSelectable) {
                return;
            }
            Logger.Info("UI: Kepler target " + marker.Target.Name + " clicked");
            imageNext?.Invoke(marker.Target);
        }

        private void SetSlitSouth(bool south) {
            if (optionsProvider.Current.DomeSlitSouth != south) {
                Logger.Info("UI: Dome slit orientation set to " + (south ? "south" : "north"));
                optionsProvider.Current.DomeSlitSouth = south;
            }
            renderedSlitSouth = south;
            RaisePropertyChanged(nameof(DomeSlitSouth));
            RaisePropertyChanged(nameof(DomeSlitNorth));
            Recompute();
        }

        private void SyncSlitToggle() {
            var south = optionsProvider.Current.DomeSlitSouth;
            if (renderedSlitSouth == south) {
                return;
            }
            renderedSlitSouth = south;
            RaisePropertyChanged(nameof(DomeSlitSouth));
            RaisePropertyChanged(nameof(DomeSlitNorth));
        }

        private void SetHovered(KeplerMarker marker) {
            if (ReferenceEquals(hovered, marker)) {
                return;
            }
            if (hovered != null) {
                hovered.IsHovered = false;
                hovered.ShowName = ShouldShowName(hovered);
            }
            hovered = marker;
            if (hovered != null) {
                hovered.IsHovered = true;
                hovered.ShowName = true;
            }
            UpdateHoverText();
        }

        private void UpdateHoverText() {
            var marker = hovered;
            if (marker == null) {
                HoverText = string.Empty;
                return;
            }
            var text = marker.Label
                + "  alt " + marker.Altitude.ToString("F0", CultureInfo.InvariantCulture) + " deg"
                + "  az " + marker.Azimuth.ToString("F0", CultureInfo.InvariantCulture) + " deg";
            var target = marker.Target;
            if (!target.ImageTarget && !string.IsNullOrWhiteSpace(target.Note2)) {
                text += "  " + target.Note2;
            } else if (target.Cycles > 0) {
                text += "  " + target.CyclesDisplay;
            }
            if (!string.IsNullOrEmpty(marker.OrderNote)) {
                text += "  " + marker.OrderNote;
            }
            if (target.DomeSlitObservationStartTime > DateTime.Now) {
                text += "  in slit at " + target.DomeSlitObservationStartTime.ToString("HH:mm", CultureInfo.InvariantCulture);
            }
            HoverText = text;
        }

        private void EnsureTimer() {
            if (timer != null) {
                return;
            }
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null) {
                return;
            }
            timer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, OnTick, dispatcher);
            timer.Stop();
        }

        private void OnTick(object sender, EventArgs e) {
            Anchor(false);
            if (DateTime.Now - orderTaken >= OrderInterval) {
                EnsureHorizonSamples();
                RefreshOrder();
            }
            Recompute();
        }

        private void Anchor(bool force) {
            if (anchorPending) {
                return;
            }
            if (!force && anchorValid && DateTime.Now - anchorTaken < AnchorLifetime) {
                return;
            }
            anchorPending = true;
            var longitude = Longitude;
            var taken = DateTime.Now;
            Task.Run(() => {
                double sidereal;
                try {
                    sidereal = AstroUtil.GetLocalSiderealTime(taken, longitude);
                } catch (Exception ex) {
                    Logger.Error(ex);
                    anchorPending = false;
                    return;
                }
                UiThread.Post(() => {
                    anchorSidereal = sidereal;
                    anchorTaken = taken;
                    anchorValid = true;
                    anchorPending = false;
                    Recompute();
                });
            });
        }

        private void EnsureHorizonSamples() {
            var horizon = profileService?.ActiveProfile?.AstrometrySettings?.Horizon;
            if (horizonSamples != null && ReferenceEquals(horizon, horizonSource)) {
                return;
            }
            horizonSource = horizon;
            if (horizon == null) {
                horizonSamples = Array.Empty<double>();
                return;
            }
            var samples = new double[HorizonSampleCount];
            var step = 360d / HorizonSampleCount;
            for (var i = 0; i < HorizonSampleCount; i++) {
                try {
                    samples[i] = horizon.GetAltitude(i * step);
                } catch (Exception ex) {
                    Logger.Error(ex);
                    horizonSamples = Array.Empty<double>();
                    return;
                }
            }
            horizonSamples = samples;
        }

        private void SyncMarkers() {
            var targets = coordinator.Targets;
            var matches = Markers.Count == targets.Count;
            if (matches) {
                for (var i = 0; i < targets.Count; i++) {
                    if (!ReferenceEquals(Markers[i].Target, targets[i])) {
                        matches = false;
                        break;
                    }
                }
            }
            if (matches) {
                return;
            }
            SetHovered(null);
            Markers.Clear();
            foreach (var target in targets) {
                Markers.Add(new KeplerMarker(target));
            }
        }

        private void RefreshOrder() {
            orderTaken = DateTime.Now;
            var current = coordinator.Session?.CurrentTarget;
            var order = ObservationOrder(current, orderTaken);
            var queued = coordinator.PinQueue;
            foreach (var marker in Markers) {
                var target = marker.Target;
                var state = KeplerMarkerState.Pending;
                var number = string.Empty;
                var note = string.Empty;
                var index = -1;
                if (ReferenceEquals(target, current)) {
                    state = KeplerMarkerState.Current;
                    number = "now";
                } else if (target.IsComplete) {
                    state = KeplerMarkerState.Done;
                } else if (!target.ImageTarget) {
                    state = KeplerMarkerState.Excluded;
                } else if (!SchedulingRules.HasCyclesLeft(target)) {
                    state = KeplerMarkerState.Unscheduled;
                    note = target.Nights <= target.Completed_nights
                        ? "nights complete"
                        : target.Cycles <= 0 ? "no cycles set" : "cycles complete";
                } else {
                    index = IndexOfReference(order, target);
                    state = ContainsReference(queued, target) ? KeplerMarkerState.Queued : KeplerMarkerState.Pending;
                    if (index >= 0) {
                        number = (index + 1).ToString(CultureInfo.InvariantCulture);
                    } else {
                        note = SkipReason(target, orderTaken);
                    }
                }
                marker.OrderIndex = index;
                marker.OrderNote = note;
                marker.Number = number;
                marker.State = state;
                marker.ShowName = ShouldShowName(marker);
            }
            NextLabel = order.Count > 0 ? "next " + order[0].Name : "no target in window";
        }

        private List<SpeckleTarget> ObservationOrder(SpeckleTarget current, DateTime now) {
            var opts = optionsProvider.Current;
            var quarterAgo = now.AddMinutes(-15);
            var head = coordinator.PinQueue
                .Where(target => !ReferenceEquals(target, current) && SchedulingRules.HasCyclesLeft(target) && ContainsReference(coordinator.Targets, target))
                .ToList();
            var rest = coordinator.Targets
                .Where(target => !ReferenceEquals(target, current) && !ContainsReference(head, target))
                .Where(target => SchedulingRules.HasCyclesLeft(target) && target.ImageTarget)
                .ToList();
            var tail = opts.DomePositionLock
                ? rest.Where(target => SchedulingRules.IsImagedType(target) && target.ImagedAt.GetValueOrDefault(DateTime.MinValue) < quarterAgo)
                    .Where(target => target.DomeSlitAltTimeList.Count > 0 && target.DomeSlitObservationStartTime > now)
                    .OrderBy(target => target.DomeSlitObservationStartTime)
                    .ToList()
                : rest.Where(target => SchedulingRules.IsImagedType(target) && target.ImagedAt.GetValueOrDefault(DateTime.MinValue) < quarterAgo)
                    .Where(target => target.ImageTime > now.AddMinutes(-5))
                    .OrderBy(target => target.Completed_cycles)
                    .ThenBy(target => target.ImageTime)
                    .ToList();
            if (tail.Count == 0 && !opts.DomePositionLock) {
                tail = FillinOrder(rest, opts);
            }
            head.AddRange(tail);
            return head;
        }

        private List<SpeckleTarget> FillinOrder(IEnumerable<SpeckleTarget> candidates, Speckle opts) {
            return candidates
                .Select(candidate => (Target: candidate, Alt: candidate.CurrentAltTime(opts.AltitudeMax, opts.MDistance)))
                .Where(entry => entry.Alt != null && entry.Alt.Altitude > entry.Target.MinAltitude && entry.Alt.DistanceToMoon > opts.MoonDistance)
                .OrderBy(entry => entry.Alt.Altitude)
                .Select(entry => entry.Target)
                .ToList();
        }

        private string SkipReason(SpeckleTarget target, DateTime now) {
            if (!SchedulingRules.IsImagedType(target)) {
                return "type " + target.Type + " is only imaged when queued";
            }
            if (target.ImagedAt.GetValueOrDefault(DateTime.MinValue) >= now.AddMinutes(-15)) {
                return "imaged just now";
            }
            if (optionsProvider.Current.DomePositionLock) {
                return target.DomeSlitAltTimeList.Count == 0 ? "no slit window tonight" : "slit window passed";
            }
            return "image time passed";
        }

        private bool ShouldShowName(KeplerMarker marker) {
            if (marker.IsHovered
                || marker.State == KeplerMarkerState.Current
                || marker.State == KeplerMarkerState.Queued) {
                return true;
            }
            if (marker.State != KeplerMarkerState.Pending) {
                return false;
            }
            return scale > NameZoomThreshold || (marker.OrderIndex >= 0 && marker.OrderIndex < NamedHeadCount);
        }

        private double[] lastLayoutHorizon;
        private double lastLayoutRadius = double.NaN;
        private double lastLayoutSlitAzimuth = double.NaN;
        private double lastLayoutSlitWidth = double.NaN;
        private Point lastLayoutCenter = new Point(double.NaN, double.NaN);
        private double lastLayoutAltitudeMin = double.NaN;
        private double lastLayoutAltitudeMax = double.NaN;
        private double lastLayoutAzAtBottom = double.NaN;

        private void RebuildStaticLayout(Speckle opts) {
            ResolveSlit(out var slitAzimuth, out var slitWidth, out var label, out var dashed);
            SlitLabel = label;
            SlitDashed = dashed;

            var unchanged = ReferenceEquals(horizonSamples, lastLayoutHorizon)
                && radius == lastLayoutRadius
                && center == lastLayoutCenter
                && opts.AltitudeMin == lastLayoutAltitudeMin
                && opts.AltitudeMax == lastLayoutAltitudeMax
                && ChartAzAtBottom == lastLayoutAzAtBottom
                && slitAzimuth == lastLayoutSlitAzimuth
                && slitWidth == lastLayoutSlitWidth;
            if (unchanged) {
                return;
            }
            lastLayoutHorizon = horizonSamples;
            lastLayoutRadius = radius;
            lastLayoutCenter = center;
            lastLayoutAltitudeMin = opts.AltitudeMin;
            lastLayoutAltitudeMax = opts.AltitudeMax;
            lastLayoutAzAtBottom = ChartAzAtBottom;
            lastLayoutSlitAzimuth = slitAzimuth;
            lastLayoutSlitWidth = slitWidth;

            var horizonCircle = new EllipseGeometry(center, radius, radius);
            horizonCircle.Freeze();
            HorizonGeometry = horizonCircle;
            FaintRingGeometry = KeplerProjection.Rings(FaintRingAltitudes, center, radius);
            LimitRingGeometry = KeplerProjection.Rings(new[] { opts.AltitudeMin, opts.AltitudeMax }, center, radius);
            HorizonBlockGeometry = KeplerProjection.HorizonBlock(horizonSamples, ChartAzAtBottom, center, radius);
            SlitGeometry = slitProfile.Band(slitAzimuth, slitWidth, opts.AltitudeMin, opts.AltitudeMax, ChartAzAtBottom, center, radius);
            SlitOutlineGeometry = slitOutlineProfile.Band(slitAzimuth, slitWidth, 0d, 90d, ChartAzAtBottom, center, radius);

            PlaceCardinal(0, KeplerProjection.NorthAtBottom);
            PlaceCardinal(1, 90d);
            PlaceCardinal(2, KeplerProjection.SouthAtBottom);
            PlaceCardinal(3, 270d);
        }

        private void Recompute() {
            SyncSlitToggle();
            if (surfaceWidth < 60d || surfaceHeight < 60d) {
                return;
            }
            var radiusBase = Math.Max(20d, 0.5d * Math.Min(surfaceWidth, surfaceHeight) - SurfaceMargin);
            radius = radiusBase * scale;
            ClampPan();
            center = new Point(surfaceWidth / 2d + panX, surfaceHeight / 2d + panY);
            var opts = optionsProvider.Current;

            RebuildStaticLayout(opts);

            ProjectMarkers();
            ProjectTelescope();
            BuildTracks();
            UpdateHoverText();
            ClockText = DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        }

        private void ClampPan() {
            PanX = Math.Clamp(panX, -radius, radius);
            PanY = Math.Clamp(panY, -radius, radius);
        }

        private void PlaceCardinal(int index, double azimuth) {
            var point = KeplerProjection.Project(0d, azimuth, ChartAzAtBottom, center, radius + 12d);
            Cardinals[index].X = point.X;
            Cardinals[index].Y = point.Y;
        }

        private const double MarkerMoveThreshold = 0.25d;

        private void ProjectMarkers() {
            if (!anchorValid) {
                return;
            }
            var latitude = Latitude;
            var sidereal = KeplerProjection.SiderealHours(anchorSidereal, DateTime.Now - anchorTaken);
            foreach (var marker in Markers) {
                KeplerProjection.AltAz(marker.Target.RA2000, marker.Target.Dec2000, latitude, sidereal, out var altitude, out var azimuth);
                marker.Altitude = altitude;
                marker.Azimuth = azimuth;
                marker.IsAboveHorizon = altitude > 0d;
                marker.ShowName = ShouldShowName(marker);
                if (!marker.IsAboveHorizon) {
                    continue;
                }
                var point = KeplerProjection.Project(altitude, azimuth, ChartAzAtBottom, center, radius);
                if (Math.Abs(marker.X - point.X) >= MarkerMoveThreshold) {
                    marker.X = point.X;
                }
                if (Math.Abs(marker.Y - point.Y) >= MarkerMoveThreshold) {
                    marker.Y = point.Y;
                }
            }
        }

        private void ProjectTelescope() {
            var info = telescopeMediator?.GetInfo();
            if (info == null || !info.Connected || double.IsNaN(info.Altitude) || double.IsNaN(info.Azimuth) || info.Altitude <= 0d) {
                TelescopeVisible = false;
                return;
            }
            var point = KeplerProjection.Project(info.Altitude, KeplerProjection.Mod360(info.Azimuth), ChartAzAtBottom, center, radius);
            TelescopeX = point.X;
            TelescopeY = point.Y;
            TelescopeVisible = true;
        }

        private void BuildTracks() {
            var now = DateTime.Now;
            var until = now.Add(TrackSpan);
            var group = new GeometryGroup();
            foreach (var marker in Markers) {
                if (marker.State != KeplerMarkerState.Current && marker.State != KeplerMarkerState.Queued) {
                    continue;
                }
                var points = marker.Target.AltList
                    .Where(altTime => altTime.Timestamp >= now && altTime.Timestamp <= until && altTime.Altitude > 0d)
                    .OrderBy(altTime => altTime.Timestamp)
                    .Select(altTime => KeplerProjection.Project(altTime.Altitude, altTime.Azimuth, ChartAzAtBottom, center, radius))
                    .ToList();
                var track = KeplerProjection.Track(points);
                if (!track.IsEmpty()) {
                    group.Children.Add(track);
                }
            }
            group.Freeze();
            TrackGeometry = group;
        }

        private void ResolveSlit(out double azimuth, out double width, out string label, out bool dashed) {
            var opts = optionsProvider.Current;
            width = opts.DomeSlitWidth > 0d ? opts.DomeSlitWidth : FallbackSlitWidth;
            var widthNote = opts.DomeSlitWidth > 0d ? string.Empty : ", slit width not set";
            dashed = false;

            var dome = domeMediator?.GetInfo();
            if (dome != null && dome.Connected && !double.IsNaN(dome.Azimuth)) {
                azimuth = KeplerProjection.Mod360(dome.Azimuth);
                dashed = dome.Slewing;
                label = "dome " + Degrees(azimuth) + widthNote;
                return;
            }

            azimuth = opts.DomeSlitSouth ? KeplerProjection.SouthAtBottom : KeplerProjection.NorthAtBottom;
            label = string.Empty;
        }

        private static string Degrees(double value) {
            return value.ToString("F0", CultureInfo.InvariantCulture) + " deg";
        }

        private double Latitude => profileService?.ActiveProfile?.AstrometrySettings?.Latitude ?? 0d;

        private double Longitude => profileService?.ActiveProfile?.AstrometrySettings?.Longitude ?? 0d;

        private static bool ContainsReference(IEnumerable<SpeckleTarget> source, SpeckleTarget target) {
            return IndexOfReference(source, target) >= 0;
        }

        private static int IndexOfReference(IEnumerable<SpeckleTarget> source, SpeckleTarget target) {
            var index = 0;
            foreach (var item in source) {
                if (ReferenceEquals(item, target)) {
                    return index;
                }
                index++;
            }
            return -1;
        }

    }
}
