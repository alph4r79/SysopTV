// SysopTV, Copyright (C) 2026 Christoph Scheel
// Licensed under the GNU General Public License v3.0, see LICENSE.
//
// Reads the Claude usage limits with the login of Claude Code, taken over
// from sender/sysoptv-sender.ps1. The login data is only read, never
// written, renewed, printed or logged.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Web.Script.Serialization;

namespace SysopTV.Tray
{
    internal sealed class UsageLimit
    {
        internal int Used;        // percent used, 0 to 100
        internal long? ResetsAt;  // Unix seconds (UTC)

        // A session without reset time is not running, like "nicht aktiv" in the firmware.
        internal static bool Inactive(UsageLimit l, bool isSession)
        {
            return isSession && l != null && !l.ResetsAt.HasValue;
        }

        // "Session 27 %", "Session nicht aktiv", "Woche --": one place for status line, tooltip and log.
        internal static string Short(UsageLimit l, bool isSession)
        {
            string name = isSession ? "Session " : "Woche ";
            if (Inactive(l, isSession))
            {
                return name + "nicht aktiv";
            }
            return name + (l == null ? "--" : l.Used + " %");
        }
    }

    internal sealed class UsageData
    {
        internal string State = "error";  // ok, login or error
        internal bool Attempted;          // a real query failed (not just the 429 pause)
        internal string Reason = "";
        internal UsageLimit Session;
        internal UsageLimit Week;
        internal bool Held;               // last valid values sent on after an error

        internal UsageData Copy()
        {
            return (UsageData)MemberwiseClone();
        }

        internal string Summary()
        {
            if (State != "ok")
            {
                return State + " (" + Reason + ")";
            }
            var parts = new List<string>();
            parts.Add(Held ? "ok mit letzten Werten (" + Reason + ")" : "ok");
            parts.Add(Session == null ? "keine Session-Daten" : UsageLimit.Inactive(Session, true) ? "Session nicht aktiv" : "Session " + Session.Used + " % verbraucht");
            parts.Add(Week != null ? "Woche " + Week.Used + " % verbraucht" : "keine Wochen-Daten");
            return string.Join(", ", parts);
        }
    }

    internal sealed class ClaudeClient
    {
        private const string UsageUrl = "https://api.anthropic.com/api/oauth/usage";
        private const string UserAgent = "claude-cli/2.1.0 (external, cli)";
        private const int MinBlockSeconds = 300;

        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        private DateTime blockedUntil = DateTime.MinValue;

        // Taken over from state.json at the start, so a restart does not end the block.
        internal DateTime BlockedUntil
        {
            get { return blockedUntil; }
            set { blockedUntil = value; }
        }

        internal UsageData Query()
        {
            // Asking again during a 429 block only makes the block longer.
            if (DateTime.Now < blockedUntil)
            {
                return Error(false, "429-Pause bis " + blockedUntil.ToString("HH:mm"));
            }
            if (!File.Exists(AppPaths.CredentialsFile))
            {
                return Login("keine Anmeldedatei");
            }

            string token;
            long expiresAt;
            try
            {
                var root = new JavaScriptSerializer().DeserializeObject(File.ReadAllText(AppPaths.CredentialsFile))
                           as Dictionary<string, object>;
                object oauthObj = null;
                if (root != null)
                {
                    root.TryGetValue("claudeAiOauth", out oauthObj);
                }
                var oauth = oauthObj as Dictionary<string, object>;
                object tokenObj = null;
                object expObj = null;
                if (oauth != null)
                {
                    oauth.TryGetValue("accessToken", out tokenObj);
                    oauth.TryGetValue("expiresAt", out expObj);
                }
                token = tokenObj as string;
                expiresAt = expObj != null ? Convert.ToInt64(expObj, CultureInfo.InvariantCulture) : 0;
            }
            catch (Exception)
            {
                // No details: the message could contain parts of the file.
                return Login("Anmeldedatei nicht lesbar");
            }
            if (string.IsNullOrEmpty(token))
            {
                return Login("kein Zugangsschlüssel");
            }
            if (expiresAt > 0 && DateTimeOffset.FromUnixTimeMilliseconds(expiresAt) <= DateTimeOffset.UtcNow)
            {
                return Login("Anmeldung abgelaufen");
            }

            string body;
            try
            {
                using (var req = new HttpRequestMessage(HttpMethod.Get, UsageUrl))
                {
                    req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + token);
                    req.Headers.TryAddWithoutValidation("anthropic-beta", "oauth-2025-04-20");
                    req.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
                    using (HttpResponseMessage resp = Http.SendAsync(req).GetAwaiter().GetResult())
                    {
                        int code = (int)resp.StatusCode;
                        if (code == 401)
                        {
                            return Login("Anthropic: 401");
                        }
                        if (code == 429)
                        {
                            int wait = MinBlockSeconds;
                            if (resp.Headers.RetryAfter != null)
                            {
                                if (resp.Headers.RetryAfter.Delta.HasValue)
                                {
                                    wait = Math.Max(wait, (int)resp.Headers.RetryAfter.Delta.Value.TotalSeconds);
                                }
                                else if (resp.Headers.RetryAfter.Date.HasValue)
                                {
                                    wait = Math.Max(wait, (int)(resp.Headers.RetryAfter.Date.Value - DateTimeOffset.UtcNow).TotalSeconds);
                                }
                            }
                            blockedUntil = DateTime.Now.AddSeconds(wait);
                            return Error(true, "Anthropic bremst (429), Pause bis " + blockedUntil.ToString("HH:mm"));
                        }
                        if (!resp.IsSuccessStatusCode)
                        {
                            return Error(true, "Anthropic: HTTP " + code);
                        }
                        body = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                    }
                }
            }
            catch (Exception ex)
            {
                return Error(true, "Anthropic nicht erreichbar: " + Describe(ex));
            }
            finally
            {
                token = null;
            }

            try
            {
                var u = new JavaScriptSerializer().DeserializeObject(body) as Dictionary<string, object>;
                return new UsageData
                {
                    State = "ok",
                    Session = ToLimit(u, "five_hour"),
                    Week = ToLimit(u, "seven_day")
                };
            }
            catch (Exception)
            {
                return Error(true, "Antwort von Anthropic nicht lesbar");
            }
        }

        // When the login of Claude Code runs out, for the window. Only expiresAt
        // is used, the token is neither kept nor shown. Null if unknown.
        internal static DateTime? LoginExpires()
        {
            try
            {
                if (!File.Exists(AppPaths.CredentialsFile))
                {
                    return null;
                }
                var root = new JavaScriptSerializer().DeserializeObject(File.ReadAllText(AppPaths.CredentialsFile)) as Dictionary<string, object>;
                object oauth, exp;
                if (root == null || !root.TryGetValue("claudeAiOauth", out oauth) || !(oauth is Dictionary<string, object>) ||
                    !((Dictionary<string, object>)oauth).TryGetValue("expiresAt", out exp) || exp == null)
                {
                    return null;
                }
                return DateTimeOffset.FromUnixTimeMilliseconds(Convert.ToInt64(exp, CultureInfo.InvariantCulture)).LocalDateTime;
            }
            catch (Exception)
            {
                return null;
            }
        }

        // One limit as the cube expects it, or null if missing.
        private static UsageLimit ToLimit(Dictionary<string, object> u, string key)
        {
            object o;
            if (u == null || !u.TryGetValue(key, out o))
            {
                return null;
            }
            var item = o as Dictionary<string, object>;
            object util;
            if (item == null || !item.TryGetValue("utilization", out util) || util == null)
            {
                return null;
            }
            // Math.Round rounds x.5 to the even number, like [math]::Round in the script.
            int used = (int)Math.Round(Convert.ToDouble(util, CultureInfo.InvariantCulture));
            used = Math.Max(0, Math.Min(100, used));
            var limit = new UsageLimit { Used = used };
            object reset;
            if (item.TryGetValue("resets_at", out reset) && reset is string && ((string)reset).Length > 0)
            {
                limit.ResetsAt = DateTimeOffset.Parse((string)reset, CultureInfo.InvariantCulture,
                                                      DateTimeStyles.AssumeUniversal).ToUnixTimeSeconds();
            }
            return limit;
        }

        private static UsageData Login(string reason)
        {
            return new UsageData { State = "login", Reason = reason };
        }

        private static UsageData Error(bool attempted, string reason)
        {
            return new UsageData { State = "error", Attempted = attempted, Reason = reason };
        }

        internal static string Describe(Exception ex)
        {
            Exception e = ex;
            while (e is AggregateException && e.InnerException != null)
            {
                e = e.InnerException;
            }
            if (e is HttpRequestException && e.InnerException != null)
            {
                e = e.InnerException;
            }
            if (e is System.Threading.Tasks.TaskCanceledException)
            {
                return "Zeitüberschreitung";
            }
            var we = e as WebException;
            if (we != null)
            {
                return we.Status.ToString();
            }
            return e.Message;
        }
    }

    // What goes to the cube for a query result, like Resolve-Data in the script:
    // "ok" and "login" go out as they are. After an error the last valid values
    // are sent on, as long as there were fewer than 3 failed queries in a row
    // and the values are not older than 15 minutes. Rounds in a 429 pause are
    // no new failures.
    internal sealed class FailurePolicy
    {
        private const int MaxFailures = 3;
        private const double MaxLastGoodMinutes = 15;

        private UsageData lastGood;
        private DateTime lastGoodTime;
        private int failCount;

        // The last valid values from state.json, as if they had just been read at that time.
        internal void Seed(UsageData good, DateTime when)
        {
            lastGood = good;
            lastGoodTime = when;
            failCount = 0;
        }

        internal UsageData Resolve(UsageData data, DateTime now)
        {
            if (data.State == "ok")
            {
                lastGood = data;
                lastGoodTime = now;
                failCount = 0;
                return data;
            }
            if (data.State == "login")
            {
                failCount = 0;
                return data;
            }
            if (data.Attempted)
            {
                failCount++;
            }
            if (lastGood != null && failCount < MaxFailures && (now - lastGoodTime).TotalMinutes <= MaxLastGoodMinutes)
            {
                UsageData held = lastGood.Copy();
                held.Held = true;
                held.Reason = data.Reason;
                return held;
            }
            return data;
        }
    }
}
