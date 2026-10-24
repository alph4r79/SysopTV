// SysopTV, Copyright (C) 2026 Christoph Scheel
// Licensed under the GNU General Public License v3.0, see LICENSE.
//
// Update check against the latest release of alph4r79/SysopTV on GitHub.
// Only while "Einmal täglich nach neuen Versionen suchen" is on (at most
// once a day) or after a click on "Jetzt nach Updates suchen". Drafts and
// pre-releases are ignored.
//
// A release carries these files, the versions are read from their names,
// because a release may bring a new firmware without a new program and the
// other way round:
//   sysoptv-app-X.Y.Z-setup.exe, sysoptv-app-X.Y.Z-portable.zip,
//   sysoptv-X.Y.Z.bin (firmware), SHA256SUMS.txt
// Every download is checked against SHA256SUMS.txt (format of sha256sum:
// checksum, two spaces, file name). Without the file or with a wrong
// checksum nothing is used.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;

namespace SysopTV.Tray
{
    [DataContract]
    internal sealed class ReleaseInfo
    {
        [DataMember(Name = "tag", EmitDefaultValue = false)] internal string Tag;
        [DataMember(Name = "url", EmitDefaultValue = false)] internal string Url;              // page of the release
        [DataMember(Name = "appVersion", EmitDefaultValue = false)] internal string AppVersion;
        [DataMember(Name = "setupName", EmitDefaultValue = false)] internal string SetupName;
        [DataMember(Name = "setupUrl", EmitDefaultValue = false)] internal string SetupUrl;
        [DataMember(Name = "zipUrl", EmitDefaultValue = false)] internal string ZipUrl;
        [DataMember(Name = "firmwareVersion", EmitDefaultValue = false)] internal string FirmwareVersion;
        [DataMember(Name = "firmwareName", EmitDefaultValue = false)] internal string FirmwareName;
        [DataMember(Name = "firmwareUrl", EmitDefaultValue = false)] internal string FirmwareUrl;
        [DataMember(Name = "sumsUrl", EmitDefaultValue = false)] internal string SumsUrl;

        // Version of the program in the release, null if it brings none.
        internal string Version
        {
            get { return AppVersion; }
        }
    }

    internal static class UpdateCheck
    {
        // The addresses can only be changed by the offline tests (simulated GitHub on 127.0.0.1).
        internal static string ApiUrl = "https://api.github.com/repos/alph4r79/SysopTV/releases/latest";
        internal static string ReleasesUrl = "https://github.com/alph4r79/SysopTV/releases";
        internal static string DownloadPrefix = "https://github.com/alph4r79/SysopTV/releases/download/";

        internal const string SumsName = "SHA256SUMS.txt";
        internal static readonly TimeSpan Every = TimeSpan.FromDays(1);

        private static readonly Regex SetupPattern = new Regex(@"^sysoptv-app-(\d+\.\d+\.\d+)-setup\.exe$", RegexOptions.CultureInvariant);
        private static readonly Regex ZipPattern = new Regex(@"^sysoptv-app-(\d+\.\d+\.\d+)-portable\.zip$", RegexOptions.CultureInvariant);
        private static readonly Regex FirmwarePattern = new Regex(@"^sysoptv-(\d+\.\d+\.\d+)\.bin$", RegexOptions.CultureInvariant);

        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };

        // Due if switched on and the last check is at least a day ago.
        internal static bool Due(bool enabled, long checkedAt, DateTimeOffset now)
        {
            return enabled && now.ToUnixTimeSeconds() - checkedAt >= (long)Every.TotalSeconds;
        }

        // The answer of the GitHub API as text, null without network or on any error.
        internal static string Fetch()
        {
            byte[] b = Get(ApiUrl, TimeSpan.FromSeconds(15));
            return b != null ? Encoding.UTF8.GetString(b) : null;
        }

        private static byte[] Get(string url, TimeSpan timeout)
        {
            try
            {
                using (var req = new HttpRequestMessage(HttpMethod.Get, url))
                using (var cts = new CancellationTokenSource(timeout))
                {
                    req.Headers.TryAddWithoutValidation("User-Agent", "SysopTV/" + Program.Version);
                    req.Headers.TryAddWithoutValidation("Accept", url == ApiUrl ? "application/vnd.github+json" : "application/octet-stream");
                    using (HttpResponseMessage resp = Http.SendAsync(req, cts.Token).GetAwaiter().GetResult())
                    {
                        if (!resp.IsSuccessStatusCode)
                        {
                            return null;
                        }
                        return resp.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
                    }
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        // Reads the release. Null for drafts, pre-releases and answers that do not fit.
        internal static ReleaseInfo Parse(string json)
        {
            if (string.IsNullOrEmpty(json))
            {
                return null;
            }
            try
            {
                var j = new JavaScriptSerializer().DeserializeObject(json) as Dictionary<string, object>;
                if (j == null)
                {
                    return null;
                }
                object tag, url, draft, pre, assets;
                j.TryGetValue("tag_name", out tag);
                j.TryGetValue("html_url", out url);
                j.TryGetValue("draft", out draft);
                j.TryGetValue("prerelease", out pre);
                j.TryGetValue("assets", out assets);
                if (true.Equals(draft) || true.Equals(pre))
                {
                    return null;
                }
                var r = new ReleaseInfo { Tag = tag as string };
                string link = url as string;
                r.Url = link != null && link.StartsWith(ReleasesUrl + "/", StringComparison.Ordinal) ? link : ReleasesUrl;
                var list = assets as object[];
                if (list != null)
                {
                    foreach (object o in list)
                    {
                        var a = o as Dictionary<string, object>;
                        if (a == null)
                        {
                            continue;
                        }
                        object n, d;
                        a.TryGetValue("name", out n);
                        a.TryGetValue("browser_download_url", out d);
                        string name = n as string, dl = d as string;
                        if (name == null || dl == null || !dl.StartsWith(DownloadPrefix, StringComparison.Ordinal))
                        {
                            continue;  // only files of this repository
                        }
                        Match m;
                        if ((m = SetupPattern.Match(name)).Success && Newer(m.Groups[1].Value, r.AppVersion ?? "0.0.0"))
                        {
                            r.AppVersion = m.Groups[1].Value;
                            r.SetupName = name;
                            r.SetupUrl = dl;
                        }
                        else if ((m = ZipPattern.Match(name)).Success)
                        {
                            r.ZipUrl = dl;
                            if (r.AppVersion == null)
                            {
                                r.AppVersion = m.Groups[1].Value;
                            }
                        }
                        else if ((m = FirmwarePattern.Match(name)).Success && Newer(m.Groups[1].Value, r.FirmwareVersion ?? "0.0.0"))
                        {
                            r.FirmwareVersion = m.Groups[1].Value;
                            r.FirmwareName = name;
                            r.FirmwareUrl = dl;
                        }
                        else if (name == SumsName)
                        {
                            r.SumsUrl = dl;
                        }
                    }
                }
                // A release without files named by the scheme: the tag names the program.
                if (r.AppVersion == null && r.FirmwareVersion == null)
                {
                    r.AppVersion = Clean(r.Tag);
                }
                return r.AppVersion != null || r.FirmwareVersion != null ? r : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        // "v0.8.1" or "0.8.1" to "0.8.1"; null for anything else.
        internal static string Clean(string tag)
        {
            if (string.IsNullOrEmpty(tag))
            {
                return null;
            }
            string t = tag.Trim();
            if (t.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            {
                t = t.Substring(1);
            }
            string[] parts = t.Split('.');
            if (parts.Length < 2 || parts.Length > 3)
            {
                return null;
            }
            foreach (string p in parts)
            {
                int n;
                if (!int.TryParse(p, NumberStyles.None, CultureInfo.InvariantCulture, out n))
                {
                    return null;
                }
            }
            return t;
        }

        // True if version is newer than own (both like "0.8.0").
        internal static bool Newer(string version, string own)
        {
            if (version == null)
            {
                return false;
            }
            int[] a = Numbers(version);
            int[] b = Numbers(own);
            for (int i = 0; i < 3; i++)
            {
                if (a[i] != b[i])
                {
                    return a[i] > b[i];
                }
            }
            return false;
        }

        private static int[] Numbers(string v)
        {
            var n = new int[3];
            string[] parts = (v ?? "").Split('.');
            for (int i = 0; i < 3 && i < parts.Length; i++)
            {
                int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out n[i]);
            }
            return n;
        }

        // Lines like sha256sum writes them: 64 hex digits, two spaces (or space
        // and star), file name. Returns file name to checksum in lower case.
        internal static Dictionary<string, string> ParseSums(string text)
        {
            var sums = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string raw in (text ?? "").Split('\n'))
            {
                Match m = Regex.Match(raw.TrimEnd('\r'), @"^([0-9a-fA-F]{64}) [ *](.+)$");
                if (m.Success)
                {
                    sums[m.Groups[2].Value.Trim()] = m.Groups[1].Value.ToLowerInvariant();
                }
            }
            return sums;
        }

        internal static string Sha256Hex(byte[] data)
        {
            using (SHA256 sha = SHA256.Create())
            {
                var sb = new StringBuilder();
                foreach (byte b in sha.ComputeHash(data))
                {
                    sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
                }
                return sb.ToString();
            }
        }

        // Downloads one file of the release and checks it against SHA256SUMS.txt.
        // Returns null with a German error text if anything does not fit.
        internal static byte[] DownloadVerified(ReleaseInfo r, string url, string name, out string error)
        {
            error = null;
            if (r == null || url == null || name == null || !url.StartsWith(DownloadPrefix, StringComparison.Ordinal))
            {
                error = "Die Datei gehört nicht zum Release von SysopTV.";
                return null;
            }
            if (r.SumsUrl == null)
            {
                error = "Im Release fehlt " + SumsName + ", ohne Prüfsumme wird nichts verwendet.";
                return null;
            }
            byte[] sumsBytes = Get(r.SumsUrl, TimeSpan.FromSeconds(30));
            if (sumsBytes == null)
            {
                error = "GitHub ist nicht erreichbar.";
                return null;
            }
            string expected;
            if (!ParseSums(Encoding.UTF8.GetString(sumsBytes)).TryGetValue(name, out expected))
            {
                error = SumsName + " enthält keine Prüfsumme für " + name + ", die Datei wird nicht verwendet.";
                return null;
            }
            byte[] data = Get(url, TimeSpan.FromMinutes(5));
            if (data == null)
            {
                error = "Der Download von " + name + " ist fehlgeschlagen.";
                return null;
            }
            if (Sha256Hex(data) != expected)
            {
                error = "Die Prüfsumme von " + name + " stimmt nicht. Die Datei wurde nicht verwendet.";
                return null;
            }
            return data;
        }
    }

    // Result of one update check, for the page Programm.
    internal sealed class UpdateCheckResult
    {
        internal bool Reached;        // GitHub answered with a usable release
        internal ReleaseInfo Release;
        internal bool AppNewer;
        internal bool FirmwareNewer;
    }
}
