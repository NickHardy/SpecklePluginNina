using System;
using System.Text;

namespace NINA.Plugin.Speckle.Web {

    internal readonly struct HttpReply {
        public int Status { get; init; }
        public string Reason { get; init; }
        public string ContentType { get; init; }
        public string CacheControl { get; init; }
        public byte[] Body { get; init; }

        public static HttpReply Html(string html) => new HttpReply {
            Status = 200,
            Reason = "OK",
            ContentType = "text/html; charset=utf-8",
            CacheControl = "no-store",
            Body = Encoding.UTF8.GetBytes(html ?? string.Empty)
        };

        public static HttpReply Json(string json) => new HttpReply {
            Status = 200,
            Reason = "OK",
            ContentType = "application/json; charset=utf-8",
            CacheControl = "no-store",
            Body = Encoding.UTF8.GetBytes(json ?? "{}")
        };

        public static HttpReply Bytes(byte[] body, string contentType, string cacheControl) => new HttpReply {
            Status = 200,
            Reason = "OK",
            ContentType = contentType,
            CacheControl = cacheControl,
            Body = body ?? Array.Empty<byte>()
        };

        public static HttpReply Text(int status, string text) => new HttpReply {
            Status = status,
            Reason = ReasonFor(status),
            ContentType = "text/plain; charset=utf-8",
            CacheControl = "no-store",
            Body = Encoding.UTF8.GetBytes(text ?? string.Empty)
        };

        public static HttpReply Empty(int status) => new HttpReply {
            Status = status,
            Reason = ReasonFor(status),
            ContentType = "text/plain; charset=utf-8",
            CacheControl = "no-store",
            Body = Array.Empty<byte>()
        };

        private static string ReasonFor(int status) {
            switch (status) {
                case 200: return "OK";
                case 204: return "No Content";
                case 400: return "Bad Request";
                case 403: return "Forbidden";
                case 404: return "Not Found";
                case 405: return "Method Not Allowed";
                case 500: return "Internal Server Error";
                default: return "Error";
            }
        }
    }
}
