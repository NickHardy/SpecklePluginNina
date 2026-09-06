using NINA.Astrometry;
using NINA.Plugin.Speckle.Model;
using System;
using System.Linq;
using System.Collections.Generic;

namespace NINA.Plugin.Speckle.Services {

    public class TargetListDefaults {
        public string User { get; init; }
        public string Template { get; init; }
        public string TemplateRef { get; init; }
        public int Cycles { get; init; }
        public int Exposures { get; init; }
        public double ExposureTime { get; init; }
    }

    public class SchedulingOptions {
        public bool SortByRa { get; init; }
        public bool IgnoreLimits { get; init; }
        public bool DomePositionLock { get; init; }
        public double DomePosition { get; init; }
        public double DomeSlitWidth { get; init; }
        public double AltitudeMin { get; init; }
        public double AltitudeMax { get; init; }
        public double MDistance { get; init; }
        public double MoonDistance { get; init; }

        public static SchedulingOptions FromOptions(Speckle options, bool sortByRa = false, bool ignoreLimits = false) {
            return new SchedulingOptions {
                SortByRa = sortByRa,
                IgnoreLimits = ignoreLimits,
                DomePositionLock = options.DomePositionLock,
                DomePosition = options.DomePosition,
                DomeSlitWidth = options.DomeSlitWidth,
                AltitudeMin = options.AltitudeMin,
                AltitudeMax = options.AltitudeMax,
                MDistance = options.MDistance,
                MoonDistance = options.MoonDistance
            };
        }
    }

    public class ReferenceStarQueryOptions {
        public double MinReferenceMag { get; init; }
        public double MaxReferenceMag { get; init; }
        public double SearchRadius { get; init; }
        public bool UseSimbadRefStars { get; init; }
        public bool UseUSNOSingleStarList { get; init; }
        public bool LookupMissingReferenceStars { get; init; }
        public bool UseReferenceStarList { get; init; }
        public bool UseGaiaReferenceStarList { get; init; }
        public bool PreferBrighterReferenceStars { get; init; }
        public bool DomePositionLock { get; init; }
        public double DomePosition { get; init; }
        public double DomeSlitWidth { get; init; }
        public double AltitudeMin { get; init; }
        public double AltitudeMax { get; init; }

        public static ReferenceStarQueryOptions FromOptions(Speckle options) {
            return new ReferenceStarQueryOptions {
                MinReferenceMag = options.MinReferenceMag,
                MaxReferenceMag = options.MaxReferenceMag,
                SearchRadius = options.SearchRadius,
                UseSimbadRefStars = options.UseSimbadRefStars,
                UseUSNOSingleStarList = options.UseUSNOSingleStarList,
                LookupMissingReferenceStars = options.LookupMissingReferenceStars,
                UseReferenceStarList = options.UseReferenceStarList,
                UseGaiaReferenceStarList = options.UseGaiaReferenceStarList,
                PreferBrighterReferenceStars = options.PreferBrighterReferenceStars,
                DomePositionLock = options.DomePositionLock,
                DomePosition = options.DomePosition,
                DomeSlitWidth = options.DomeSlitWidth,
                AltitudeMin = options.AltitudeMin,
                AltitudeMax = options.AltitudeMax
            };
        }
    }

    public class ReferenceStarResult {
        public List<ReferenceStar> Candidates { get; init; } = new List<ReferenceStar>();
        public ReferenceStar Chosen { get; init; }
        public string TemplateRefOverride { get; init; }

        public void ApplyTo(SpeckleTarget target) {
            if (!string.IsNullOrWhiteSpace(TemplateRefOverride)) {
                target.TemplateRef = TemplateRefOverride;
            }
            target.ReferenceStarList = Candidates;
            target.ReferenceStar = Chosen;
        }
    }

    public class TargetPlanDefaults {
        public string DefaultTemplate { get; init; } = "";
        public string DefaultRefTemplate { get; init; } = "";
        public int ReferenceExposures { get; init; }
        public IReadOnlyList<string> DefaultFilters { get; init; } = Array.Empty<string>();

        public static TargetPlanDefaults FromOptions(Speckle options) {
            return new TargetPlanDefaults {
                DefaultTemplate = options.DefaultTemplate,
                DefaultRefTemplate = options.DefaultRefTemplate,
                ReferenceExposures = options.ReferenceExposures,
                DefaultFilters = SplitFilters(options.DefaultFilters)
            };
        }

        public static IReadOnlyList<string> SplitFilters(string value) {
            if (string.IsNullOrWhiteSpace(value)) {
                return Array.Empty<string>();
            }
            return value.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(entry => entry.Trim())
                .Where(entry => entry.Length > 0)
                .ToList();
        }
    }

    public class TargetPlan {
        public bool IsReference { get; init; }
        public string TemplateName { get; init; }
        public string ContainerName { get; init; }
        public string TargetName { get; init; }
        public string Title { get; init; }
        public Coordinates Coordinates { get; init; }
        public DateTime ImageTime { get; init; }
        public IReadOnlyList<CaptureEntry> CaptureEntries { get; init; } = new List<CaptureEntry>();

        public CaptureEntry PrimaryEntry => CaptureEntries != null && CaptureEntries.Count > 0 ? CaptureEntries[0] : null;

        public double Exp => PrimaryEntry?.ExposureTime ?? 0;

        public int NExp => PrimaryEntry?.FrameCount ?? 0;

        public string FilterName => PrimaryEntry?.Filter;
    }

    public class TemplateIssue {
        public SpeckleTarget Target { get; init; }
        public string TargetName { get; init; }
        public string TemplateName { get; init; }
        public bool IsReference { get; init; }
        public bool IsBlank { get; init; }

        public string Message {
            get {
                var kind = IsReference ? "reference template" : "template";
                return IsBlank
                    ? "Target " + TargetName + " has no " + kind + " name set"
                    : "Target " + TargetName + " uses " + kind + " " + TemplateName + " which is not a saved sequence template";
            }
        }
    }

    public enum ExportFormat {
        Json,
        Csv
    }

    public class ReferenceStarExportResult {
        public string FilePath { get; init; }
        public IReadOnlyList<SpeckleTarget> SurfacedReferenceRecords { get; init; } = new List<SpeckleTarget>();
    }
}
