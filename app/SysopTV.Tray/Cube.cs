// SysopTV, Copyright (C) 2026 Christoph Scheel
// Licensed under the GNU General Public License v3.0, see LICENSE.
//
// Talking to the cube: POST /api/usage, POST /api/frame, GET /api/info,
// GET and POST /api/settings and the search in the local network. Only
// plain HTTP inside the LAN, no proxy.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace SysopTV.Tray
{
    internal sealed class SendResult
    {
        internal bool Ok;
        internal int Status;      // HTTP status, 0 if the cube was not reached
        internal string Message;  // short text for log and status
    }

    // Fields of /api/info that the program uses (firmware 0.3.2 and 0.4.0).
    internal sealed class CubeInfo
    {
        internal string Address;
        internal string Ip;
        internal string Hostname;
        internal string DeviceName;   // since 0.4.0
        internal string Version;
        internal string UptimeText;
        internal long Uptime;          // seconds
        internal bool Rescue;
        internal List<string> Capabilities = new List<string>();
        internal bool? FrameValid;     // since 0.4.3: the screen shows the whole picture from the PC

        internal bool Can(string capability)
        {
            return Capabilities.Contains(capability);
        }

        // Firmware version at least major.minor.patch.
        internal bool AtLeast(int major, int minor, int patch)
        {
            return CompareVersion(Version, major, minor, patch) >= 0;
        }

        internal static int CompareVersion(string version, int major, int minor, int patch)
        {
            string[] parts = (version ?? "").Split('.');
            int[] want = { major, minor, patch };
            for (int i = 0; i < 3; i++)
            {
                int v;
                int.TryParse(i < parts.Length ? parts[i] : "0", NumberStyles.Integer, CultureInfo.InvariantCulture, out v);
                if (v != want[i])
                {
                    return v < want[i] ? -1 : 1;
                }
            }
            return 0;
        }

        public override string ToString()
        {
            string name = !string.IsNullOrEmpty(DeviceName) ? DeviceName : Hostname;
            return Ip + "  " + name + "  (Firmware " + Version + ")";
        }
    }

    // Display settings of the cube from /api/settings (firmware 0.4.0).
    internal sealed class CubeSettings
    {
        internal int Design;
        internal List<string> DesignNames = new List<string>();
        internal int CycleSeconds;
        internal bool ShowUsed;
        internal int Brightness;
        internal bool NightEnabled;
        internal string NightFrom = "22:00";
        internal string NightTo = "07:00";
        internal int NightBrightness;
        internal string DeviceName = "";
    }

    internal static class Cube
    {
        private static readonly HttpClient Http = new HttpClient(new HttpClientHandler { UseProxy = false })
        {
            Timeout = TimeSpan.FromSeconds(15)
        };

        internal static SendResult SendUsage(string address, string json, string apiKey)
        {
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            return Post(address, "api/usage", content, apiKey);
        }

        // The picture goes as the only file of a multipart upload, see docs/frame-format.md.
        // source names the picture for the overview page of the cube (header X-SysopTV-Source).
        internal static SendResult SendFrame(string address, byte[] frame, string apiKey, string source)
        {
            var form = new MultipartFormDataContent("sysoptv" + DateTime.Now.Ticks.ToString("x"));
            var file = new ByteArrayContent(frame);
            file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            // The parser of the firmware expects quoted values like a browser sends them.
            file.Headers.ContentDisposition = new ContentDispositionHeaderValue("form-data")
            {
                Name = "\"frame\"",
                FileName = "\"frame.syf\""
            };
            form.Add(file);
            return Post(address, "api/frame", form, apiKey, source);
        }

        // Header values travel as ASCII: everything else, and the percent sign
        // itself, as percent encoded UTF-8. The firmware decodes it.
        internal static string HeaderText(string s)
        {
            var sb = new StringBuilder();
            foreach (byte b in Encoding.UTF8.GetBytes(s ?? ""))
            {
                if (b >= 0x20 && b < 0x7F && b != (byte)'%')
                {
                    sb.Append((char)b);
                }
                else
                {
                    sb.Append('%').Append(b.ToString("X2", CultureInfo.InvariantCulture));
                }
            }
            return sb.ToString();
        }

        private static SendResult Post(string address, string path, HttpContent content, string apiKey, string source = null)
        {
            try
            {
                using (var req = new HttpRequestMessage(HttpMethod.Post, Net.BaseUrl(address) + path))
                {
                    req.Content = content;
                    if (!string.IsNullOrEmpty(apiKey))
                    {
                        req.Headers.TryAddWithoutValidation("X-SysopTV-Key", apiKey);
                    }
                    if (!string.IsNullOrEmpty(source))
                    {
                        req.Headers.TryAddWithoutValidation("X-SysopTV-Source", HeaderText(source));
                    }
                    using (HttpResponseMessage resp = Http.SendAsync(req).GetAwaiter().GetResult())
                    {
                        int code = (int)resp.StatusCode;
                        string text = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                        if (resp.IsSuccessStatusCode)
                        {
                            return new SendResult { Ok = true, Status = code, Message = "HTTP " + code + " " + text };
                        }
                        if (code == 401)
                        {
                            return new SendResult { Status = code, Message = "HTTP 401, der Würfel verlangt einen API-Schlüssel oder der Schlüssel stimmt nicht" };
                        }
                        return new SendResult { Status = code, Message = "HTTP " + code + " " + text };
                    }
                }
            }
            catch (Exception ex)
            {
                return new SendResult { Status = 0, Message = ClaudeClient.Describe(ex) };
            }
            finally
            {
                content.Dispose();
            }
        }

        internal static CubeInfo GetInfo(string address, TimeSpan timeout)
        {
            using (var cts = new CancellationTokenSource(timeout))
            {
                return GetInfoAsync(address, cts.Token).GetAwaiter().GetResult();
            }
        }

        // Returns null if there is no SysopTV at this address.
        internal static async Task<CubeInfo> GetInfoAsync(string address, CancellationToken token)
        {
            try
            {
                using (HttpResponseMessage resp = await Http.GetAsync(Net.BaseUrl(address) + "api/info", token).ConfigureAwait(false))
                {
                    if (!resp.IsSuccessStatusCode)
                    {
                        return null;
                    }
                    string body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                    var j = new JavaScriptSerializer().DeserializeObject(body) as Dictionary<string, object>;
                    if (j == null || Text(j, "name") != "SysopTV")
                    {
                        return null;
                    }
                    var info = new CubeInfo
                    {
                        Address = address,
                        Ip = Text(j, "ip"),
                        Hostname = Text(j, "hostname"),
                        DeviceName = Text(j, "deviceName"),
                        Version = Text(j, "version"),
                        UptimeText = Text(j, "uptimeText"),
                        Rescue = true.Equals(Value(j, "rescue")),
                        FrameValid = j.ContainsKey("frameValid") ? true.Equals(Value(j, "frameValid")) : (bool?)null
                    };
                    long uptime;
                    long.TryParse(Text(j, "uptime"), NumberStyles.Integer, CultureInfo.InvariantCulture, out uptime);
                    info.Uptime = uptime;
                    var caps = Value(j, "capabilities") as object[];
                    if (caps != null)
                    {
                        info.Capabilities.AddRange(caps.Select(c => Convert.ToString(c, CultureInfo.InvariantCulture)));
                    }
                    else
                    {
                        info.Capabilities.Add("usage");  // firmware before 0.4.0
                    }
                    return info;
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static object Value(Dictionary<string, object> j, string key)
        {
            object o;
            return j != null && j.TryGetValue(key, out o) ? o : null;
        }

        private static string Text(Dictionary<string, object> j, string key)
        {
            object o = Value(j, key);
            return o != null ? Convert.ToString(o, CultureInfo.InvariantCulture) : "";
        }

        private static void AddAuth(HttpRequestMessage req, string password)
        {
            if (!string.IsNullOrEmpty(password))
            {
                string basic = Convert.ToBase64String(Encoding.UTF8.GetBytes("admin:" + password));
                req.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
            }
        }

        // GET /api/settings. Throws with a German message on errors.
        internal static CubeSettings GetSettings(string address, string password)
        {
            using (var req = new HttpRequestMessage(HttpMethod.Get, Net.BaseUrl(address) + "api/settings"))
            {
                AddAuth(req, password);
                return ReadSettings(req);
            }
        }

        // POST /api/settings with the JSON content type and client header the firmware requires.
        internal static CubeSettings SaveSettings(string address, string password, CubeSettings s)
        {
            var body = new Dictionary<string, object>
            {
                { "design", s.Design },
                { "cycleSeconds", s.CycleSeconds },
                { "showUsed", s.ShowUsed },
                { "brightness", s.Brightness },
                { "night", new Dictionary<string, object>
                    {
                        { "enabled", s.NightEnabled }, { "from", s.NightFrom }, { "to", s.NightTo }, { "brightness", s.NightBrightness }
                    }
                },
                { "deviceName", s.DeviceName }
            };
            using (var req = new HttpRequestMessage(HttpMethod.Post, Net.BaseUrl(address) + "api/settings"))
            {
                AddAuth(req, password);
                req.Headers.TryAddWithoutValidation("X-SysopTV-Client", "app");
                req.Content = new StringContent(new JavaScriptSerializer().Serialize(body), Encoding.UTF8, "application/json");
                return ReadSettings(req);
            }
        }

        private static CubeSettings ReadSettings(HttpRequestMessage req)
        {
            HttpResponseMessage resp;
            try
            {
                resp = Http.SendAsync(req).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Würfel nicht erreichbar: " + ClaudeClient.Describe(ex));
            }
            using (resp)
            {
                string text = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                int code = (int)resp.StatusCode;
                if (code == 401)
                {
                    throw new InvalidOperationException("Admin-Passwort fehlt oder stimmt nicht.");
                }
                if (!resp.IsSuccessStatusCode)
                {
                    throw new InvalidOperationException("Würfel meldet HTTP " + code + ": " + text);
                }
                var j = (Dictionary<string, object>)new JavaScriptSerializer().DeserializeObject(text);
                var night = Value(j, "night") as Dictionary<string, object>;
                var s = new CubeSettings
                {
                    Design = Convert.ToInt32(Value(j, "design") ?? 0, CultureInfo.InvariantCulture),
                    CycleSeconds = Convert.ToInt32(Value(j, "cycleSeconds") ?? 0, CultureInfo.InvariantCulture),
                    ShowUsed = true.Equals(Value(j, "showUsed")),
                    Brightness = Convert.ToInt32(Value(j, "brightness") ?? 100, CultureInfo.InvariantCulture),
                    NightEnabled = true.Equals(Value(night, "enabled")),
                    NightFrom = Text(night, "from"),
                    NightTo = Text(night, "to"),
                    NightBrightness = Convert.ToInt32(Value(night, "brightness") ?? 10, CultureInfo.InvariantCulture),
                    DeviceName = Text(j, "deviceName")
                };
                var names = Value(j, "designNames") as object[];
                if (names != null)
                {
                    s.DesignNames.AddRange(names.Select(n => Convert.ToString(n, CultureInfo.InvariantCulture)));
                }
                return s;
            }
        }

        // First sysoptv.local (firmware before 0.4.0), then the own /24 networks.
        // Short timeout, at most 32 requests at the same time. A hit counts only
        // if /api/info says "SysopTV".
        internal static async Task<List<CubeInfo>> DiscoverAsync(IProgress<string> progress, CancellationToken cancel)
        {
            var found = new List<CubeInfo>();
            var seen = new HashSet<string>();
            object listLock = new object();

            if (progress != null)
            {
                progress.Report("Suche sysoptv.local ...");
            }
            try
            {
                IPAddress[] addrs = await Dns.GetHostAddressesAsync("sysoptv.local").ConfigureAwait(false);
                foreach (IPAddress a in addrs.Where(x => x.AddressFamily == AddressFamily.InterNetwork))
                {
                    using (var cts = CancellationTokenSource.CreateLinkedTokenSource(cancel))
                    {
                        cts.CancelAfter(1500);
                        CubeInfo info = await GetInfoAsync(a.ToString(), cts.Token).ConfigureAwait(false);
                        if (info != null && seen.Add(a.ToString()))
                        {
                            found.Add(info);
                        }
                    }
                }
            }
            catch (Exception)
            {
                // Name not resolvable, the network scan follows.
            }

            List<IPAddress> candidates = LocalCandidates();
            if (progress != null)
            {
                progress.Report("Durchsuche " + candidates.Count + " Adressen im Netz ...");
            }
            var gate = new SemaphoreSlim(32);
            var tasks = new List<Task>();
            foreach (IPAddress ip in candidates)
            {
                if (cancel.IsCancellationRequested)
                {
                    break;
                }
                await gate.WaitAsync(cancel).ConfigureAwait(false);
                string addr = ip.ToString();
                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        using (var cts = CancellationTokenSource.CreateLinkedTokenSource(cancel))
                        {
                            cts.CancelAfter(400);
                            CubeInfo info = await GetInfoAsync(addr, cts.Token).ConfigureAwait(false);
                            if (info != null)
                            {
                                lock (listLock)
                                {
                                    if (seen.Add(addr))
                                    {
                                        found.Add(info);
                                    }
                                }
                            }
                        }
                    }
                    finally
                    {
                        gate.Release();
                    }
                }));
            }
            await Task.WhenAll(tasks).ConfigureAwait(false);
            return found;
        }

        // All addresses in the /24 networks of the active network adapters.
        private static List<IPAddress> LocalCandidates()
        {
            var list = new List<IPAddress>();
            var nets = new HashSet<string>();
            foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                {
                    continue;
                }
                foreach (UnicastIPAddressInformation ua in nic.GetIPProperties().UnicastAddresses)
                {
                    if (ua.Address.AddressFamily != AddressFamily.InterNetwork)
                    {
                        continue;
                    }
                    byte[] b = ua.Address.GetAddressBytes();
                    if (b[0] == 169 && b[1] == 254)
                    {
                        continue;  // link-local without DHCP
                    }
                    if (!nets.Add(b[0] + "." + b[1] + "." + b[2]))
                    {
                        continue;
                    }
                    for (int i = 1; i < 255; i++)
                    {
                        if (i != b[3])
                        {
                            list.Add(new IPAddress(new[] { b[0], b[1], b[2], (byte)i }));
                        }
                    }
                }
            }
            return list;
        }
    }
}
