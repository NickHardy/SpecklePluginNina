using NINA.Profile.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NINA.Plugin.Speckle.Services {

    /// <summary>
    /// One entry in a profile dropdown. Id is the profile guid as a string because that is what
    /// the light path options store; an empty Id means "stay on the current profile".
    /// </summary>
    public class ProfileChoice {

        public ProfileChoice(string id, string name) {
            Id = id;
            Name = name;
        }

        public string Id { get; }

        public string Name { get; }
    }

    public static class ProfileCatalog {
        public const string StayOnCurrent = "Stay on the current profile";

        /// <summary>
        /// The profiles offered for the wide field and science light paths, newest first, with a
        /// leading "stay put" entry. <paramref name="selectedId"/> is kept in the list even when it
        /// no longer resolves, so a stale guid shows up instead of leaving the dropdown blank.
        /// </summary>
        /// <summary>
        /// Identifies the contents a dropdown would have. A WPF ComboBox drops its selection when
        /// its ItemsSource is replaced, so the caller must hand back the same list instance until
        /// this signature actually changes - switching between existing profiles must not rebuild it.
        /// </summary>
        public static string Signature(IProfileService profileService, string selectedId) {
            var profiles = profileService?.Profiles;
            var ids = profiles == null
                ? string.Empty
                : string.Join("|", profiles.OrderBy(p => p.Name).Select(p => p.Id + ":" + p.Name));
            // The selection only affects the contents when it does not resolve, because that is the
            // case that appends an "Unknown profile" entry. Choosing a real profile must leave the
            // signature alone, otherwise the list would be rebuilt and the ComboBox would clear the
            // selection the user just made.
            var resolves = string.IsNullOrWhiteSpace(selectedId)
                || (profiles != null && profiles.Any(p =>
                        string.Equals(p.Id.ToString(), selectedId, StringComparison.OrdinalIgnoreCase)));
            return resolves ? ids : ids + "#" + selectedId;
        }

        public static IReadOnlyList<ProfileChoice> Choices(IProfileService profileService, string selectedId) {
            var choices = new List<ProfileChoice> { new ProfileChoice(string.Empty, StayOnCurrent) };
            var profiles = profileService?.Profiles;
            if (profiles != null) {
                choices.AddRange(profiles
                    .OrderBy(profile => profile.Name)
                    .Select(profile => new ProfileChoice(profile.Id.ToString(), profile.Name)));
            }
            if (!string.IsNullOrWhiteSpace(selectedId)
                && !choices.Any(choice => string.Equals(choice.Id, selectedId, System.StringComparison.OrdinalIgnoreCase))) {
                choices.Add(new ProfileChoice(selectedId, "Unknown profile " + selectedId));
            }
            return choices;
        }
    }
}
