// SysopTV, Copyright (C) 2026 Christoph Scheel
// Licensed under the GNU General Public License v3.0, see LICENSE.
//
// Firmware update from the program. The firmware comes with the program
// (folder "firmware" next to SysopTV.exe). With the update check on, or after
// "Jetzt nach Updates suchen", a newer firmware of the latest GitHub release
// is used as well: downloaded on the click, checked against SHA256SUMS.txt
// and kept in the data folder (folder "firmware" next to state.json). The upload
// goes through the update page of the cube, the same one the browser uses:
//   1. GET /api/info: only a cube with SysopTV firmware is updated, never the
//      original firmware of GeekMagic (it has no /api/info with "SysopTV").
//   2. GET /update with the admin password: the page carries the form token
//      (CSRF) in the address of its form.
//   3. POST /update?csrf=... with the file as multipart field "firmware".
//      The cube checks password and token before it writes anything.
//   4. Wait for the restart and read the version from /api/info again.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace SysopTV.Tray
{
    internal enum UpdateOutcome
    {
        Done,            // the new version runs
        NotReachable,    // no SysopTV answers at the address
        PasswordNeeded,  // the cube wants the admin password and none is known
        WrongPassword,
        Refused,         // the cube refused the form (token), nothing written
        Aborted,         // the upload or the writing failed, the old firmware stays
        NoRestart,       // the cube did not come back in time
        OldVersion       // the cube came back with the old version
    }

    internal sealed class UpdateResult
    {
        internal UpdateOutcome Outcome;
        internal string Message;  // German, for the window
        internal string Version;  // what runs now, if known
    }

    // Answer of one HTTP request, Status 0 if the cube was not reached.
    internal sealed class HttpReply
    {
        internal int Status;
        internal string Body = "";
        internal bool Broken;     // the connection broke off
        internal long BytesSent;  // for the upload
    }

    // A firmware file: the one that comes with the program, one downloaded
    // before (both with Path), or one in the GitHub release (Path null until
    // EnsureLocal() downloaded it).
    internal sealed class BundledFirmware
    {
        internal const int MaxSize = 500000;  // same limit as build.sh
        internal string Path;
        internal string Version;
        internal ReleaseInfo Release;  // set for a firmware from GitHub

        internal bool FromGitHub
        {
            get { return Release != null; }
        }

        // Downloaded firmware files, in the data folder of the program.
        internal static string CacheFolder
        {
            get { return System.IO.Path.Combine(AppPaths.LogDir, "firmware"); }
        }

        // The newer of the firmware that comes with the program and one downloaded before.
        internal static BundledFirmware FindLocal()
        {
            return Newest(Find(Folder), Find(CacheFolder));
        }

        internal static BundledFirmware Newest(BundledFirmware a, BundledFirmware b)
        {
            if (a == null)
            {
                return b;
            }
            if (b == null)
            {
                return a;
            }
            return UpdateCheck.Newer(b.Version, a.Version) ? b : a;
        }

        // The local firmware, or the one of the release if that is newer.
        internal static BundledFirmware Best(BundledFirmware local, ReleaseInfo release)
        {
            if (release == null || release.FirmwareUrl == null || (local != null && !UpdateCheck.Newer(release.FirmwareVersion, local.Version)))
            {
                return local;
            }
            return new BundledFirmware { Version = release.FirmwareVersion, Release = release };
        }

        // Downloads a firmware of the release, checks it against SHA256SUMS.txt
        // and keeps it in CacheFolder. False with a German error text if anything
        // does not fit; then nothing is kept.
        internal bool EnsureLocal(out string error)
        {
            error = null;
            if (Path != null)
            {
                return true;
            }
            byte[] data = UpdateCheck.DownloadVerified(Release, Release.FirmwareUrl, Release.FirmwareName, out error);
            if (data == null)
            {
                return false;
            }
            Directory.CreateDirectory(CacheFolder);
            string file = System.IO.Path.Combine(CacheFolder, Release.FirmwareName);
            File.WriteAllBytes(file, data);
            if (!Valid(file))
            {
                File.Delete(file);
                error = "Die Datei " + Release.FirmwareName + " ist keine gültige Firmware für den Würfel.";
                return false;
            }
            Path = file;
            return true;
        }

        internal static string Folder
        {
            get { return System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "firmware"); }
        }

        // The newest valid sysoptv-<version>.bin in the folder, null if none.
        internal static BundledFirmware Find(string folder)
        {
            BundledFirmware best = null;
            try
            {
                if (!Directory.Exists(folder))
                {
                    return null;
                }
                foreach (string file in Directory.GetFiles(folder, "sysoptv-*.bin"))
                {
                    string name = System.IO.Path.GetFileNameWithoutExtension(file);
                    string version = UpdateCheck.Clean(name.Substring("sysoptv-".Length));
                    if (version == null || !Valid(file))
                    {
                        continue;
                    }
                    if (best == null || UpdateCheck.Newer(version, best.Version))
                    {
                        best = new BundledFirmware { Path = file, Version = version };
                    }
                }
            }
            catch (Exception)
            {
                return null;
            }
            return best;
        }

        internal static BundledFirmware Find()
        {
            return Find(Folder);
        }

        // Header of an ESP8266 image (E9, DIO, 4 MB) and not larger than the limit.
        internal static bool Valid(string file)
        {
            var fi = new FileInfo(file);
            if (fi.Length < 1024 || fi.Length > MaxSize)
            {
                return false;
            }
            var head = new byte[4];
            using (FileStream fs = File.OpenRead(file))
            {
                if (fs.Read(head, 0, 4) != 4)
                {
                    return false;
                }
            }
            return head[0] == 0xE9 && head[2] == 0x02 && head[3] == 0x40;
        }

        // Is the cube older than this firmware? Only for SysopTV cubes.
        internal bool NewerThan(CubeInfo info)
        {
            return info != null && UpdateCheck.Newer(Version, info.Version);
        }
    }

    internal sealed class FirmwareUpdater
    {
        // The HTTP parts. The offline tests put a simulated cube here.
        internal Func<string, TimeSpan, CubeInfo> ReadInfo = Cube.GetInfo;
        internal Func<string, string, HttpReply> GetUpdatePage = RealGetUpdatePage;
        internal Func<string, string, string, string, byte[], Action<long>, HttpReply> Upload = RealUpload;
        internal Action<TimeSpan> Sleep = Thread.Sleep;

        internal TimeSpan RestartWait = TimeSpan.FromSeconds(90);
        internal TimeSpan PollEvery = TimeSpan.FromSeconds(2);
        // After a broken connection: a cube that answers this long after the
        // upload without having restarted never took the file.
        internal TimeSpan BrokenGrace = TimeSpan.FromSeconds(15);

        private static readonly Regex TokenPattern = new Regex("action=\"/update\\?csrf=([0-9A-Za-z]+)\"", RegexOptions.CultureInvariant);

        // progress gets short German texts ("Lade hoch: 120 von 415 KB").
        internal UpdateResult Run(string address, string password, string fileName, byte[] image, string version, Action<string> progress)
        {
            Report(progress, "Prüfe den Würfel ...");
            CubeInfo before = ReadInfo(address, TimeSpan.FromSeconds(4));
            if (before == null)
            {
                return Fail(UpdateOutcome.NotReachable, "Würfel nicht erreichbar oder ohne SysopTV-Firmware. Das Programm aktualisiert nur Würfel mit SysopTV, nie die Original-Firmware von GeekMagic.", null);
            }

            Report(progress, "Melde mich an ...");
            HttpReply page = GetUpdatePage(address, password);
            if (page.Status == 0)
            {
                return Fail(UpdateOutcome.NotReachable, "Würfel nicht erreichbar.", before.Version);
            }
            if (page.Status == 401)
            {
                return string.IsNullOrEmpty(password)
                    ? Fail(UpdateOutcome.PasswordNeeded, "Der Würfel verlangt das Admin-Passwort.", before.Version)
                    : Fail(UpdateOutcome.WrongPassword, "Das Admin-Passwort stimmt nicht. Nichts wurde geändert.", before.Version);
            }
            Match m = TokenPattern.Match(page.Body ?? "");
            if (page.Status != 200 || !m.Success)
            {
                return Fail(UpdateOutcome.Refused, "Die Update-Seite des Würfels antwortet nicht wie erwartet (HTTP " + page.Status + "). Nichts wurde geändert.", before.Version);
            }

            long total = image.Length;
            Report(progress, "Lade hoch: 0 von " + total / 1024 + " KB");
            HttpReply up = Upload(address, password, m.Groups[1].Value, fileName, image,
                                  sent => Report(progress, "Lade hoch: " + Math.Min(sent, total) / 1024 + " von " + total / 1024 + " KB"));
            DateTime uploadEnd = DateTime.Now;
            if (up.Status == 401)
            {
                return Fail(UpdateOutcome.WrongPassword, "Das Admin-Passwort stimmt nicht. Nichts wurde geändert.", before.Version);
            }
            if (up.Status == 403)
            {
                return Fail(UpdateOutcome.Refused, "Der Würfel hat das Formular abgelehnt, zum Beispiel nach einem Neustart. Nichts wurde geändert, bitte noch einmal versuchen.", before.Version);
            }
            string body = PlainText(up.Body);
            if (up.Status == 200 && body.Contains("Update fehlgeschlagen"))
            {
                return Fail(UpdateOutcome.Aborted, "Update abgebrochen: " + Detail(body) + " Die bisherige Firmware bleibt erhalten, der Würfel startet in ein paar Sekunden neu.", before.Version);
            }
            bool accepted = up.Status == 200 && body.Contains("Update erfolgreich");
            // The cube restarts right after its answer; a connection that broke
            // off after the whole file went through may still have worked.
            bool maybe = up.Broken && up.BytesSent >= total;
            if (!accepted && !maybe)
            {
                string why = up.Status == 0 ? "Verbindung abgebrochen" : "HTTP " + up.Status;
                return Fail(UpdateOutcome.Aborted, "Update abgebrochen (" + why + "). Die bisherige Firmware bleibt erhalten. Geht danach gar nichts mehr, hilft der Rettungsmodus, siehe Anleitung.", before.Version);
            }

            Report(progress, "Warte auf den Neustart ...");
            Sleep(TimeSpan.FromSeconds(3));
            DateTime deadline = uploadEnd + RestartWait;
            while (true)
            {
                CubeInfo after = ReadInfo(address, TimeSpan.FromSeconds(3));
                double since = (DateTime.Now - uploadEnd).TotalSeconds;
                if (after != null)
                {
                    if (after.Version == version)
                    {
                        return new UpdateResult { Outcome = UpdateOutcome.Done, Message = "Firmware " + version + " läuft.", Version = version };
                    }
                    // Restarted (short uptime) but still the old version.
                    if (after.Uptime <= since + 5)
                    {
                        return Fail(UpdateOutcome.OldVersion, "Der Würfel läuft nach dem Neustart weiter mit Firmware " + after.Version + ". Das Update wurde nicht übernommen.", after.Version);
                    }
                    // The connection broke off and the cube simply goes on: the file never arrived.
                    if (!accepted && since >= BrokenGrace.TotalSeconds)
                    {
                        return Fail(UpdateOutcome.Aborted, "Die Verbindung brach beim Hochladen ab. Der Würfel läuft weiter mit Firmware " + after.Version + ", nichts wurde geändert.", after.Version);
                    }
                }
                if (DateTime.Now >= deadline)
                {
                    return Fail(UpdateOutcome.NoRestart, "Der Würfel meldet sich nach dem Update nicht zurück. Strom kurz trennen und wieder einstecken. Zeigt er danach „RETTUNGSMODUS“, die Firmware über seine Update-Seite im Browser noch einmal einspielen.", null);
                }
                Sleep(PollEvery);
            }
        }

        private static void Report(Action<string> progress, string text)
        {
            if (progress != null)
            {
                progress(text);
            }
        }

        private static UpdateResult Fail(UpdateOutcome o, string message, string version)
        {
            return new UpdateResult { Outcome = o, Message = message, Version = version };
        }

        // Text of an HTML page without tags, entities for umlauts resolved.
        internal static string PlainText(string html)
        {
            string t = Regex.Replace(html ?? "", "<[^>]*>", " ");
            t = WebUtility.HtmlDecode(t);
            return Regex.Replace(t, "\\s+", " ").Trim();
        }

        // The reason the firmware gives after "Update fehlgeschlagen".
        private static string Detail(string text)
        {
            int i = text.IndexOf("Update fehlgeschlagen", StringComparison.Ordinal);
            string rest = i >= 0 ? text.Substring(i + "Update fehlgeschlagen".Length).Trim() : "";
            int end = rest.IndexOf(". ", StringComparison.Ordinal);
            if (end > 0)
            {
                rest = rest.Substring(0, end + 1);
            }
            if (rest.Length == 0 || rest.Length > 120)
            {
                return "Der Würfel meldet einen Fehler.";
            }
            return rest;
        }

        // ---- the real requests ----

        private static readonly HttpClient Http = new HttpClient(new HttpClientHandler { UseProxy = false })
        {
            Timeout = TimeSpan.FromMinutes(3)
        };

        private static void AddAuth(HttpRequestMessage req, string password)
        {
            if (!string.IsNullOrEmpty(password))
            {
                req.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes("admin:" + password)));
            }
        }

        private static HttpReply RealGetUpdatePage(string address, string password)
        {
            try
            {
                using (var req = new HttpRequestMessage(HttpMethod.Get, Net.BaseUrl(address) + "update"))
                using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
                {
                    AddAuth(req, password);
                    using (HttpResponseMessage resp = Http.SendAsync(req, cts.Token).GetAwaiter().GetResult())
                    {
                        return new HttpReply { Status = (int)resp.StatusCode, Body = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult() };
                    }
                }
            }
            catch (Exception)
            {
                return new HttpReply { Status = 0 };
            }
        }

        private static HttpReply RealUpload(string address, string password, string token, string fileName, byte[] image, Action<long> sent)
        {
            var content = new UploadContent(fileName, image, sent);
            try
            {
                using (var req = new HttpRequestMessage(HttpMethod.Post, Net.BaseUrl(address) + "update?csrf=" + Uri.EscapeDataString(token)))
                {
                    AddAuth(req, password);
                    req.Content = content;
                    using (HttpResponseMessage resp = Http.SendAsync(req).GetAwaiter().GetResult())
                    {
                        string body = "";
                        try
                        {
                            body = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                        }
                        catch (Exception)
                        {
                            // The cube restarts right after the answer.
                        }
                        return new HttpReply { Status = (int)resp.StatusCode, Body = body, BytesSent = content.Sent };
                    }
                }
            }
            catch (Exception)
            {
                return new HttpReply { Status = 0, Broken = true, BytesSent = content.Sent };
            }
            finally
            {
                content.Dispose();
            }
        }

        // multipart/form-data with the one field "firmware", sent in pieces of
        // 4 KB so the window can show the progress. Quoted values like a browser.
        private sealed class UploadContent : HttpContent
        {
            private readonly byte[] head;
            private readonly byte[] image;
            private readonly byte[] tail;
            private readonly Action<long> sent;
            internal long Sent;

            internal UploadContent(string fileName, byte[] image, Action<long> sent)
            {
                string boundary = "sysoptv" + DateTime.Now.Ticks.ToString("x", CultureInfo.InvariantCulture);
                head = Encoding.ASCII.GetBytes("--" + boundary + "\r\nContent-Disposition: form-data; name=\"firmware\"; filename=\"" + fileName +
                                               "\"\r\nContent-Type: application/octet-stream\r\n\r\n");
                tail = Encoding.ASCII.GetBytes("\r\n--" + boundary + "--\r\n");
                this.image = image;
                this.sent = sent;
                Headers.ContentType = MediaTypeHeaderValue.Parse("multipart/form-data; boundary=" + boundary);
            }

            protected override async Task SerializeToStreamAsync(Stream stream, TransportContext context)
            {
                await stream.WriteAsync(head, 0, head.Length).ConfigureAwait(false);
                for (int pos = 0; pos < image.Length; pos += 4096)
                {
                    int n = Math.Min(4096, image.Length - pos);
                    await stream.WriteAsync(image, pos, n).ConfigureAwait(false);
                    Sent = pos + n;
                    if (sent != null)
                    {
                        sent(Sent);
                    }
                }
                await stream.WriteAsync(tail, 0, tail.Length).ConfigureAwait(false);
            }

            protected override bool TryComputeLength(out long length)
            {
                length = head.Length + image.Length + tail.Length;
                return true;
            }
        }
    }
}
