using NINA.Plugin.Speckle.Model;
using System.Collections.Generic;
using System.Linq;
using System.ComponentModel.Composition;

namespace NINA.Plugin.Speckle.Services {

    [Export(typeof(ITargetPlanner))]
    [PartCreationPolicy(CreationPolicy.Shared)]
    public class TargetPlanner : ITargetPlanner {

        [ImportingConstructor]
        public TargetPlanner() {
        }

        public TargetPlan BuildPlan(SpeckleTarget target, bool isReferenceLeg, Speckle options) {
            return BuildPlan(target, isReferenceLeg, TargetPlanDefaults.FromOptions(options));
        }

        public TargetPlan BuildPlan(SpeckleTarget target, bool isReferenceLeg, TargetPlanDefaults defaults) {
            var cycle = target.Completed_cycles + 1;
            var targetTemplateName = string.IsNullOrWhiteSpace(target.Template) ? defaults.DefaultTemplate : target.Template;
            if (!isReferenceLeg) {
                return new TargetPlan {
                    IsReference = false,
                    TemplateName = targetTemplateName,
                    TargetName = target.Name + "_c" + cycle,
                    ContainerName = target.Proj + "_" + target.Obs + "_" + target.Name + "_c" + cycle,
                    Title = target.Obs,
                    Coordinates = target.Coordinates(),
                    ImageTime = target.ImageTime,
                    CaptureEntries = BuildEntries(target, defaults, target.NExp)
                };
            }

            var refTemplateName = string.IsNullOrWhiteSpace(target.TemplateRef) ? defaults.DefaultRefTemplate : target.TemplateRef;
            var refStarTemplate = string.IsNullOrWhiteSpace(target.ReferenceStar?.Template) ? refTemplateName : target.ReferenceStar?.Template;
            return new TargetPlan {
                IsReference = true,
                TemplateName = string.IsNullOrWhiteSpace(refStarTemplate) ? targetTemplateName : refStarTemplate,
                TargetName = target.Name2 + "_c" + cycle + "_ref_" + target.ReferenceStar?.Name,
                ContainerName = target.Proj + "_" + target.Obs + "_" + target.Name2 + "_" + cycle + "_ref_" + target.ReferenceStar?.Name,
                Title = target.Obs,
                Coordinates = target.ReferenceStar?.Coordinates(),
                ImageTime = target.ImageTime,
                CaptureEntries = BuildEntries(target, defaults,
                    defaults.ReferenceExposures > 0 ? defaults.ReferenceExposures : target.NExp)
            };
        }

        private static IReadOnlyList<CaptureEntry> BuildEntries(SpeckleTarget target, TargetPlanDefaults defaults, int frameCount) {
            if (!string.IsNullOrWhiteSpace(target.Filter) || defaults.DefaultFilters == null || defaults.DefaultFilters.Count == 0) {
                return CapturePlan.Single(target.Filter, target.Exp, frameCount);
            }
            return defaults.DefaultFilters
                .Select(filter => new CaptureEntry(filter, target.Exp, frameCount))
                .ToList();
        }
    }
}
