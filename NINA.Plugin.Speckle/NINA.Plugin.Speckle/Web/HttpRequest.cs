using System.Collections.Generic;

namespace NINA.Plugin.Speckle.Web {

    internal readonly struct HttpRequest {
        public string Method { get; init; }
        public string Path { get; init; }
        public IDictionary<string, string> Query { get; init; }
        public IDictionary<string, string> Headers { get; init; }

        public string Header(string name) {
            return Headers != null && Headers.TryGetValue(name, out var value) ? value : null;
        }
    }
}
