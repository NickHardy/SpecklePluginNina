using CsvHelper;
using CsvHelper.Configuration;
using NINA.Astrometry;
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
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Plugin.Speckle.Services {

    [Export(typeof(IReferenceStarService))]
    [PartCreationPolicy(CreationPolicy.Shared)]
    public class ReferenceStarService : IReferenceStarService {
        private readonly IProfileService profileService;
        private readonly ISpeckleOptionsProvider optionsProvider;
        private readonly ISimbadUtils simbadUtils;
        private readonly SemaphoreSlim loadLock = new SemaphoreSlim(1, 1);
        private bool listFailureReported;
        private List<ReferenceStar> referenceStars = new List<ReferenceStar>();
        private List<GaiaReferenceStar> gaiaReferenceStars = new List<GaiaReferenceStar>();

        [ImportingConstructor]
        public ReferenceStarService(IProfileService profileService, ISpeckleOptionsProvider optionsProvider, ISimbadUtils simbadUtils) {
            this.profileService = profileService;
            this.optionsProvider = optionsProvider;
            this.simbadUtils = simbadUtils;
        }

        public IReadOnlyList<ReferenceStar> ReferenceStars => referenceStars;

        public IReadOnlyList<GaiaReferenceStar> GaiaReferenceStars => gaiaReferenceStars;

        public async Task EnsureReferenceListsLoadedAsync(CancellationToken ct) {
            var options = optionsProvider?.Current;
            if (options == null || !options.UseReferenceStarList) {
                Logger.Debug("The reference star list is switched off in the options, so it will not be loaded.");
                return;
            }
            if (string.IsNullOrWhiteSpace(options.ReferenceStarListLocation)) {
                Logger.Info("The reference star list is switched on but no file is set for it, so reference stars can only come from Simbad, "
                    + "the USNO single bright star list, or the target list itself.");
                return;
            }
            await loadLock.WaitAsync(ct).ConfigureAwait(false);
            try {
                if (referenceStars.Count > 0) {
                    return;
                }
                if (!File.Exists(options.ReferenceStarListLocation)) {
                    throw new FileNotFoundException("The reference star list file is not there: " + options.ReferenceStarListLocation,
                        options.ReferenceStarListLocation);
                }
                await Task.Run(() => {
                    var config = new CsvConfiguration(CultureInfo.InvariantCulture);
                    config.MissingFieldFound = null;
                    using (var reader = new StreamReader(options.ReferenceStarListLocation))
                    using (var csv = new CsvReader(reader, config)) {
                        csv.Context.RegisterClassMap<ReferenceStarMap>();
                        referenceStars = csv.GetRecords<ReferenceStar>().ToList();
                    }
                    Logger.Info("Reference star list loaded from " + options.ReferenceStarListLocation + ": " + referenceStars.Count
                        + " stars, " + referenceStars.Count(star => star.Gmag > 7 && star.Gmag < 10 && star.Rp > 7 && star.Rp < 10)
                        + " of them inside the usable magnitude band.");
                    if (!options.UseGaiaReferenceStarList || gaiaReferenceStars.Count > 0 || string.IsNullOrWhiteSpace(options.GaiaReferenceStarListLocation)) {
                        return;
                    }
                    using (var reader = new StreamReader(options.GaiaReferenceStarListLocation))
                    using (var csv = new CsvReader(reader, config)) {
                        csv.Context.RegisterClassMap<GaiaReferenceStarMap>();
                        gaiaReferenceStars = csv.GetRecords<GaiaReferenceStar>().ToList();
                    }
                    Logger.Info("Gaia reference star list loaded from " + options.GaiaReferenceStarListLocation + ": " + gaiaReferenceStars.Count + " stars.");
                }, ct).ConfigureAwait(false);
            } catch (OperationCanceledException) {
                throw;
            } catch (Exception ex) {
                Logger.Error("Failed to load reference star lists.", ex);
                if (!listFailureReported) {
                    listFailureReported = true;
                    Notification.ShowError("The reference star list could not be read, so reference stars can only come from Simbad, "
                        + "the USNO single bright star list, or the target list itself. " + ex.Message);
                }
            } finally {
                loadLock.Release();
            }
        }

        public async Task<ReferenceStarResult> ResolveAsync(SpeckleTarget target, IReadOnlyList<SpeckleTarget> allTargets, ReferenceStarQueryOptions opts, IProgress<ApplicationStatus> progress, CancellationToken ct) {
            await EnsureReferenceListsLoadedAsync(ct).ConfigureAwait(false);

            var candidates = new List<ReferenceStar>();
            string templateRefOverride = null;

            if (HasGaiaNumber(target.RefGaiaNum)) {
                ReferenceStar refTarget = null;
                var speckleRefTarget = allTargets.FirstOrDefault(candidate => SameGaiaStar(candidate.GaiaNum, target.RefGaiaNum));
                if (speckleRefTarget != null) {
                    refTarget = new ReferenceStar(speckleRefTarget);
                    if (!string.IsNullOrWhiteSpace(speckleRefTarget.Template)) {
                        templateRefOverride = speckleRefTarget.Template;
                    }
                }
                if (refTarget == null && referenceStars.Count > 0) {
                    refTarget = referenceStars.FirstOrDefault(candidate => SameGaiaStar(candidate.GaiaNum, target.RefGaiaNum));
                }
                if (refTarget != null) {
                    candidates.Add(refTarget);
                    Logger.Info("Reference star " + target.RefGaiaNum + " for " + TargetLabel.Of(target) + " was taken from the target list itself"
                        + (speckleRefTarget != null ? "" : " (from the reference star list file)") + ".");
                } else {
                    Logger.Warning("The target list names reference star " + target.RefGaiaNum + " for " + TargetLabel.Of(target)
                        + " but no row with that Gaia number is in the loaded lists, so a reference will have to be looked up instead.");
                }
            }

            var lookUpAReference = NeedsReferenceLookup(target, opts);
            Logger.Info("Resolving a reference star for " + TargetLabel.Of(target) + ": type " + (string.IsNullOrWhiteSpace(target.Type) ? "none" : target.Type)
                + ", GetRef " + target.GetRef + ", RefGaiaNum " + (string.IsNullOrWhiteSpace(target.RefGaiaNum) ? "none" : target.RefGaiaNum)
                + ", already resolved " + candidates.Count + " from the lists, look up " + lookUpAReference + ".");

            if (lookUpAReference && (target.GetRef > 0 || candidates.Count == 0)) {
                ReferenceStar targetStar = new ReferenceStar();
                double targetColor = target.Color != 0 ? target.Color : 0.65;
                double minMagnitude = target.Rp != 0 ? target.Rp - 1d : opts.MinReferenceMag;
                double maxMagnitude = opts.MaxReferenceMag;

                if (target.Color == 0) {
                    try {
                        var coords = target.Coordinates();
                        targetStar = await simbadUtils.GetStarByPosition(progress, ct, coords.RADegrees, coords.Dec, target.Pmag).ConfigureAwait(false);
                        if (targetStar == null) throw new Exception("Target star not found.");
                        targetColor = targetStar.color != 0 ? targetStar.color : 0.65;
                        minMagnitude = opts.MinReferenceMag > targetStar.Rp ? targetStar.Rp - 1d : opts.MinReferenceMag;
                        maxMagnitude = opts.MaxReferenceMag;
                        Logger.Info("Simbad identifies " + TargetLabel.Of(target) + " as " + targetStar.Name2 + " (V " + targetStar.Rp
                            + ", B-V " + Math.Round(targetColor, 2) + ").");
                    } catch (OperationCanceledException) when (ct.IsCancellationRequested) {
                        throw;
                    } catch (Exception ex) {
                        Logger.Info("Simbad could not identify " + TargetLabel.Of(target) + " at its coordinates, so a G-type star with B-V 0.65 is assumed. "
                            + ex.Message);
                    }
                }

                var targetMagnitude = TargetMagnitude(target, targetStar);

                double MagnitudeCloseness(ReferenceStar star) {
                    return Math.Round(Math.Abs(CatalogueMagnitude(star) - targetMagnitude), 1);
                }

                double SkyCloseness(ReferenceStar star) {
                    return Math.Round(Math.Abs(star.Dec2000 - target.Dec2000) + star.distance, 1);
                }

                Func<ReferenceStar, double> preferredRankingKey = opts.PreferBrighterReferenceStars ? MagnitudeCloseness : SkyCloseness;
                Func<ReferenceStar, double> fallbackRankingKey = opts.PreferBrighterReferenceStars ? SkyCloseness : MagnitudeCloseness;
                var refStarList = new List<ReferenceStar>();
                if (opts.UseSimbadRefStars) {
                    var fromSimbad = await simbadUtils.FindSimbadSaoStars(progress, ct, target.Coordinates(), opts.SearchRadius, minMagnitude, maxMagnitude).ConfigureAwait(false);
                    Logger.Info("Simbad returned " + fromSimbad.Count + " SAO stars within " + opts.SearchRadius + " degrees of " + TargetLabel.Of(target)
                        + " between magnitude " + Math.Round(minMagnitude, 2) + " and " + Math.Round(maxMagnitude, 2) + ".");
                    refStarList.AddRange(fromSimbad);
                } else {
                    Logger.Info("Simbad reference stars are switched off in the options.");
                }
                if (opts.UseUSNOSingleStarList) {
                    var fromUsno = await simbadUtils.FindSingleBrightStars(progress, ct, target.Coordinates(), opts.SearchRadius, minMagnitude, maxMagnitude).ConfigureAwait(false);
                    Logger.Info("The USNO single bright star list returned " + fromUsno.Count + " stars for " + TargetLabel.Of(target) + ".");
                    refStarList.AddRange(fromUsno);
                }
                if (opts.UseReferenceStarList && referenceStars.Count > 0) {
                    var targetCoordinates = target.Coordinates();
                    var fromList = referenceStars
                        .Where(star => Math.Abs(star.Dec2000 - target.Dec2000) <= opts.SearchRadius)
                        .Where(star => (targetCoordinates - star.Coordinates()).Distance.Degree <= opts.SearchRadius)
                        .ToList();
                    Logger.Info("The reference star list contributed " + fromList.Count + " stars near " + TargetLabel.Of(target) + ".");
                    refStarList.AddRange(fromList);
                }

                foreach (var rstar in refStarList) {
                    Separation sep = target.Coordinates() - rstar.Coordinates();
                    rstar.distance = sep.Distance.Degree;
                }

                var notTheTarget = refStarList
                    .Where(star => !SameGaiaStar(star.GaiaNum, target?.GaiaNum))
                    .Where(star => !SameGaiaStar(star.GaiaNum, target?.RefGaiaNum))
                    .ToList();
                var inBand = notTheTarget.Where(InUsableMagnitudeBand).ToList();
                var faintEnough = inBand
                    .Where(star => CatalogueMagnitude(star) - targetMagnitude > star.color - targetColor)
                    .ToList();
                Logger.Info("Reference candidates for " + TargetLabel.Of(target) + " (magnitude " + Math.Round(targetMagnitude, 2)
                    + ", B-V " + Math.Round(targetColor, 2) + "): " + refStarList.Count + " found, " + notTheTarget.Count
                    + " left after dropping the target itself, " + inBand.Count + " inside magnitude 7 to 10, " + faintEnough.Count
                    + " also fainter than the target once colour is allowed for.");

                var ranked = faintEnough.Count > 0 ? faintEnough : inBand;
                if (faintEnough.Count == 0 && inBand.Count > 0) {
                    Logger.Info("No candidate for " + TargetLabel.Of(target) + " was fainter than the target, so the colour test is dropped and the "
                        + inBand.Count + " stars inside the magnitude band are ranked instead.");
                }
                candidates.AddRange(ranked
                    .OrderBy(preferredRankingKey)
                    .ThenBy(fallbackRankingKey)
                    .Take(10));

                if (opts.DomePositionLock && target.DomeSlitObservationTime > 0) {
                    var slitAz1 = opts.DomePosition - (opts.DomeSlitWidth / 2);
                    var slitAz2 = opts.DomePosition + (opts.DomeSlitWidth / 2);
                    var observer = ObservingSite.Of(profileService);
                    var horizon = profileService.ActiveProfile.AstrometrySettings.Horizon;
                    foreach (var rstar in candidates) {
                        rstar.AltList = AltTimeCalculator.Compute(rstar.Coordinates(), observer, horizon, opts.DomePositionLock ? 0.01 : 0.05);
                        rstar.BuildDomeSlitAltTimeList(opts.AltitudeMin, opts.AltitudeMax, slitAz1, slitAz2);
                    }

                    var beforeTheSlit = candidates.Count;
                    candidates = candidates
                        .Where(star => star.DomeSlitAltTimeList != null && star.DomeSlitAltTimeList.Any())
                        .ToList();

                    var topObservationTime = candidates.Any() ? candidates.Max(star => star.DomeSlitObservationTime) * 0.7 : 0;
                    candidates = candidates
                        .Where(star => star.DomeSlitObservationTime >= topObservationTime)
                        .OrderBy(star => SameGaiaStar(star.GaiaNum, target.RefGaiaNum) ? 0 : 1)
                        .ThenBy(preferredRankingKey)
                        .ThenBy(fallbackRankingKey)
                        .ThenBy(star => star.DomeSlitAltTimeList.OrderBy(altTime => altTime.Timestamp).FirstOrDefault()?.Timestamp)
                        .ToList();
                    Logger.Info("The dome slit at azimuth " + opts.DomePosition + " and width " + opts.DomeSlitWidth + " leaves "
                        + candidates.Count + " of " + beforeTheSlit + " reference candidates for " + TargetLabel.Of(target) + ".");
                }

                if (!candidates.Any()) {
                    Logger.Warning("No reference star could be found for " + TargetLabel.Of(target) + " within " + opts.SearchRadius
                        + " degrees between magnitude " + Math.Round(minMagnitude, 2) + " and " + Math.Round(maxMagnitude, 2) + ".");
                }
            }

            var chosen = candidates.FirstOrDefault();
            if (chosen == null) {
                Logger.Warning("No reference star resolved for " + TargetLabel.Of(target) + " (RefGaiaNum " + target?.RefGaiaNum
                    + ", GetRef " + target?.GetRef + ", lookup " + lookUpAReference + ").");
            } else {
                Logger.Info("Reference star for " + TargetLabel.Of(target) + ": " + (chosen.Name ?? chosen.Name2) + " at "
                    + Math.Round(chosen.RA2000, 5) + " " + Math.Round(chosen.Dec2000, 5) + ", magnitude "
                    + Math.Round(CatalogueMagnitude(chosen), 2) + ", " + Math.Round(chosen.distance, 3) + " degrees away, chosen from "
                    + candidates.Count + " candidate" + (candidates.Count == 1 ? "" : "s") + ".");
            }
            return new ReferenceStarResult {
                Candidates = candidates,
                Chosen = chosen,
                TemplateRefOverride = templateRefOverride
            };
        }

        public static bool NeedsReferenceLookup(SpeckleTarget target, ReferenceStarQueryOptions opts) {
            if (target == null) {
                return false;
            }
            if (target.GetRef > 0) {
                return true;
            }
            return opts.LookupMissingReferenceStars
                && !string.Equals(target.Type, "R", StringComparison.OrdinalIgnoreCase)
                && !HasGaiaNumber(target.RefGaiaNum);
        }

        public static bool HasGaiaNumber(string gaiaNumber) {
            return !string.IsNullOrWhiteSpace(gaiaNumber) && gaiaNumber.Trim() != "0";
        }

        private static bool SameGaiaStar(string firstGaiaNumber, string secondGaiaNumber) {
            return HasGaiaNumber(firstGaiaNumber) && HasGaiaNumber(secondGaiaNumber)
                && string.Equals(firstGaiaNumber.Trim(), secondGaiaNumber.Trim(), StringComparison.Ordinal);
        }

        private static double CatalogueMagnitude(ReferenceStar star) {
            return star.Gmag != 0 ? star.Gmag : star.Rp;
        }

        private static double TargetMagnitude(SpeckleTarget target, ReferenceStar targetStar) {
            if (target.Gmag != 0) {
                return target.Gmag;
            }
            if (targetStar != null && targetStar.Rp != 0) {
                return targetStar.Rp;
            }
            if (target.Rp != 0) {
                return target.Rp;
            }
            return target.Pmag;
        }

        private static bool InUsableMagnitudeBand(ReferenceStar star) {
            if (star.Gmag == 0 && star.Rp == 0) {
                return false;
            }
            var gmagFits = star.Gmag == 0 || (star.Gmag > 7 && star.Gmag < 10);
            var rpFits = star.Rp == 0 || (star.Rp > 7 && star.Rp < 10);
            return gmagFits && rpFits;
        }

    }
}
