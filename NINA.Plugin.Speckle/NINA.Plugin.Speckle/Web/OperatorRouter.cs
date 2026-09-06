using Newtonsoft.Json;
using NINA.Astrometry;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Profile.Interfaces;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;

namespace NINA.Plugin.Speckle.Web {

    internal sealed class OperatorRouter {
        private const string TokenPlaceholder = "__SPECKLE_TOKEN__";
        private const string TokenHeader = "X-Speckle-Token";
        private static readonly DateTime UnixEpoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        private readonly OperatorState state;
        private readonly IProfileService profileService;
        private readonly ITelescopeMediator telescopeMediator;
        private readonly string token = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        private readonly Lazy<string> html;
        private readonly HashSet<string> allowedHosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "localhost" };

        public OperatorRouter(OperatorState state, IProfileService profileService, ITelescopeMediator telescopeMediator) {
            this.state = state;
            this.profileService = profileService;
            this.telescopeMediator = telescopeMediator;
            html = new Lazy<string>(() => EmbeddedAssets.OperatorHtml.Replace(TokenPlaceholder, token));
            AddHostName(Environment.MachineName);
            try { AddHostName(Dns.GetHostName()); } catch (Exception) { }
        }

        public HttpReply Route(HttpRequest request) {
            if (!HostAllowed(request.Header("Host"))) {
                return HttpReply.Text(400, "bad host");
            }
            var path = request.Path;
            if (string.Equals(path, "/", StringComparison.Ordinal)) {
                return HttpReply.Html(html.Value);
            }
            if (string.Equals(path, "/background.jpg", StringComparison.OrdinalIgnoreCase)) {
                return HttpReply.Bytes(EmbeddedAssets.BackgroundJpg, "image/jpeg", "public, max-age=86400");
            }
            if (string.Equals(path, "/favicon.ico", StringComparison.OrdinalIgnoreCase)) {
                return HttpReply.Empty(204);
            }
            if (string.Equals(path, "/api/state", StringComparison.OrdinalIgnoreCase)) {
                return HttpReply.Json(BuildState());
            }
            if (string.Equals(path, "/api/ontarget", StringComparison.OrdinalIgnoreCase)) {
                return Resolve(request, true);
            }
            if (string.Equals(path, "/api/skip", StringComparison.OrdinalIgnoreCase)) {
                return Resolve(request, false);
            }
            return HttpReply.Text(404, "not found");
        }

        private void AddHostName(string name) {
            if (!string.IsNullOrEmpty(name)) {
                allowedHosts.Add(name);
                var dot = name.IndexOf('.');
                if (dot > 0) {
                    allowedHosts.Add(name.Substring(0, dot));
                }
            }
        }

        private bool HostAllowed(string header) {
            if (string.IsNullOrEmpty(header)) {
                return false;
            }
            var name = header;
            if (name.StartsWith("[", StringComparison.Ordinal)) {
                var end = name.IndexOf(']');
                name = end > 0 ? name.Substring(1, end - 1) : name;
            } else {
                var colon = name.IndexOf(':');
                if (colon >= 0) {
                    name = name.Substring(0, colon);
                }
            }
            return IPAddress.TryParse(name, out _) || allowedHosts.Contains(name);
        }

        private HttpReply Resolve(HttpRequest request, bool confirmed) {
            if (!string.Equals(request.Method, "POST", StringComparison.OrdinalIgnoreCase)) {
                return HttpReply.Text(405, "method not allowed");
            }
            if (!string.Equals(request.Header(TokenHeader), token, StringComparison.Ordinal)) {
                return HttpReply.Text(403, "forbidden");
            }
            if (!request.Query.TryGetValue("id", out var raw) || !long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)) {
                return HttpReply.Json("{\"ok\":false,\"reason\":\"id\"}");
            }
            var accepted = confirmed ? state.Confirm(id) : state.Skip(id);
            return HttpReply.Json(accepted ? "{\"ok\":true}" : "{\"ok\":false,\"reason\":\"stale\"}");
        }

        private string BuildState() {
            var latitude = profileService.ActiveProfile.AstrometrySettings.Latitude;
            var longitude = profileService.ActiveProfile.AstrometrySettings.Longitude;
            var elevation = profileService.ActiveProfile.AstrometrySettings.Elevation;
            var pending = state.Pending;
            var target = pending?.Target;

            return JsonConvert.SerializeObject(new {
                ts = (DateTime.UtcNow - UnixEpoch).TotalSeconds,
                state = pending == null ? "idle" : "waiting",
                id = pending?.Id ?? 0L,
                kind = pending == null ? "None" : pending.Kind.ToString(),
                targetName = pending?.TargetName ?? string.Empty,
                target = Describe(target, latitude, longitude, elevation),
                canSkip = pending != null && pending.CanSkip,
                message = pending?.Message ?? string.Empty
            });
        }

        private static object Describe(Coordinates coordinates, double latitude, double longitude, double elevation) {
            if (coordinates == null) {
                return null;
            }
            var topocentric = coordinates.Transform(Angle.ByDegree(latitude), Angle.ByDegree(longitude), elevation);
            return new {
                raText = AstroUtil.HoursToHMS(coordinates.RA),
                decText = Dms(coordinates.Dec),
                altText = Dms(topocentric.Altitude.Degree),
                azText = Dms(topocentric.Azimuth.Degree)
            };
        }

        private static string Dms(double degrees) {
            return AstroUtil.DegreesToFitsDMS(degrees).Replace(' ', ':');
        }
    }
}
