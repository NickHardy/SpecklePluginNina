using Newtonsoft.Json;
using NINA.Core.Utility;
using NINA.Plugin.Speckle.Model;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;

namespace NINA.Plugin.Speckle.Services {

    [Export(typeof(ITargetSchedulerService))]
    [PartCreationPolicy(CreationPolicy.Shared)]
    public class TargetSchedulerService : ITargetSchedulerService {
        private SpeckleTarget pinnedTarget;

        [ImportingConstructor]
        public TargetSchedulerService() {
        }

        public void PromoteTarget(SpeckleTarget target) {
            pinnedTarget = target;
        }

        public SpeckleTarget PickNextTarget(IReadOnlyList<SpeckleTarget> targets, SchedulingOptions opts, double? currentRa, DateTime now) {
            var pinned = pinnedTarget;
            pinnedTarget = null;
            if (pinned != null && targets.Contains(pinned) && pinned.ImageTarget && SchedulingRules.HasCyclesLeft(pinned)) {
                Logger.Debug("Using promoted target " + pinned.Name);
                if (pinned.ImageTime > now) {
                    pinned.ImageTime = now;
                }
                return pinned;
            }

            Logger.Debug("Getting next target from list: " + targets.Count + " targets total.");
            var quarterAgo = now.AddMinutes(-15);
            SpeckleTarget next = null;

            if (opts.SortByRa) {
                var ra = currentRa ?? 0;
                var target = targets.Where(candidate => candidate.ImageTarget && SchedulingRules.IsImagedType(candidate))
                    .Where(SchedulingRules.HasCyclesLeft)
                    .Where(candidate => candidate.ImagedAt == null || candidate.ImagedAt < quarterAgo)
                    .Where(candidate => candidate.RA2000 > ra)
                    .OrderBy(candidate => candidate.RA2000)
                    .FirstOrDefault();

                if (target != null) {
                    return target;
                }
            }
            if (opts.DomePositionLock) {
                var candidates = targets.Where(candidate => candidate.ImageTarget && SchedulingRules.IsImagedType(candidate))
                    .Where(SchedulingRules.HasCyclesLeft)
                    .Where(candidate => candidate.ImagedAt == null || candidate.ImagedAt < quarterAgo)
                    .Where(candidate => opts.IgnoreLimits || (candidate.DomeSlitAltTimeList.Count > 0 && candidate.DomeSlitObservationStartTime > now))
                    .OrderBy(candidate => candidate.DomeSlitObservationStartTime);

                if (candidates.Count() > 0) {
                    Logger.Debug(JsonConvert.SerializeObject(candidates, Formatting.Indented));
                    next = candidates.First();
                    var altTime = next.CurrentDomeAltTime();
                    next.ImageTime = altTime?.Timestamp ?? now;
                } else {
                    return null;
                }
            } else {
                DateTime maxImageTime = now.AddMinutes(-5);
                next = targets.Where(candidate => candidate.ImageTarget && SchedulingRules.IsImagedType(candidate))
                    .Where(SchedulingRules.HasCyclesLeft)
                    .Where(candidate => opts.IgnoreLimits || candidate.ImageTime > maxImageTime)
                    .Where(candidate => candidate.ImagedAt == null || candidate.ImagedAt < quarterAgo)
                    .OrderBy(candidate => candidate.Completed_cycles)
                    .ThenBy(candidate => candidate.ImageTime)
                    .FirstOrDefault();

                if (next == null && !opts.IgnoreLimits) {
                    Logger.Debug("No next target. Looking for previous target.");
                    var fillinTarget = targets.Where(candidate => candidate.ImageTarget && SchedulingRules.IsImagedType(candidate))
                        .Where(SchedulingRules.HasCyclesLeft)
                        .Where(candidate => candidate.CurrentAltTime(opts.AltitudeMax, opts.MDistance) != null
                            && candidate.CurrentAltTime(opts.AltitudeMax, opts.MDistance).Altitude > candidate.MinAltitude
                            && candidate.CurrentAltTime(opts.AltitudeMax, opts.MDistance).DistanceToMoon > opts.MoonDistance)
                        .OrderBy(candidate => candidate.CurrentAltTime(opts.AltitudeMax, opts.MDistance).Altitude)
                        .FirstOrDefault();
                    if (fillinTarget != null) {
                        Logger.Debug("Getting fillin target " + fillinTarget.Name2);
                        next = fillinTarget;
                    }
                }
            }

            if (next == null) {
                return null;
            }
            if (opts.IgnoreLimits && next.ImageTime > now) {
                next.ImageTime = now;
            }
            if (next.ImageTime > now && next.ImageTime < now.AddMinutes(5)) {
                next.ImageTime = now;
            }
            return next;
        }

        public void MarkCycleComplete(SpeckleTarget target, DateTime imagedAt) {
            target.ImagedAt = imagedAt;
            target.Completed_cycles += 1;
            if (target.Completed_cycles == target.Cycles) {
                target.Completed_nights += 1;
            }
        }
    }
}
