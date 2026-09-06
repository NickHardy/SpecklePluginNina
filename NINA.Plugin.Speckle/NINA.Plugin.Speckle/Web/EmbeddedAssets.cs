using NINA.Core.Utility;
using System;
using System.IO;
using System.Text;

namespace NINA.Plugin.Speckle.Web {

    internal static class EmbeddedAssets {
        private static readonly Lazy<byte[]> background = new Lazy<byte[]>(() => Load("SpeckleOperator.jpg"));
        private static readonly Lazy<string> page = new Lazy<string>(() => Encoding.UTF8.GetString(Load("SpeckleOperator.html")));

        public static byte[] BackgroundJpg => background.Value;

        public static string OperatorHtml => page.Value;

        private static byte[] Load(string logicalName) {
            try {
                using var stream = typeof(EmbeddedAssets).Assembly.GetManifestResourceStream(logicalName);
                if (stream == null) {
                    Logger.Error("Speckle operator page asset " + logicalName + " is missing from the plugin assembly");
                    return Array.Empty<byte>();
                }
                using var buffer = new MemoryStream();
                stream.CopyTo(buffer);
                return buffer.ToArray();
            } catch (Exception ex) {
                Logger.Error("Speckle operator page asset " + logicalName + " could not be loaded", ex);
                return Array.Empty<byte>();
            }
        }
    }
}
