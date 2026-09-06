using NINA.Core.Utility;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Plugin.Speckle.Web {

    internal sealed class MicroHttpServer : IDisposable {
        private const int HeadLimit = 16 * 1024;
        private const int PortProbeRange = 20;
        private const int MaxConnections = 32;
        private static readonly TimeSpan ConnectionTimeout = TimeSpan.FromSeconds(15);

        private readonly Func<HttpRequest, HttpReply> route;
        private readonly SemaphoreSlim connections = new SemaphoreSlim(MaxConnections, MaxConnections);
        private readonly object sync = new object();
        private TcpListener listener;
        private CancellationTokenSource cts;

        public MicroHttpServer(Func<HttpRequest, HttpReply> route) {
            this.route = route ?? throw new ArgumentNullException(nameof(route));
        }

        public int Port { get; private set; }

        public bool IsRunning { get; private set; }

        public void Start(int preferredPort, bool lanVisible) {
            TcpListener started;
            lock (sync) {
                if (IsRunning) {
                    return;
                }
                var address = lanVisible ? IPAddress.Any : IPAddress.Loopback;
                started = Bind(address, preferredPort);
                listener = started;
                Port = ((IPEndPoint)started.LocalEndpoint).Port;
                cts = new CancellationTokenSource();
                IsRunning = true;
                var token = cts.Token;
                _ = Task.Run(() => AcceptLoopAsync(started, token), CancellationToken.None);
            }
            Logger.Info("Speckle operator page listening on " + (lanVisible ? "0.0.0.0" : "127.0.0.1") + ":"
                + Port.ToString(CultureInfo.InvariantCulture));
        }

        public void Stop() {
            CancellationTokenSource stopping;
            TcpListener stopped;
            lock (sync) {
                if (!IsRunning) {
                    return;
                }
                IsRunning = false;
                stopping = cts;
                stopped = listener;
                cts = null;
                listener = null;
            }
            try { stopping?.Cancel(); } catch (Exception ex) { Logger.Debug("Speckle operator page cancel failed: " + ex.Message); }
            try { stopped?.Stop(); } catch (Exception ex) { Logger.Debug("Speckle operator page stop failed: " + ex.Message); }
            stopping?.Dispose();
        }

        public void Dispose() {
            Stop();
            connections.Dispose();
        }

        private static TcpListener Bind(IPAddress address, int preferred) {
            SocketException last = null;
            foreach (var candidate in Candidates(preferred)) {
                var attempt = new TcpListener(address, candidate);
                try {
                    attempt.Start();
                    return attempt;
                } catch (SocketException ex) {
                    last = ex;
                    try { attempt.Stop(); } catch (Exception) { }
                    Logger.Warning("Speckle operator page: port " + candidate.ToString(CultureInfo.InvariantCulture)
                        + " unavailable (" + ex.SocketErrorCode + "), trying the next one");
                }
            }
            throw new IOException("No free port for the Speckle operator page", last);
        }

        private static IEnumerable<int> Candidates(int preferred) {
            var start = preferred < 1024 || preferred > 65535 ? 32323 : preferred;
            yield return start;
            for (var port = start + 1; port < start + PortProbeRange && port <= 65535; port++) {
                yield return port;
            }
            yield return 0;
        }

        private async Task AcceptLoopAsync(TcpListener active, CancellationToken token) {
            while (!token.IsCancellationRequested) {
                TcpClient client;
                try {
                    client = await active.AcceptTcpClientAsync(token).ConfigureAwait(false);
                } catch (OperationCanceledException) {
                    return;
                } catch (ObjectDisposedException) {
                    return;
                } catch (SocketException) {
                    return;
                } catch (InvalidOperationException) {
                    return;
                }
                if (!connections.Wait(0)) {
                    Logger.Warning("Speckle operator page: connection limit reached, dropping a client");
                    try { client.Close(); } catch (Exception) { }
                    continue;
                }
                _ = Task.Run(async () => {
                    try {
                        await ServeAsync(client, token).ConfigureAwait(false);
                    } finally {
                        connections.Release();
                    }
                }, CancellationToken.None);
            }
        }

        private async Task ServeAsync(TcpClient client, CancellationToken token) {
            try {
                using (client)
                using (var limit = CancellationTokenSource.CreateLinkedTokenSource(token)) {
                    limit.CancelAfter(ConnectionTimeout);
                    client.NoDelay = true;
                    var stream = client.GetStream();
                    var head = await ReadHeadAsync(stream, limit.Token).ConfigureAwait(false);
                    if (string.IsNullOrEmpty(head)) {
                        return;
                    }
                    var firstLine = head.Split('\n')[0].TrimEnd('\r');
                    var parts = firstLine.Split(' ');
                    if (parts.Length < 2) {
                        return;
                    }
                    var target = parts[1];
                    var separator = target.IndexOf('?');
                    var path = separator < 0 ? target : target.Substring(0, separator);
                    var request = new HttpRequest {
                        Method = parts[0],
                        Path = Unescape(path),
                        Query = ParseQuery(separator < 0 ? string.Empty : target.Substring(separator + 1)),
                        Headers = ParseHeaders(head)
                    };
                    HttpReply reply;
                    try {
                        reply = route(request);
                    } catch (Exception ex) {
                        Logger.Error("Speckle operator page handler failed", ex);
                        reply = HttpReply.Text(500, "internal error");
                    }
                    await WriteAsync(stream, reply, limit.Token).ConfigureAwait(false);
                }
            } catch (OperationCanceledException) {
            } catch (IOException) {
            } catch (SocketException) {
            } catch (ObjectDisposedException) {
            } catch (Exception ex) {
                Logger.Error("Speckle operator page connection failed", ex);
            }
        }

        private static async Task<string> ReadHeadAsync(NetworkStream stream, CancellationToken token) {
            var buffer = new byte[2048];
            var head = new MemoryStream();
            while (head.Length < HeadLimit) {
                var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), token).ConfigureAwait(false);
                if (read <= 0) {
                    break;
                }
                head.Write(buffer, 0, read);
                var text = Encoding.ASCII.GetString(head.GetBuffer(), 0, (int)head.Length);
                if (text.Contains("\r\n\r\n") || text.Contains("\n\n")) {
                    return text;
                }
            }
            return head.Length == 0 ? null : Encoding.ASCII.GetString(head.GetBuffer(), 0, (int)head.Length);
        }

        private static string Unescape(string value) {
            try {
                return Uri.UnescapeDataString(value);
            } catch (UriFormatException) {
                return value;
            }
        }

        private static Dictionary<string, string> ParseHeaders(string head) {
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var lines = head.Split('\n');
            for (var index = 1; index < lines.Length; index++) {
                var line = lines[index].TrimEnd('\r');
                if (line.Length == 0) {
                    break;
                }
                var split = line.IndexOf(':');
                if (split <= 0) {
                    continue;
                }
                headers[line.Substring(0, split).Trim()] = line.Substring(split + 1).Trim();
            }
            return headers;
        }

        private static Dictionary<string, string> ParseQuery(string query) {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(query)) {
                return result;
            }
            foreach (var pair in query.Split('&')) {
                if (pair.Length == 0) {
                    continue;
                }
                var split = pair.IndexOf('=');
                var key = split < 0 ? pair : pair.Substring(0, split);
                var value = split < 0 ? string.Empty : pair.Substring(split + 1);
                result[Unescape(key)] = Unescape(value.Replace("+", " "));
            }
            return result;
        }

        private static async Task WriteAsync(NetworkStream stream, HttpReply reply, CancellationToken token) {
            var body = reply.Body ?? Array.Empty<byte>();
            var builder = new StringBuilder();
            builder.Append("HTTP/1.1 ").Append(reply.Status.ToString(CultureInfo.InvariantCulture)).Append(' ').Append(reply.Reason).Append("\r\n");
            builder.Append("Content-Type: ").Append(reply.ContentType).Append("\r\n");
            builder.Append("Content-Length: ").Append(body.Length.ToString(CultureInfo.InvariantCulture)).Append("\r\n");
            builder.Append("Cache-Control: ").Append(string.IsNullOrEmpty(reply.CacheControl) ? "no-store" : reply.CacheControl).Append("\r\n");
            builder.Append("X-Content-Type-Options: nosniff\r\n");
            builder.Append("Connection: close\r\n\r\n");
            var header = Encoding.ASCII.GetBytes(builder.ToString());
            await stream.WriteAsync(header.AsMemory(0, header.Length), token).ConfigureAwait(false);
            if (body.Length > 0) {
                await stream.WriteAsync(body.AsMemory(0, body.Length), token).ConfigureAwait(false);
            }
            await stream.FlushAsync(token).ConfigureAwait(false);
        }
    }
}
