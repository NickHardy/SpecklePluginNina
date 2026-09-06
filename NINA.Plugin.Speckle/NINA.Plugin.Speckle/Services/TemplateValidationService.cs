using NINA.Core.Utility;
using NINA.Plugin.Speckle.Model;
using NINA.Sequencer.Interfaces.Mediator;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;

namespace NINA.Plugin.Speckle.Services {

    [Export(typeof(ITemplateValidationService))]
    [PartCreationPolicy(CreationPolicy.Shared)]
    public class TemplateValidationService : ITemplateValidationService {
        private readonly ISequenceMediator sequenceMediator;
        private readonly ITargetPlanner planner;
        private readonly TemplateFilterReader filterReader;

        [ImportingConstructor]
        public TemplateValidationService(ISequenceMediator sequenceMediator, ITargetPlanner planner) {
            this.sequenceMediator = sequenceMediator;
            this.planner = planner;
            filterReader = new TemplateFilterReader(sequenceMediator);
        }

        public IReadOnlyList<TemplateIssue> Validate(IEnumerable<SpeckleTarget> targets, TargetPlanDefaults defaults) {
            var issues = new List<TemplateIssue>();
            if (targets == null) {
                return issues;
            }
            var known = GetKnownTemplateNames();
            var planDefaults = defaults ?? new TargetPlanDefaults();
            foreach (var target in targets) {
                var issue = Check(target, planner.BuildPlan(target, false, planDefaults).TemplateName, false, known);
                if (issue != null) {
                    issues.Add(issue);
                }
                if (target.GetRef > 0 || ReferenceStarService.HasGaiaNumber(target.RefGaiaNum)) {
                    var refIssue = Check(target, planner.BuildPlan(target, true, planDefaults).TemplateName, true, known);
                    if (refIssue != null) {
                        issues.Add(refIssue);
                    }
                }
            }
            return issues;
        }

        public int FillFiltersFromTemplates(IEnumerable<SpeckleTarget> targets) {
            if (targets == null) {
                return 0;
            }
            filterReader.Forget();
            var filled = 0;
            foreach (var target in targets) {
                if (target == null || !string.IsNullOrWhiteSpace(target.Filter)) {
                    continue;
                }
                var entries = filterReader.ReadEntries(target.Template);
                var first = entries.FirstOrDefault(entry => !string.IsNullOrWhiteSpace(entry.Filter));
                if (first == null) {
                    continue;
                }
                target.Filter = first.Filter;
                if (target.Exp <= 0 && first.ExposureTime > 0) {
                    target.Exp = first.ExposureTime;
                }
                if (target.NExp <= 0 && first.FrameCount > 0) {
                    target.NExp = first.FrameCount;
                }
                filled++;
            }
            if (filled > 0) {
                Logger.Info("Filled the filter in for " + filled + " targets by reading it out of their sequence template");
            }
            return filled;
        }

        public TemplateIssue Inspect(SpeckleTarget target, TargetPlan plan) {
            if (target == null || plan == null) {
                return null;
            }
            return Check(target, plan.TemplateName, plan.IsReference, GetKnownTemplateNames());
        }

        private static TemplateIssue Check(SpeckleTarget target, string templateName, bool isReferenceLeg, HashSet<string> known) {
            if (target == null) {
                return null;
            }
            if (string.IsNullOrWhiteSpace(templateName)) {
                return new TemplateIssue {
                    Target = target,
                    TargetName = target.Name,
                    TemplateName = string.Empty,
                    IsReference = isReferenceLeg,
                    IsBlank = true
                };
            }
            if (known == null || known.Contains(templateName)) {
                return null;
            }
            return new TemplateIssue {
                Target = target,
                TargetName = target.Name,
                TemplateName = templateName,
                IsReference = isReferenceLeg,
                IsBlank = false
            };
        }

        private HashSet<string> GetKnownTemplateNames() {
            if (sequenceMediator?.Initialized != true) {
                return null;
            }
            try {
                return new HashSet<string>(
                    sequenceMediator.GetDeepSkyObjectContainerTemplates()
                        .Select(template => template?.Name)
                        .Where(name => !string.IsNullOrWhiteSpace(name)),
                    StringComparer.OrdinalIgnoreCase);
            } catch (Exception ex) {
                Logger.Error("Could not read the saved sequence templates", ex);
                return null;
            }
        }
    }
}
