// SysopTV, Copyright (C) 2026 Christoph Scheel
// Licensed under the GNU General Public License v3.0, see LICENSE.
//
// Orders and revenue from a WooCommerce shop through the WordPress plugin
// "SysopTV for WooCommerce" (wordpress/sysoptv-woocommerce): one request to
// GET /wp-json/sysoptv/v1/stats with the key in the header X-SysopTV-Key.
// The plugin computes all totals, the answer has no customer data. The key
// is never logged.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Web.Script.Serialization;

namespace SysopTV.Tray
{
    internal sealed class WooClient
    {
        internal const string Route = "/sysoptv/v1/stats";
        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        // "shop.example.com" or "https://shop.example.com/" gives "https://shop.example.com".
        internal static string NormalizeUrl(string url)
        {
            string u = (url ?? "").Trim().TrimEnd('/');
            if (u.Length == 0)
            {
                return "";
            }
            if (!u.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !u.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                u = "https://" + u;
            }
            return u;
        }

        internal ShopData Fetch(WooConfig cfg)
        {
            string site = NormalizeUrl(cfg.Url);
            if (site.Length == 0)
            {
                return new ShopData { FetchedAt = DateTime.Now, Error = "Keine Adresse der Website eingetragen." };
            }
            string key = cfg.Key;
            if (key.Length == 0)
            {
                return new ShopData { FetchedAt = DateTime.Now, Error = "Kein Schlüssel eingetragen." };
            }
            try
            {
                int status;
                string body;
                int retry;
                Get(site + "/wp-json" + Route, key, out status, out body, out retry);
                // Sites without pretty permalinks only know the REST API as ?rest_route=.
                if (status == 404 && !IsJson(body))
                {
                    Get(site + "/?rest_route=" + Route, key, out status, out body, out retry);
                }
                if (status == 200)
                {
                    return Parse(body, DateTime.Now);
                }
                return new ShopData { FetchedAt = DateTime.Now, Error = Explain(status, body, retry), RetryAfterSeconds = retry };
            }
            catch (HttpRequestException ex)
            {
                return new ShopData { FetchedAt = DateTime.Now, Error = "Website nicht erreichbar: " + ClaudeClient.Describe(ex) + ". Stimmt die Adresse?" };
            }
            catch (System.Threading.Tasks.TaskCanceledException)
            {
                return new ShopData { FetchedAt = DateTime.Now, Error = "Die Website antwortet nicht (Zeitüberschreitung)." };
            }
        }

        private static void Get(string url, string key, out int status, out string body, out int retry)
        {
            using (var req = new HttpRequestMessage(HttpMethod.Get, url))
            {
                req.Headers.TryAddWithoutValidation("X-SysopTV-Key", key);
                req.Headers.TryAddWithoutValidation("Accept", "application/json");
                using (HttpResponseMessage resp = Http.SendAsync(req).GetAwaiter().GetResult())
                {
                    status = (int)resp.StatusCode;
                    body = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                    retry = 0;
                    if (resp.Headers.RetryAfter != null && resp.Headers.RetryAfter.Delta.HasValue)
                    {
                        retry = (int)Math.Ceiling(resp.Headers.RetryAfter.Delta.Value.TotalSeconds);
                    }
                }
            }
        }

        private static bool IsJson(string body)
        {
            string b = (body ?? "").TrimStart();
            return b.StartsWith("{", StringComparison.Ordinal);
        }

        private static string Code(string body)
        {
            try
            {
                var j = new JavaScriptSerializer().DeserializeObject(body) as Dictionary<string, object>;
                object code;
                return j != null && j.TryGetValue("code", out code) ? code as string ?? "" : "";
            }
            catch (ArgumentException)
            {
                return "";
            }
            catch (InvalidOperationException)
            {
                return "";
            }
        }

        // Plain words for an answer that is not 200. Public for the offline tests.
        internal static string Explain(int status, string body, int retryAfter)
        {
            string code = Code(body);
            switch (status)
            {
                case 401:
                    return "Der Schlüssel fehlt oder ist falsch. Im Plugin unter WooCommerce, SysopTV einen neuen Schlüssel erzeugen und hier eintragen.";
                case 403:
                    if (code == "sysoptv_https_required")
                    {
                        return "Die Website antwortet nur über HTTPS. Bitte die Adresse mit https:// eintragen.";
                    }
                    return "Zugriff verweigert (HTTP 403). Vermutlich blockiert ein Sicherheits-Plugin oder eine Firewall die REST-API.";
                case 404:
                    return "Das Plugin „SysopTV for WooCommerce“ antwortet nicht. Ist es installiert und aktiviert, und stimmt die Adresse?";
                case 429:
                    if (code == "sysoptv_locked")
                    {
                        return "Zu viele falsche Schlüssel, der Zugang ist für 15 Minuten gesperrt.";
                    }
                    return "Zu viele Anfragen, das Plugin erlaubt eine alle 30 Sekunden." + (retryAfter > 0 ? " Nächster Versuch in " + retryAfter + " s." : "");
            }
            if (status >= 500)
            {
                return "Fehler auf der Website (HTTP " + status + ").";
            }
            return "Unerwartete Antwort der Website (HTTP " + status + ").";
        }

        // The answer of the plugin as ShopData. Broken answers give a ShopData with Error.
        internal static ShopData Parse(string body, DateTime fetchedAt)
        {
            const string unreadable = "Die Antwort der Website ist nicht lesbar. Vermutlich liefert ein Caching- oder Sicherheits-Plugin eine andere Seite.";
            Dictionary<string, object> j;
            try
            {
                j = new JavaScriptSerializer().DeserializeObject(body ?? "") as Dictionary<string, object>;
            }
            catch (ArgumentException)
            {
                j = null;
            }
            catch (InvalidOperationException)
            {
                j = null;
            }
            if (j == null || !j.ContainsKey("today") || !j.ContainsKey("days"))
            {
                return new ShopData { FetchedAt = fetchedAt, Error = unreadable };
            }
            try
            {
                var d = new ShopData
                {
                    FetchedAt = fetchedAt,
                    ShopName = Text(j, "shop"),
                    Currency = Text(j, "currency"),
                    Plugin = Text(j, "plugin"),
                    ShopNow = Time(Text(j, "generatedAt")) ?? fetchedAt,
                    Today = Period(j, "today"),
                    Yesterday = Period(j, "yesterday"),
                    CalendarWeek = Period(j, "week"),
                    Week = Period(j, "last7Days"),
                    Month = Period(j, "month"),
                    Unshipped = Convert.ToInt32(j["unshipped"] ?? 0, CultureInfo.InvariantCulture)
                };
                foreach (object o in (object[])j["days"])
                {
                    var day = (Dictionary<string, object>)o;
                    ShopPeriod p = PeriodOf(day);
                    p.Date = DateTime.ParseExact(Text(day, "date"), "yyyy-MM-dd", CultureInfo.InvariantCulture);
                    d.Days.Add(p);
                }
                var last = j.ContainsKey("lastOrder") ? j["lastOrder"] as Dictionary<string, object> : null;
                if (last != null)
                {
                    d.LastOrderTime = Time(Text(last, "time"));
                    d.LastOrderAmount = Money(last["amount"]);
                }
                object list;
                if (j.TryGetValue("todayOrders", out list) && list is object[])
                {
                    foreach (Dictionary<string, object> o in ((object[])list).Cast<Dictionary<string, object>>().Take(ShopData.MaxTodayOrders))
                    {
                        d.TodayOrders.Add(new ShopOrder
                        {
                            Number = Text(o, "number"),
                            Time = Time(Text(o, "time")) ?? d.ShopNow,
                            Amount = Money(o["amount"]),
                            Shipped = true.Equals(o["shipped"])
                        });
                    }
                }
                return d;
            }
            catch (Exception ex) when (ex is InvalidCastException || ex is FormatException || ex is KeyNotFoundException || ex is NullReferenceException || ex is OverflowException)
            {
                return new ShopData { FetchedAt = fetchedAt, Error = unreadable };
            }
        }

        private static string Text(Dictionary<string, object> d, string key)
        {
            object o;
            return d.TryGetValue(key, out o) && o != null ? Convert.ToString(o, CultureInfo.InvariantCulture) : "";
        }

        // Shop time as it was in the shop, without converting to the PC time zone.
        private static DateTime? Time(string iso)
        {
            DateTimeOffset t;
            if (DateTimeOffset.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.None, out t))
            {
                return t.DateTime;
            }
            return null;
        }

        private static decimal Money(object o)
        {
            return decimal.Parse(Convert.ToString(o, CultureInfo.InvariantCulture), NumberStyles.Number, CultureInfo.InvariantCulture);
        }

        private static ShopPeriod PeriodOf(Dictionary<string, object> p)
        {
            return new ShopPeriod
            {
                Orders = Convert.ToInt32(p["orders"], CultureInfo.InvariantCulture),
                Revenue = Money(p["revenue"])
            };
        }

        private static ShopPeriod Period(Dictionary<string, object> j, string key)
        {
            return PeriodOf((Dictionary<string, object>)j[key]);
        }
    }
}
