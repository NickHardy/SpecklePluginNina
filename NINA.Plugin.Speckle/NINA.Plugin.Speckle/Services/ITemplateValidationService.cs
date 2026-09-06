using NINA.Plugin.Speckle.Model;
using System.Collections.Generic;

namespace NINA.Plugin.Speckle.Services {

    public interface ITemplateValidationService {

        IReadOnlyList<TemplateIssue> Validate(IEnumerable<SpeckleTarget> targets, TargetPlanDefaults defaults);

        TemplateIssue Inspect(SpeckleTarget target, TargetPlan plan);

        int FillFiltersFromTemplates(IEnumerable<SpeckleTarget> targets);
    }
}
