using NINA.Core.Utility;
using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;

namespace NINA.Plugin.Speckle.Services {

    public static class FringeDemoData {
        public const string Label = "WDS 17533+2459 R (demo)";
        public const double ArcsecPerPixel = 0.025000206305202756;
        public const string ScaleSource = "demo data";
        public const double KSpaceRadiusPxPer128 = 23.0;

        private const string ResourceMarker = ".Demo.wds17533.spk";
        private const string Magic = "SPKDEMO1";

        public static int Size { get; private set; }

        public static double KSpaceRadiusPx => KSpaceRadiusPxPer128 * Size / 128.0;

        public static ushort[][] Load() {
            var assembly = Assembly.GetExecutingAssembly();
            var resourceName = assembly.GetManifestResourceNames()
                .FirstOrDefault(resourceName => resourceName.EndsWith(ResourceMarker, StringComparison.OrdinalIgnoreCase));
            if (resourceName == null) {
                Logger.Error("Fringe demo data is missing from the plugin assembly");
                return Array.Empty<ushort[]>();
            }
            using (var stream = assembly.GetManifestResourceStream(resourceName))
            using (var reader = new BinaryReader(stream, Encoding.ASCII, true)) {
                var magic = Encoding.ASCII.GetString(reader.ReadBytes(Magic.Length));
                if (magic != Magic) {
                    Logger.Error("Fringe demo data has an unexpected header: " + magic);
                    return Array.Empty<ushort[]>();
                }
                var frameCount = reader.ReadInt32();
                var size = reader.ReadInt32();
                var pixelsPerFrame = size * size;
                var total = frameCount * pixelsPerFrame;
                var split = new byte[total * sizeof(ushort)];
                using (var deflate = new DeflateStream(stream, CompressionMode.Decompress)) {
                    var read = 0;
                    while (read < split.Length) {
                        var got = deflate.Read(split, read, split.Length - read);
                        if (got <= 0) {
                            Logger.Error("Fringe demo data ended early after " + read + " of " + split.Length + " bytes");
                            return Array.Empty<ushort[]>();
                        }
                        read += got;
                    }
                }
                var frames = new ushort[frameCount][];
                for (var f = 0; f < frameCount; f++) {
                    var frame = new ushort[pixelsPerFrame];
                    var offset = f * pixelsPerFrame;
                    for (var i = 0; i < pixelsPerFrame; i++) {
                        frame[i] = (ushort)(split[offset + i] | (split[total + offset + i] << 8));
                    }
                    frames[f] = frame;
                }
                Size = size;
                Logger.Info("Fringe demo data loaded: " + frameCount + " frames of " + size + "x" + size);
                return frames;
            }
        }
    }
}
