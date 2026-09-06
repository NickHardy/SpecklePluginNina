using NINA.Core.Model.Equipment;
using NINA.Profile.Interfaces;
using System.Collections.Generic;
using System.Linq;

namespace NINA.Plugin.Speckle.Services {

    public static class FilterWheelCatalog {

        public static IReadOnlyList<FilterInfo> Filters(IProfileService profileService) {
            var filters = profileService?.ActiveProfile?.FilterWheelSettings?.FilterWheelFilters;
            return filters == null ? new List<FilterInfo>() : filters.ToList();
        }

        public static IReadOnlyList<string> Names(IProfileService profileService) {
            return Filters(profileService)
                .Where(filter => !string.IsNullOrWhiteSpace(filter?.Name))
                .Select(filter => filter.Name)
                .ToList();
        }

        public static FilterInfo Find(IProfileService profileService, string name) {
            return string.IsNullOrWhiteSpace(name)
                ? null
                : Filters(profileService).FirstOrDefault(filter => filter.Name == name);
        }
    }
}
