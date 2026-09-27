using NINA.Core.Utility;
using Newtonsoft.Json;
using System;
using System.IO;

namespace NINA.Plugin.Speckle.Services {

    /// <summary>
    /// The physical two camera rig: which N.I.N.A. profile drives each light path, where the flip
    /// mirror sits, which camera is on which side, and how each camera is oriented.
    /// </summary>
    public sealed class RigSettings {
        public bool DualCameraSetup { get; set; }
        public string WideProfileId { get; set; } = string.Empty;
        public string ScienceProfileId { get; set; } = string.Empty;
        public int WideMirrorPosition { get; set; }
        public int ScienceMirrorPosition { get; set; }
        public string WideCameraName { get; set; } = string.Empty;
        public string ScienceCameraName { get; set; } = string.Empty;
        public string WideCameraId { get; set; } = string.Empty;
        public string ScienceCameraId { get; set; } = string.Empty;
        public double WideCompassAngle { get; set; }
        public double ScienceCompassAngle { get; set; }
        public bool WideCompassMirrored { get; set; }
        public bool ScienceCompassMirrored { get; set; } = true;
    }

    /// <summary>
    /// Stores <see cref="RigSettings"/> outside the N.I.N.A. profile.
    /// <para>
    /// Every other plugin option is deliberately per profile, but the rig is not: the wide and
    /// science profile ids exist in order to switch between profiles, so keeping them inside a
    /// profile means the setup has to be duplicated into each one and hand kept in sync. Storing it
    /// once removes that, and makes a half configured profile impossible.
    /// </para>
    /// </summary>
    public static class RigStore {
        private static readonly object gate = new object();
        private static RigSettings current;

        public static string FilePath { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NINA", "SpeckleInterferometry", "rig.json");

        public static bool Exists => File.Exists(FilePath);

        public static RigSettings Current {
            get {
                lock (gate) {
                    return current ??= Load();
                }
            }
        }

        /// <summary>Replaces the stored rig, used once to carry a per profile setup over.</summary>
        public static void Seed(RigSettings seed) {
            lock (gate) {
                current = seed ?? new RigSettings();
                SaveLocked();
            }
        }

        public static void Save() {
            lock (gate) {
                SaveLocked();
            }
        }

        private static RigSettings Load() {
            try {
                if (!File.Exists(FilePath)) {
                    return new RigSettings();
                }
                var json = File.ReadAllText(FilePath);
                return JsonConvert.DeserializeObject<RigSettings>(json) ?? new RigSettings();
            } catch (Exception ex) {
                Logger.Error("The Speckle two camera rig could not be read from " + FilePath
                    + "; starting from defaults so the settings page still works", ex);
                return new RigSettings();
            }
        }

        private static void SaveLocked() {
            try {
                var folder = Path.GetDirectoryName(FilePath);
                if (!string.IsNullOrEmpty(folder)) {
                    Directory.CreateDirectory(folder);
                }
                File.WriteAllText(FilePath, JsonConvert.SerializeObject(current, Formatting.Indented));
            } catch (Exception ex) {
                Logger.Error("The Speckle two camera rig could not be written to " + FilePath
                    + "; the change will be lost when N.I.N.A. closes", ex);
            }
        }
    }
}
