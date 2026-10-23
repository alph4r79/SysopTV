// SysopTV, Copyright (C) 2026 Christoph Scheel
// Licensed under the GNU General Public License v3.0, see LICENSE.

using System;
using System.Net;

namespace SysopTV.Tray
{
    internal static class Net
    {
        // TLS 1.2 or newer for api.anthropic.com. The cube is only reached over plain HTTP in the LAN.
        internal static void Setup()
        {
            try
            {
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls13;
            }
            catch (NotSupportedException)
            {
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            }
            // The web server on the cube does not answer "Expect: 100-continue".
            ServicePointManager.Expect100Continue = false;
        }

        // "192.168.178.50", "sysoptv.local" or "host:port" to "http://.../".
        internal static string BaseUrl(string address)
        {
            string a = (address ?? "").Trim().TrimEnd('/');
            if (a.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            {
                a = a.Substring(7);
            }
            return "http://" + a + "/";
        }
    }
}
