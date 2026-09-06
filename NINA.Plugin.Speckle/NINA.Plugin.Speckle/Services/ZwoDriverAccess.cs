using NINA.Core.Utility;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace NINA.Plugin.Speckle.Services {

    public class ZwoDriverAccess {
        private readonly Type driverType;
        private readonly int cameraId;
        private readonly string sdkVersion;

        private ZwoDriverAccess(Type driverType, int cameraId, string sdkVersion) {
            this.driverType = driverType;
            this.cameraId = cameraId;
            this.sdkVersion = sdkVersion;
        }

        public int CameraId => cameraId;

        public string SdkVersion => sdkVersion;

        public static ZwoDriverAccess Resolve(string cameraName) {
            try {
                var driverType = AppDomain.CurrentDomain.GetAssemblies()
                    .SelectMany(SafeTypes)
                    .FirstOrDefault(type => type.Name == "ASICameraDll" && type.Namespace == "ZWOptical.ASISDK");
                if (driverType == null) {
                    return null;
                }
                var version = ReadSdkVersion(driverType);
                var identifier = FindCameraId(driverType, cameraName);
                if (identifier < 0) {
                    Logger.Info("BENCHMARK could not match " + cameraName + " to a connected ZWO camera");
                    return null;
                }
                return new ZwoDriverAccess(driverType, identifier, version);
            } catch (Exception ex) {
                Logger.Warning("BENCHMARK could not reach the ZWO driver: " + ex.Message);
                return null;
            }
        }

        public int ReadDroppedFrames() {
            try {
                var method = driverType.GetMethod("ASIGetDroppedFrames", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (method == null) {
                    return -1;
                }
                var arguments = new object[] { cameraId, 0 };
                method.Invoke(null, arguments);
                return arguments[1] is int dropped ? dropped : -1;
            } catch {
                return -1;
            }
        }

        public IEnumerable<DriverProbe> BuildProbes() {
            var probes = new List<DriverProbe>();
            var roiFormat = ReadRoiFormat();
            var startPosition = ReadStartPosition();

            probes.Add(new DriverProbe("ASIGetROIFormat", false, () => ReadRoiFormat()));
            probes.Add(new DriverProbe("ASIGetStartPos", false, () => ReadStartPosition()));
            probes.Add(new DriverProbe("ASIGetExpStatus", false, () => Invoke("ASIGetExpStatus", new object[] { cameraId, Default("ASI_EXPOSURE_STATUS") })));
            probes.Add(new DriverProbe("GetCameraProperties", false, () => Invoke("GetCameraProperties", new object[] { 0 })));
            probes.Add(new DriverProbe("ASIGetSDKVersion", false, () => Invoke("ASIGetSDKVersion", Array.Empty<object>())));

            if (roiFormat != null) {
                probes.Add(new DriverProbe("ASISetROIFormat (same values)", true, () =>
                    Invoke("ASISetROIFormat", new object[] { cameraId, roiFormat[0], roiFormat[1], roiFormat[2], roiFormat[3] })));
            }
            if (startPosition != null) {
                probes.Add(new DriverProbe("ASISetStartPos (same values)", true, () =>
                    Invoke("ASISetStartPos", new object[] { cameraId, startPosition[0], startPosition[1] })));
                probes.Add(new DriverProbe("ASISetStartPos (moved by 8 px)", true, () => {
                    var moved = Convert.ToInt32(startPosition[0]) + 8;
                    Invoke("ASISetStartPos", new object[] { cameraId, moved, startPosition[1] });
                    Invoke("ASISetStartPos", new object[] { cameraId, startPosition[0], startPosition[1] });
                }));
            }
            if (roiFormat != null) {
                probes.Add(new DriverProbe("ASISetROIFormat (half the width)", true, () => {
                    var halfWidth = Math.Max(8, Convert.ToInt32(roiFormat[0]) / 2 / 8 * 8);
                    Invoke("ASISetROIFormat", new object[] { cameraId, halfWidth, roiFormat[1], roiFormat[2], roiFormat[3] });
                    Invoke("ASISetROIFormat", new object[] { cameraId, roiFormat[0], roiFormat[1], roiFormat[2], roiFormat[3] });
                }));
            }
            probes.Add(new DriverProbe("ASIGetDroppedFrames", false, () => ReadDroppedFrames()));
            AddControlProbes(probes);
            return probes;
        }

        private void AddControlProbes(List<DriverProbe> probes) {
            var controlType = driverType.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault(candidate => candidate.Name == "ASI_CONTROL_TYPE");
            var boolType = driverType.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault(candidate => candidate.Name == "ASI_BOOL");
            if (controlType == null || boolType == null) {
                return;
            }
            foreach (var controlName in new[] { "ASI_EXPOSURE", "ASI_GAIN", "ASI_OFFSET", "ASI_BANDWIDTHOVERLOAD", "ASI_HIGH_SPEED_MODE" }) {
                if (!Enum.IsDefined(controlType, controlName)) {
                    continue;
                }
                var control = Enum.Parse(controlType, controlName);
                var falseValue = Enum.Parse(boolType, "ASI_FALSE");
                int currentValue;
                try {
                    var arguments = new object[] { cameraId, control, 0, Activator.CreateInstance(boolType) };
                    Invoke("ASIGetControlValue", arguments);
                    currentValue = Convert.ToInt32(arguments[2]);
                } catch {
                    continue;
                }
                probes.Add(new DriverProbe("ASIGetControlValue " + controlName, false, () =>
                    Invoke("ASIGetControlValue", new object[] { cameraId, control, 0, Activator.CreateInstance(boolType) })));
                probes.Add(new DriverProbe("ASISetControlValue " + controlName + " (same value)", true, () =>
                    Invoke("ASISetControlValue", new object[] { cameraId, control, currentValue, falseValue })));
            }
        }

        private object[] ReadRoiFormat() {
            var method = driverType.GetMethod("ASIGetROIFormat", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (method == null) {
                return null;
            }
            var arguments = new object[] { cameraId, 0, 0, 0, Default("ASI_IMG_TYPE") };
            method.Invoke(null, arguments);
            return new[] { arguments[1], arguments[2], arguments[3], arguments[4] };
        }

        private object[] ReadStartPosition() {
            var method = driverType.GetMethod("ASIGetStartPos", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (method == null) {
                return null;
            }
            var arguments = new object[] { cameraId, 0, 0 };
            method.Invoke(null, arguments);
            return new[] { arguments[1], arguments[2] };
        }

        private object Invoke(string name, object[] arguments) {
            var method = driverType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                .FirstOrDefault(candidate => candidate.Name == name && candidate.GetParameters().Length == arguments.Length);
            if (method == null) {
                throw new MissingMethodException(driverType.Name, name);
            }
            return method.Invoke(null, arguments);
        }

        private object Default(string typeName) {
            var type = driverType.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault(candidate => candidate.Name == typeName);
            return type == null ? null : Activator.CreateInstance(type);
        }

        private static string ReadSdkVersion(Type driverType) {
            try {
                var method = driverType.GetMethod("ASIGetSDKVersion", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                var pointer = method?.Invoke(null, Array.Empty<object>());
                return pointer is IntPtr handle && handle != IntPtr.Zero
                    ? System.Runtime.InteropServices.Marshal.PtrToStringAnsi(handle)
                    : "unknown";
            } catch {
                return "unknown";
            }
        }

        private static int FindCameraId(Type driverType, string cameraName) {
            var countMethod = driverType.GetMethod("ASIGetNumOfConnectedCameras", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            var propertiesMethod = driverType.GetMethod("GetCameraProperties", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (countMethod == null || propertiesMethod == null) {
                return -1;
            }
            var count = (int)countMethod.Invoke(null, Array.Empty<object>());
            var fallback = -1;
            for (var index = 0; index < count; index++) {
                var properties = propertiesMethod.Invoke(null, new object[] { index });
                var identifier = ReadMember(properties, "CameraID");
                var name = ReadMember(properties, "Name") as string;
                if (identifier is int cameraIdentifier) {
                    if (fallback < 0) {
                        fallback = cameraIdentifier;
                    }
                    if (!string.IsNullOrWhiteSpace(cameraName) && !string.IsNullOrWhiteSpace(name)
                        && cameraName.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0) {
                        return cameraIdentifier;
                    }
                }
            }
            return count == 1 ? fallback : -1;
        }

        private static object ReadMember(object instance, string name) {
            if (instance == null) {
                return null;
            }
            var type = instance.GetType();
            var field = type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (field != null) {
                return field.GetValue(instance);
            }
            var property = type.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            return property?.GetValue(instance);
        }

        private static IEnumerable<Type> SafeTypes(Assembly assembly) {
            try {
                return assembly.GetTypes();
            } catch {
                return Array.Empty<Type>();
            }
        }

        public class DriverProbe {

            public DriverProbe(string name, bool writes, Action action) {
                Name = name;
                Writes = writes;
                Action = action;
            }

            public string Name { get; }

            public bool Writes { get; }

            private Action Action { get; }

            public void Invoke() {
                Action();
            }
        }
    }
}
