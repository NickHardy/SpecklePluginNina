using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace NINA.Plugin.Speckle.Web {

    internal static class LanAddress {

        public static IPAddress Primary() {
            try {
                using var probe = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                probe.Connect("8.8.8.8", 65530);
                var local = probe.LocalEndPoint as IPEndPoint;
                return local?.Address ?? IPAddress.Loopback;
            } catch (SocketException) {
                return IPAddress.Loopback;
            } catch (ObjectDisposedException) {
                return IPAddress.Loopback;
            }
        }

        public static IReadOnlyList<IPAddress> All() {
            try {
                return NetworkInterface.GetAllNetworkInterfaces()
                    .Where(adapter => adapter.OperationalStatus == OperationalStatus.Up)
                    .Where(adapter => adapter.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                    .Where(adapter => adapter.NetworkInterfaceType != NetworkInterfaceType.Tunnel)
                    .SelectMany(adapter => adapter.GetIPProperties().UnicastAddresses)
                    .Select(unicast => unicast.Address)
                    .Where(address => address.AddressFamily == AddressFamily.InterNetwork)
                    .ToList();
            } catch (NetworkInformationException) {
                return Array.Empty<IPAddress>();
            }
        }

        public static IReadOnlyList<string> Urls(int port, bool lanVisible) {
            var urls = new List<string> { Format(IPAddress.Loopback, port) };
            if (!lanVisible) {
                return urls;
            }
            var primary = Primary();
            if (!IPAddress.IsLoopback(primary)) {
                urls.Insert(0, Format(primary, port));
            }
            foreach (var address in All()) {
                var url = Format(address, port);
                if (!urls.Contains(url)) {
                    urls.Add(url);
                }
            }
            return urls;
        }

        private static string Format(IPAddress address, int port) {
            return "http://" + address + ":" + port.ToString(System.Globalization.CultureInfo.InvariantCulture) + "/";
        }
    }
}
