using CsvHelper;
using CsvHelper.Configuration;
using Newtonsoft.Json;
using NINA.Astrometry;
using NINA.Astrometry.Interfaces;
using NINA.Core.Model;
using NINA.Core.Utility;
using NINA.Core.Utility.Notification;
using NINA.Plugin.Speckle.Model;
using NINA.Profile.Interfaces;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Plugin.Speckle.Services {

    [Export(typeof(ITargetListService))]
    [PartCreationPolicy(CreationPolicy.Shared)]
    public class TargetListService : ITargetListService {
        private readonly IProfileService profileService;
        private readonly INighttimeCalculator nighttimeCalculator;
        private readonly ISpeckleOptionsProvider optionsProvider;
        private readonly IReferenceStarService referenceStarService;

        [ImportingConstructor]
        public TargetListService(IProfileService profileService, INighttimeCalculator nighttimeCalculator, ISpeckleOptionsProvider optionsProvider, IReferenceStarService referenceStarService) {
            this.profileService = profileService;
            this.nighttimeCalculator = nighttimeCalculator;
            this.optionsProvider = optionsProvider;
            this.referenceStarService = referenceStarService;
        }

        private Speckle Options => optionsProvider.Current;


        public NighttimeData GetNighttimeData(DateTime now) {
            if (now.Hour <= 12 && now.Hour >= 8) {
                return nighttimeCalculator.Calculate(now.AddHours(5));
            }
            return nighttimeCalculator.Calculate();
        }

        public Task<IReadOnlyList<SpeckleTarget>> LoadTargetsAsync(string csvPath, TargetListDefaults defaults, bool ignoreLimits, CancellationToken ct) {
            return Task.Run<IReadOnlyList<SpeckleTarget>>(() => {
                var options = Options;
                var night = GetNighttimeData(DateTime.Now);
                var opts = SchedulingOptions.FromOptions(options, ignoreLimits: ignoreLimits);
                var targets = new List<SpeckleTarget>();
                var config = new CsvConfiguration(CultureInfo.InvariantCulture);
                config.MissingFieldFound = null;
                config.TrimOptions = TrimOptions.Trim;
                using (var reader = new StreamReader(csvPath, DetectEncoding(csvPath)))
                using (var csv = new CsvReader(reader, config)) {
                    csv.Context.RegisterClassMap<SpeckleTargetMap>();
                    var records = csv.GetRecords<SpeckleTarget>();
                    foreach (SpeckleTarget speckleTarget in records.ToList()) {
                        ct.ThrowIfCancellationRequested();
                        speckleTarget.Name1 = ToAscii(speckleTarget.Name1);
                        speckleTarget.Name2 = ToAscii(speckleTarget.Name2);
                        speckleTarget.Proj = ToAscii(speckleTarget.Proj);
                        speckleTarget.Obs = ToAscii(speckleTarget.Obs);
                        if (speckleTarget.RA2000 == 0d && speckleTarget.Dec2000 == 0d) {
                            Logger.Debug("No coordinates found. Skipping target " + speckleTarget.Name + " for user " + speckleTarget.Proj);
                            continue;
                        }
                        var defaultUser = defaults?.User?.Trim() ?? "";
                        speckleTarget.Obs = speckleTarget.Obs.Trim() != "" ? speckleTarget.Obs.Trim() : defaultUser != "" ? defaultUser : options.User;
                        if (!ignoreLimits && speckleTarget.Nights > 0 && speckleTarget.Nights <= speckleTarget.Completed_nights) {
                            Logger.Debug("Target already imaged enough nights. Skipping target " + speckleTarget.Name + " for user " + speckleTarget.Proj);
                            speckleTarget.ImageTarget = false;
                            speckleTarget.Note2 = "Target finished.";
                        }
                        if (!ignoreLimits && speckleTarget.Sep > 0 && (speckleTarget.Sep < options.MinSep || speckleTarget.Sep > options.MaxSep)) {
                            Logger.Debug("Seperation not within limits. Skipping target " + speckleTarget.Name + " for user " + speckleTarget.Proj);
                            speckleTarget.ImageTarget = false;
                            speckleTarget.Note2 = "Target not within separation limits.";
                        }
                        speckleTarget.GaiaNum = speckleTarget.GaiaNum.Trim().TrimStart('G');
                        speckleTarget.RefGaiaNum = speckleTarget.RefGaiaNum.Trim().TrimStart('G');
                        speckleTarget.Cycles = speckleTarget.Cycles > 0 ? speckleTarget.Cycles : defaults.Cycles;
                        speckleTarget.Nights = speckleTarget.Nights > 0 ? speckleTarget.Nights : options.Nights;
                        if (speckleTarget.Cycles > 0 && speckleTarget.Completed_cycles >= speckleTarget.Cycles
                            && speckleTarget.Nights > speckleTarget.Completed_nights
                            && !IsProgressFromCurrentNight(csvPath)) {
                            Logger.Info("Starting a new night for " + speckleTarget.Name + ", so its cycle progress is reset from "
                                + speckleTarget.Completed_cycles + " of " + speckleTarget.Cycles + " back to none");
                            speckleTarget.Completed_cycles = 0;
                            speckleTarget.Completed_ref_cycles = 0;
                        }
                        speckleTarget.Exp = speckleTarget.Exp > 0 ? speckleTarget.Exp : defaults.ExposureTime;
                        speckleTarget.NExp = speckleTarget.NExp > 0 ? speckleTarget.NExp : defaults.Exposures;
                        ApplyVisibilityWindows(speckleTarget, night, opts);
                        speckleTarget.Template = FirstNonEmpty(speckleTarget.Template, defaults.Template, options.DefaultTemplate);
                        speckleTarget.TemplateRef = FirstNonEmpty(speckleTarget.TemplateRef, defaults.TemplateRef, options.DefaultRefTemplate);
                        if (speckleTarget.Type == "M" || speckleTarget.Type == "C") {
                            if (!ignoreLimits && speckleTarget.Pmag > 0 && (speckleTarget.Pmag < options.MinMag || speckleTarget.Pmag > options.MaxMag)) {
                                Logger.Debug("Magnitude not within limits. Skipping target " + speckleTarget.Name + " for user " + speckleTarget.Proj);
                                speckleTarget.ImageTarget = false;
                                speckleTarget.Note2 = "Target not within magnitude limits.";
                            }
                            if (speckleTarget.Smag == 0) {
                                Logger.Debug("Failed to get secondary magnitude for " + speckleTarget.Name);
                                speckleTarget.NoEC = 1;
                            }
                        }
                        speckleTarget.SourceList = csvPath;
                        targets.Add(speckleTarget);
                    }
                }
                return Order(targets);
            }, ct);
        }

        public IReadOnlyList<SpeckleTarget> Order(IEnumerable<SpeckleTarget> targets) {
            return targets.GroupBy(target => target.ImageTime)
                .SelectMany(sameTime => sameTime.OrderByDescending(candidate => candidate.Priority).ThenByDescending(target => target.ImageTimeAlt).ToList())
                .OrderBy(target => target.ImageTime).ThenBy(target => target.ImageTimeAlt)
                .ToList();
        }

        private static Encoding DetectEncoding(string path) {
            var probe = new byte[4096];
            int read;
            using (var stream = File.OpenRead(path)) {
                read = stream.Read(probe, 0, probe.Length);
            }
            if (read >= 3 && probe[0] == 0xEF && probe[1] == 0xBB && probe[2] == 0xBF) {
                return new UTF8Encoding(true);
            }
            if (read >= 2 && probe[0] == 0xFF && probe[1] == 0xFE) {
                return Encoding.Unicode;
            }
            if (read >= 2 && probe[0] == 0xFE && probe[1] == 0xFF) {
                return Encoding.BigEndianUnicode;
            }
            try {
                new UTF8Encoding(false, true).GetString(probe, 0, read);
                return new UTF8Encoding(false);
            } catch (DecoderFallbackException) {
                return Encoding.Latin1;
            }
        }

        private static string DescribeRejection(SpeckleTarget target, NighttimeData night, SchedulingOptions opts, AltTime chosen) {
            var set = night.NauticalTwilightRiseAndSet.Set;
            var rise = night.NauticalTwilightRiseAndSet.Rise;
            var window = "darkness " + (set.HasValue ? set.Value.ToString("HH:mm") : "unknown") + " to " + (rise.HasValue ? rise.Value.ToString("HH:mm") : "unknown");
            if (target.AltList == null || target.AltList.Count == 0) {
                return "Never rises above the horizon tonight";
            }
            if (chosen != null) {
                if (chosen.Altitude <= target.MinAltitude) {
                    return "Peaks at " + chosen.Altitude.ToString("F0") + " deg during " + window + ", below the " + target.MinAltitude.ToString("F0") + " deg minimum";
                }
                return "Best time " + chosen.Timestamp.ToString("HH:mm") + " falls outside " + window;
            }
            var inWindow = target.AltList.Where(altTime => (!set.HasValue || altTime.Timestamp >= set.Value) && (!rise.HasValue || altTime.Timestamp <= rise.Value)).ToList();
            if (inWindow.Count == 0) {
                return "Only up outside " + window;
            }
            var maxAlt = inWindow.Max(altTime => altTime.Altitude);
            if (maxAlt <= target.MinAltitude) {
                return "Peaks at " + maxAlt.ToString("F0") + " deg during " + window + ", below the " + target.MinAltitude.ToString("F0") + " deg minimum";
            }
            var moonOk = inWindow.Where(altTime => altTime.DistanceToMoon >= opts.MoonDistance).ToList();
            if (moonOk.Count == 0) {
                var closest = inWindow.Max(altTime => altTime.DistanceToMoon);
                return "Never more than " + closest.ToString("F0") + " deg from the moon (" + opts.MoonDistance.ToString("F0") + " deg required)";
            }
            var airmassOk = moonOk.Where(altTime => altTime.Airmass >= target.AirmassMin && altTime.Airmass <= target.AirmassMax).ToList();
            if (airmassOk.Count == 0) {
                return "Airmass never between " + target.AirmassMin.ToString("F1") + " and " + target.AirmassMax.ToString("F1") + " during " + window;
            }
            if (airmassOk.All(altTime => altTime.Altitude > opts.AltitudeMax)) {
                return "Always above the " + opts.AltitudeMax.ToString("F0") + " deg maximum altitude during " + window;
            }
            var meridian = target.MeridianAltTime();
            var passingMeridian = airmassOk.Count(altTime => meridian == null || altTime.HourAngleDegrees <= meridian.HourAngleDegrees - opts.MDistance || altTime.HourAngleDegrees >= meridian.HourAngleDegrees + opts.MDistance);
            var first = target.AltList.First().Timestamp.ToString("HH:mm");
            var last = target.AltList.Last().Timestamp.ToString("HH:mm");
            return "No usable time found in " + window
                + " [samples " + first + "-" + last + " n=" + target.AltList.Count
                + ", in window " + inWindow.Count
                + ", moon ok " + moonOk.Count
                + ", airmass ok " + airmassOk.Count
                + ", meridian ok " + passingMeridian
                + ", best alt " + maxAlt.ToString("F0") + " deg"
                + ", min alt " + target.MinAltitude.ToString("F0")
                + ", max alt " + opts.AltitudeMax.ToString("F0") + "]";
        }

        private static string FirstNonEmpty(params string[] values) {
            foreach (var value in values) {
                if (!string.IsNullOrWhiteSpace(value)) {
                    return value.Trim();
                }
            }
            return "";
        }

        private static string ToAscii(string value) {
            if (string.IsNullOrEmpty(value)) {
                return value;
            }
            var builder = new StringBuilder(value.Length);
            foreach (var character in value) {
                builder.Append(character < 128 ? character : ' ');
            }
            return string.Join(" ", builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
        }

        public List<AltTime> ComputeAltList(Coordinates coords, ObserverInfo observer, CustomHorizon horizon, double stepHours) {
            return AltTimeCalculator.Compute(coords, observer, horizon, stepHours);
        }

        public void ApplyVisibilityWindows(SpeckleTarget target, NighttimeData night, SchedulingOptions opts) {
            var slitAz1 = opts.DomePosition - (opts.DomeSlitWidth / 2);
            var slitAz2 = opts.DomePosition + (opts.DomeSlitWidth / 2);
            target.MinAltitude = target.MinAltitude == 0d ? opts.AltitudeMin : target.MinAltitude;
            target.AltList = ComputeAltList(target.Coordinates(), ObservingSite.Of(profileService), profileService.ActiveProfile.AstrometrySettings.Horizon, opts.DomePositionLock ? 0.01 : 0.05);
            target.BuildDomeSlitAltTimeList(opts.AltitudeMin, opts.AltitudeMax, slitAz1, slitAz2, target.AirmassMin, target.AirmassMax);
            var imageTo = target.DomeSlitAltTimeList.OrderBy(altTime => altTime.Timestamp).FirstOrDefault();
            if (!opts.DomePositionLock) {
                imageTo = target.ImageTo(night, opts.AltitudeMax, opts.MDistance, target.AirmassMin, target.AirmassMax, opts.MoonDistance);
            }
            if (imageTo != null && imageTo.Altitude > target.MinAltitude && imageTo.Timestamp >= night.NauticalTwilightRiseAndSet.Set && imageTo.Timestamp <= night.NauticalTwilightRiseAndSet.Rise) {
                target.ImageTime = opts.DomePositionLock ? imageTo.Timestamp : RoundUp(imageTo.Timestamp, TimeSpan.FromMinutes(5));
                target.ImageTimeAlt = imageTo.Altitude;
            } else if (!opts.IgnoreLimits) {
                var reason = DescribeRejection(target, night, opts, imageTo);
                Logger.Info("Not observable: " + target.Name + " (" + target.Proj + ") - " + reason);
                target.ImageTarget = false;
                target.Note2 = reason;
            } else {
                target.ImageTime = DateTime.Now;
                target.ImageTimeAlt = imageTo?.Altitude ?? target.AltList.OrderByDescending(altTime => altTime.Altitude).FirstOrDefault()?.Altitude ?? 0d;
            }
        }

        private static DateTime RoundUp(DateTime moment, TimeSpan step) {
            return new DateTime((moment.Ticks + step.Ticks - 1) / step.Ticks * step.Ticks, moment.Kind);
        }

        public void WriteTargets(IEnumerable<SpeckleTarget> targets, string csvPath) {
            using (var writer = new StreamWriter(csvPath))
            using (var csv = new CsvWriter(writer, CultureInfo.InvariantCulture)) {
                csv.Context.RegisterClassMap<SpeckleTargetMap>();
                csv.WriteRecords(targets);
            }
        }

        public async Task SaveSnapshotAsync(IEnumerable<SpeckleTarget> targets, string directory) {
            try {
                await Task.Run(() => {
                    var stamp = ObservingNightStamp(DateTime.Now);
                    var lists = (targets ?? Enumerable.Empty<SpeckleTarget>())
                        .GroupBy(target => target.SourceList ?? "")
                        .ToList();
                    foreach (var list in lists) {
                        var csvfile = Path.Combine(directory, SnapshotFileName(list.Key, lists.Count > 1, stamp));
                        WarnIfSnapshotBelongsToAnotherList(csvfile, list.Key);
                        WriteTargets(list, csvfile);
                        Logger.Info("Progress for " + list.Count() + " targets from "
                            + (string.IsNullOrWhiteSpace(list.Key) ? "an unnamed list" : list.Key) + " written to " + csvfile);
                    }
                }).ConfigureAwait(false);
            } catch (Exception ex) {
                Logger.Error("Failed to write target list snapshot.", ex);
                Notification.ShowError("Failed to write target list snapshot: " + ex.Message);
            }
        }

        private readonly Dictionary<string, string> snapshotSources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private void WarnIfSnapshotBelongsToAnotherList(string csvPath, string sourceList) {
            lock (snapshotSources) {
                if (snapshotSources.TryGetValue(csvPath, out var previous)
                    && !string.Equals(previous, sourceList, StringComparison.OrdinalIgnoreCase)) {
                    Logger.Warning("Progress for " + sourceList + " is overwriting the snapshot last written for "
                        + previous + " at " + csvPath + ". Load both lists together if you want both kept.");
                    Notification.ShowWarning("The progress file " + Path.GetFileName(csvPath)
                        + " now holds " + sourceList + " and no longer holds " + previous + ".");
                }
                snapshotSources[csvPath] = sourceList;
            }
        }

        private static string ObservingNightStamp(DateTime moment) {
            return moment.AddHours(-12).ToString("yyyy-MM-dd");
        }

        private static bool IsProgressFromCurrentNight(string csvPath) {
            var fileName = Path.GetFileNameWithoutExtension(csvPath) ?? string.Empty;
            return fileName.StartsWith("TargetList-", StringComparison.OrdinalIgnoreCase)
                && fileName.EndsWith(ObservingNightStamp(DateTime.Now), StringComparison.Ordinal);
        }

        private static string SnapshotFileName(string sourceList, bool nameAfterSourceList, string stamp) {
            if (!nameAfterSourceList) {
                return "TargetList-" + stamp + ".csv";
            }
            var name = string.IsNullOrWhiteSpace(sourceList) ? "unlisted" : Path.GetFileNameWithoutExtension(sourceList);
            foreach (var invalid in Path.GetInvalidFileNameChars()) {
                name = name.Replace(invalid, '_');
            }
            return "TargetList-" + name + "-" + stamp + ".csv";
        }

        public async Task<ReferenceStarExportResult> ExportWithReferenceStarsAsync(IReadOnlyList<SpeckleTarget> targets, ExportFormat format, string directory, IProgress<ApplicationStatus> progress, CancellationToken ct) {
            var exportTargets = targets.Where(SchedulingRules.IsImagedType).OrderBy(target => target.RA2000).ToList();
            var surfaced = new List<SpeckleTarget>();
            if (format == ExportFormat.Json) {
                foreach (var target in exportTargets) {
                    if (target.Type == "G") {
                        continue;
                    }
                    await ResolveIfNeededAsync(target, targets, progress, ct).ConfigureAwait(false);
                    var referenceStar = target.ReferenceStar;
                    if (referenceStar != null && referenceStar.RA2000 != 0) {
                        surfaced.Add(BuildReferenceStarRecord(target, referenceStar));
                    }
                }
                var jsonExport = JsonConvert.SerializeObject(exportTargets, Formatting.Indented);
                string jsonfile = Path.Combine(directory, "TargetListIncludingReferenceStars.json");
                using (var writer = new StreamWriter(jsonfile)) {
                    writer.Write(jsonExport);
                }
                return new ReferenceStarExportResult { FilePath = jsonfile, SurfacedReferenceRecords = surfaced };
            }

            var targetsWithReferenceStars = new List<SpeckleTarget>();
            var addedReferenceGaiaNums = new HashSet<string>();
            foreach (var target in exportTargets) {
                if (target.Type == "G") {
                    targetsWithReferenceStars.Add(target);
                    continue;
                }
                await ResolveIfNeededAsync(target, targets, progress, ct).ConfigureAwait(false);
                targetsWithReferenceStars.Add(target);
                var referenceStar = target.ReferenceStar;
                if (referenceStar != null && referenceStar.RA2000 != 0) {
                    target.RefGaiaNum = referenceStar.GaiaNum;
                    if (string.IsNullOrWhiteSpace(referenceStar.GaiaNum) || addedReferenceGaiaNums.Add(referenceStar.GaiaNum)) {
                        var refStar = BuildReferenceStarRecord(target, referenceStar);
                        targetsWithReferenceStars.Add(refStar);
                        surfaced.Add(refStar);
                    }
                } else {
                    Logger.Debug($"No reference star to export for target {target.Name} (GaiaNum {target.GaiaNum}).");
                }
            }

            string csvfile = Path.Combine(directory, "TargetWithReferenceList-" + DateTime.Now.ToString("yyyy-MM-dd") + ".csv");
            using (var writer = new StreamWriter(csvfile))
            using (var csv = new CsvWriter(writer, CultureInfo.InvariantCulture)) {
                csv.Context.RegisterClassMap<SpeckleTargetMap>();
                csv.WriteRecords(targetsWithReferenceStars);
            }
            return new ReferenceStarExportResult { FilePath = csvfile, SurfacedReferenceRecords = surfaced };
        }

        private async Task ResolveIfNeededAsync(SpeckleTarget target, IReadOnlyList<SpeckleTarget> allTargets, IProgress<ApplicationStatus> progress, CancellationToken ct) {
            if (target.ReferenceStarList != null && target.ReferenceStarList.Any()) {
                return;
            }
            var result = await referenceStarService.ResolveAsync(target, allTargets, ReferenceStarQueryOptions.FromOptions(Options), progress, ct).ConfigureAwait(false);
            result.ApplyTo(target);
        }

        private static SpeckleTarget BuildReferenceStarRecord(SpeckleTarget target, ReferenceStar referenceStar) {
            var refStar = new SpeckleTarget(referenceStar);
            refStar.Type = "R";
            refStar.Proj = target.Proj;
            refStar.Obs = target.Obs;
            refStar.Exp = target.Exp;
            refStar.NExp = target.NExp;
            refStar.Name2 = refStar.Name1;
            refStar.Name1 = target.Name1 + "_ref";
            refStar.GetRef = 0;
            refStar.Template = "";
            refStar.ImageTime = target.ImageTime;
            return refStar;
        }
    }
}
