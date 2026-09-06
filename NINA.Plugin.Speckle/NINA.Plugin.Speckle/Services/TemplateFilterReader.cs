using NINA.Core.Utility;
using NINA.Sequencer.Container;
using NINA.Sequencer.Interfaces.Mediator;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace NINA.Plugin.Speckle.Services {

    public class TemplateFilterReader {
        private readonly ISequenceMediator sequenceMediator;
        private readonly Dictionary<string, IReadOnlyList<CaptureEntry>> cache = new Dictionary<string, IReadOnlyList<CaptureEntry>>(StringComparer.OrdinalIgnoreCase);

        public TemplateFilterReader(ISequenceMediator sequenceMediator) {
            this.sequenceMediator = sequenceMediator;
        }

        public IReadOnlyList<CaptureEntry> ReadEntries(string templateName) {
            if (string.IsNullOrWhiteSpace(templateName) || sequenceMediator?.Initialized != true) {
                return Array.Empty<CaptureEntry>();
            }
            lock (cache) {
                if (cache.TryGetValue(templateName, out var cached)) {
                    return cached;
                }
            }
            var entries = Array.Empty<CaptureEntry>() as IReadOnlyList<CaptureEntry>;
            try {
                var template = sequenceMediator.GetDeepSkyObjectContainerTemplates()
                    .FirstOrDefault(candidate => string.Equals(candidate?.Name, templateName, StringComparison.OrdinalIgnoreCase));
                if (template != null) {
                    var found = new List<CaptureEntry>();
                    Walk(template, null, found, 0);
                    entries = found;
                    Logger.Info("Template " + templateName + " describes " + found.Count + " capture "
                        + (found.Count == 1 ? "entry" : "entries") + ": " + CapturePlan.Describe(found));
                }
            } catch (Exception ex) {
                Logger.Error("Could not read the capture settings out of template " + templateName, ex);
            }
            lock (cache) {
                cache[templateName] = entries;
            }
            return entries;
        }

        public void Forget() {
            lock (cache) {
                cache.Clear();
            }
        }

        private static void Walk(object node, string filterInScope, List<CaptureEntry> found, int depth) {
            if (node == null || depth > 12) {
                return;
            }
            var typeName = node.GetType().Name;
            if (typeName.IndexOf("SwitchFilter", StringComparison.OrdinalIgnoreCase) >= 0) {
                var name = ReadFilterName(node);
                if (!string.IsNullOrWhiteSpace(name)) {
                    filterInScope = name;
                }
            }
            if (LooksLikeAnExposureItem(typeName)) {
                found.Add(new CaptureEntry(filterInScope, ReadDouble(node, "ExposureTime"), ReadInt(node, "TotalExposureCount")));
            }
            if (node is ISequenceContainer container) {
                foreach (var child in container.Items ?? Enumerable.Empty<object>().Cast<dynamic>()) {
                    Walk(child, filterInScope, found, depth + 1);
                    if (child != null && child.GetType().Name.IndexOf("SwitchFilter", StringComparison.OrdinalIgnoreCase) >= 0) {
                        var name = ReadFilterName(child);
                        if (!string.IsNullOrWhiteSpace(name)) {
                            filterInScope = name;
                        }
                    }
                }
            } else {
                var items = node.GetType().GetProperty("Items", BindingFlags.Public | BindingFlags.Instance)?.GetValue(node) as IEnumerable;
                if (items != null) {
                    foreach (var child in items) {
                        Walk(child, filterInScope, found, depth + 1);
                    }
                }
            }
        }

        private static bool LooksLikeAnExposureItem(string typeName) {
            return typeName.IndexOf("TakeRoiExposures", StringComparison.OrdinalIgnoreCase) >= 0
                || typeName.IndexOf("TakeLiveExposures", StringComparison.OrdinalIgnoreCase) >= 0
                || typeName.IndexOf("TakeSingleRoiExposure", StringComparison.OrdinalIgnoreCase) >= 0
                || typeName.IndexOf("TakeSingleExposure", StringComparison.OrdinalIgnoreCase) >= 0
                || typeName.IndexOf("TakeExposure", StringComparison.OrdinalIgnoreCase) >= 0
                || typeName.IndexOf("TakeManyExposures", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string ReadFilterName(object node) {
            var filter = node.GetType().GetProperty("Filter", BindingFlags.Public | BindingFlags.Instance)?.GetValue(node);
            if (filter == null) {
                return null;
            }
            var name = filter.GetType().GetProperty("Name", BindingFlags.Public | BindingFlags.Instance)?.GetValue(filter) as string;
            return string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        }

        private static double ReadDouble(object node, string propertyName) {
            var value = node.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance)?.GetValue(node);
            return value == null ? 0 : Convert.ToDouble(value);
        }

        private static int ReadInt(object node, string propertyName) {
            var value = node.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance)?.GetValue(node);
            return value == null ? 0 : Convert.ToInt32(value);
        }
    }
}
