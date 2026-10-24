// SysopTV, Copyright (C) 2026 Christoph Scheel
// Licensed under the GNU General Public License v3.0, see LICENSE.
//
// Orders and revenue from the Shopify GraphQL Admin API (version 2026-10).
// Needed access scopes: read_orders, and read_all_orders for the year.
// Without read_all_orders Shopify only returns the last 60 days, which is
// enough for today, the week, the month and the last 14 days. Read are only number, time, amount and fulfillment state of
// each order, never customer names, addresses or e-mail addresses.
//
// Access either with an Admin API access token of an older custom app, or
// with client ID and secret of an app from the Shopify Dev Dashboard
// (client credentials grant, the token lasts 24 hours and is fetched again).
// Tokens are never logged.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace SysopTV.Tray
{
    // An error with a message that can be shown as it is.
    internal sealed class ShopifyException : Exception
    {
        internal readonly string Kind;

        internal ShopifyException(string message, string kind) : base(message)
        {
            Kind = kind;
        }
    }

    internal sealed class ShopifyClient
    {
        internal const string ApiVersion = "2026-10";
        private const int MaxPages = 40;   // 40 x 250 orders per month is plenty
        private const int MaxYearPages = 400;  // 100000 orders a year
        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        private string cachedToken;
        private DateTime cachedUntil = DateTime.MinValue;

        // Day values of finished days of this year, kept in state.json by the service.
        internal YearCache Cache = new YearCache();

        internal const string MissingAllOrders = "Für das Jahr fehlt der Zugriff read_all_orders";

        // The note for the year when the scope is missing, null if it is there.
        internal static string ScopeNote(IEnumerable<string> scopes)
        {
            foreach (string scope in scopes)
            {
                if (scope == "read_all_orders")
                {
                    return null;
                }
            }
            return MissingAllOrders;
        }

        // Orders and revenue since 1 January: the stored day values of finished
        // days plus today. The window (today and the last 30 days) replaces the
        // stored values each time; once a day fullReload gives all finished days
        // of the year again. With a new year the values start again.
        internal static ShopPeriod Year(YearCache cache, DateTime today, Dictionary<DateTime, ShopPeriod> window,
                                        Func<Dictionary<DateTime, ShopPeriod>> fullReload, ShopPeriod todaySum)
        {
            if (cache.Year != today.Year)
            {
                cache.Year = today.Year;
                cache.Days.Clear();
                cache.FullDate = DateTime.MinValue;
            }
            DateTime yearStart = new DateTime(today.Year, 1, 1);
            if (cache.FullDate != today)
            {
                Dictionary<DateTime, ShopPeriod> all = fullReload();
                cache.Days.Clear();
                foreach (KeyValuePair<DateTime, ShopPeriod> d in all)
                {
                    if (d.Key >= yearStart && d.Key < today)
                    {
                        cache.Days.Add(new ShopPeriod { Date = d.Key, Orders = d.Value.Orders, Revenue = d.Value.Revenue });
                    }
                }
                cache.FullDate = today;
            }
            foreach (KeyValuePair<DateTime, ShopPeriod> d in window)
            {
                if (d.Key >= yearStart && d.Key < today)
                {
                    cache.Days.RemoveAll(x => x.Date == d.Key);
                    cache.Days.Add(new ShopPeriod { Date = d.Key, Orders = d.Value.Orders, Revenue = d.Value.Revenue });
                }
            }
            var year = new ShopPeriod { Orders = todaySum.Orders, Revenue = todaySum.Revenue };
            foreach (ShopPeriod d in cache.Days)
            {
                if (d.Date >= yearStart && d.Date < today)
                {
                    year.Orders += d.Orders;
                    year.Revenue += d.Revenue;
                }
            }
            return year;
        }

        internal ShopData Fetch(ShopConfig cfg)
        {
            var result = new ShopData { FetchedAt = DateTime.Now };
            try
            {
                string domain = NormalizeDomain(cfg.Domain);
                if (domain.Length == 0)
                {
                    result.Error = "keine Shop-Adresse";
                    result.ErrorKind = ShopErrors.Access;
                    return result;
                }
                string token = Token(cfg, domain);
                if (Cache.Shop != domain)
                {
                    Cache = new YearCache { Shop = domain };
                }

                // Time zone of the shop and the scopes of the app in one query.
                var shop = Query(domain, token, "{ shop { name timezoneOffsetMinutes } currentAppInstallation { accessScopes { handle } } }", null);
                result.ShopName = Dict(Dict(shop, "shop"), "name")["__value"] as string ?? "";
                int offset = Convert.ToInt32(Dict(Dict(shop, "shop"), "timezoneOffsetMinutes")["__value"] ?? 0,
                                             CultureInfo.InvariantCulture);
                var scopes = new List<string>();
                foreach (object o in List(Dict(shop, "currentAppInstallation"), "accessScopes"))
                {
                    var scope = o as Dictionary<string, object>;
                    if (scope != null && scope.ContainsKey("handle"))
                    {
                        scopes.Add(scope["handle"] as string ?? "");
                    }
                }
                DateTime shopNow = ShopCalendar.ShopNow(DateTime.UtcNow, offset);
                result.ShopNow = shopNow;
                DateTime today = shopNow.Date;
                DateTime weekStart = today.AddDays(-6);
                DateTime calendarWeek = ShopCalendar.WeekStart(today);
                DateTime daysStart = today.AddDays(-13);
                DateTime monthStart = ShopCalendar.MonthStart(today);
                DateTime recent = today.AddDays(-30);
                DateTime from = daysStart < monthStart ? daysStart : monthStart;
                from = recent < from ? recent : from;
                var window = new Dictionary<DateTime, ShopPeriod>();
                for (DateTime d = from; d <= today; d = d.AddDays(1))
                {
                    window[d] = new ShopPeriod { Date = d };
                }
                for (int i = 0; i < 14; i++)
                {
                    result.Days.Add(new ShopPeriod { Date = daysStart.AddDays(i) });
                }

                bool first = true;
                foreach (Dictionary<string, object> node in Orders(domain, token, from, null, offset, true))
                {
                    DateTime created = Created(node, offset);
                    decimal amount = Amount(node);
                    if (first)
                    {
                        result.LastOrderTime = created;
                        result.LastOrderAmount = amount;
                        first = false;
                    }
                    if (created.Date == today && result.TodayOrders.Count < ShopData.MaxTodayOrders)
                    {
                        result.TodayOrders.Add(new ShopOrder
                        {
                            Number = node["name"] as string ?? "",
                            Time = created,
                            Amount = amount,
                            Shipped = "FULFILLED".Equals(node["displayFulfillmentStatus"])
                        });
                    }
                    int day = (created.Date - daysStart).Days;
                    if (day >= 0 && day < 14)
                    {
                        Add(result.Days[day], true, amount);
                    }
                    ShopPeriod w;
                    if (window.TryGetValue(created.Date, out w))
                    {
                        Add(w, true, amount);
                    }
                    Add(result.Today, created.Date == today, amount);
                    Add(result.Yesterday, created.Date == today.AddDays(-1), amount);
                    Add(result.Week, created.Date >= weekStart, amount);
                    Add(result.CalendarWeek, created.Date >= calendarWeek, amount);
                    Add(result.Month, created.Date >= monthStart, amount);
                }

                if (first)
                {
                    // No order in the window: the last order is older.
                    var last = Query(domain, token,
                        "{ orders(first:1, sortKey:CREATED_AT, reverse:true, query:\"test:false AND NOT status:cancelled\")" +
                        "{ nodes{ createdAt totalPriceSet{ shopMoney{ amount } } } } }", null);
                    foreach (object o in List(Dict(last, "orders"), "nodes"))
                    {
                        var node = (Dictionary<string, object>)o;
                        result.LastOrderTime = Created(node, offset);
                        result.LastOrderAmount = Amount(node);
                    }
                }

                // The year needs read_all_orders, read_orders only reaches back 60 days.
                result.YearNote = ScopeNote(scopes);
                if (result.YearNote == null)
                {
                    result.Year = Year(Cache, today, window, () =>
                    {
                        var all = new Dictionary<DateTime, ShopPeriod>();
                        foreach (Dictionary<string, object> node in Orders(domain, token, new DateTime(today.Year, 1, 1), today, offset, false))
                        {
                            DateTime d = Created(node, offset).Date;
                            ShopPeriod p;
                            if (!all.TryGetValue(d, out p))
                            {
                                p = new ShopPeriod { Date = d };
                                all[d] = p;
                            }
                            Add(p, true, Amount(node));
                        }
                        return all;
                    }, result.Today);
                }

                var open = Query(domain, token,
                    "{ ordersCount(query:\"fulfillment_status:unshipped AND status:open AND test:false\", limit:null){ count } }", null);
                result.Unshipped = Convert.ToInt32(Dict(open, "ordersCount")["count"], CultureInfo.InvariantCulture);
            }
            catch (ShopifyException ex)
            {
                result.Error = ex.Message;
                result.ErrorKind = ex.Kind;
            }
            catch (System.Net.Http.HttpRequestException ex)
            {
                result.Error = "Shop nicht erreichbar: " + ClaudeClient.Describe(ex) + ". Stimmt die Shop-Adresse?";
                result.ErrorKind = ShopErrors.Offline;
            }
            catch (System.Threading.Tasks.TaskCanceledException)
            {
                result.Error = "Shopify antwortet nicht (Zeitüberschreitung).";
                result.ErrorKind = ShopErrors.Offline;
            }
            catch (Exception ex)
            {
                result.Error = "Unerwartete Antwort von Shopify (" + ex.GetType().Name + ").";
                result.ErrorKind = ShopErrors.Other;
            }
            return result;
        }

        // Kind of an answer that is not fine, for the notice screen.
        internal static string KindOf(bool tokenRequest, int status, string body)
        {
            string b = body ?? "";
            if (tokenRequest || status == 401 || status == 403 || b.Contains("ACCESS_DENIED") || b.Contains("read_orders") || b.Contains("Invalid API key"))
            {
                return ShopErrors.Access;
            }
            if (status == 429 || b.Contains("THROTTLED"))
            {
                return ShopErrors.TooMany;
            }
            return status >= 500 ? ShopErrors.Offline : ShopErrors.Other;
        }

        // Plain words for what Shopify answers. Status 0 means an answer with
        // an errors object in a 200 response. Public for the offline tests.
        internal static string Explain(bool tokenRequest, int status, string body)
        {
            string b = body ?? "";
            if (tokenRequest)
            {
                if (b.Contains("shop_not_permitted"))
                {
                    return "Shopify erlaubt Client-ID und Secret für diesen Shop nicht. App und Shop müssen zur selben Organisation gehören.";
                }
                if (status == 404)
                {
                    return "Shop nicht gefunden. Stimmt die Shop-Adresse (meinshop.myshopify.com)?";
                }
                return "Anmeldung abgelehnt (HTTP " + status + "): Client-ID oder Secret falsch, oder die App ist nicht im Shop installiert.";
            }
            if (b.Contains("read_orders") || b.Contains("Required access"))
            {
                return "Der App fehlt der Scope read_orders. Im Dev Dashboard eintragen, neue Version veröffentlichen, dann die App im Shop deinstallieren und neu installieren. Client-ID und Secret bleiben gleich.";
            }
            if (status == 401 || b.Contains("ACCESS_DENIED") || b.Contains("Invalid API key or access token"))
            {
                return "Zugang abgelehnt: Token oder Secret falsch, oder die App ist nicht im Shop installiert.";
            }
            switch (status)
            {
                case 402: return "Der Shop ist eingefroren (offene Rechnung bei Shopify).";
                case 403: return "Shopify verweigert den Zugriff auf diesen Shop (HTTP 403).";
                case 404: return "Shop nicht gefunden. Stimmt die Shop-Adresse (meinshop.myshopify.com)?";
                case 423: return "Der Shop ist bei Shopify gesperrt (HTTP 423).";
            }
            if (b.Contains("SHOP_INACTIVE"))
            {
                return "Der Shop ist nicht aktiv.";
            }
            return status >= 500 ? "Fehler bei Shopify (HTTP " + status + "), später erneut." : "Shopify meldet einen Fehler (HTTP " + status + ").";
        }

        // Counted orders (not cancelled, no tests) created from "from" up to
        // "until" (exclusive, null for now), in pages of 250. newestFirst for the
        // window, oldest first for the yearly reload.
        private IEnumerable<Dictionary<string, object>> Orders(string domain, string token, DateTime from, DateTime? until, int offset, bool newestFirst)
        {
            string filter = "created_at:>='" + Utc(from, offset) + "'";
            if (until.HasValue)
            {
                filter += " AND created_at:<'" + Utc(until.Value, offset) + "'";
            }
            filter += " AND test:false AND NOT status:cancelled";
            string cursor = null;
            for (int page = 0; page < (newestFirst ? MaxPages : MaxYearPages); page++)
            {
                var vars = new Dictionary<string, object> { { "q", filter }, { "after", cursor } };
                var data = Query(domain, token,
                    "query($q:String!,$after:String){ orders(first:250, after:$after, query:$q, sortKey:CREATED_AT, reverse:" + (newestFirst ? "true" : "false") + ")" +
                    "{ pageInfo{ hasNextPage endCursor } nodes{ name createdAt cancelledAt test displayFulfillmentStatus totalPriceSet{ shopMoney{ amount } } } } }",
                    vars);
                var orders = Dict(data, "orders");
                foreach (object o in List(orders, "nodes"))
                {
                    var node = o as Dictionary<string, object>;
                    if (node == null || node["cancelledAt"] != null || true.Equals(node["test"]))
                    {
                        continue;
                    }
                    yield return node;
                }
                var info = Dict(orders, "pageInfo");
                if (!true.Equals(info["hasNextPage"]))
                {
                    yield break;
                }
                cursor = (string)info["endCursor"];
            }
        }

        private static string Utc(DateTime shopTime, int offset)
        {
            return shopTime.AddMinutes(-offset).ToString("yyyy-MM-ddTHH:mm:ss'Z'", CultureInfo.InvariantCulture);
        }

        private static DateTime Created(Dictionary<string, object> node, int offset)
        {
            return DateTimeOffset.Parse((string)node["createdAt"], CultureInfo.InvariantCulture).UtcDateTime.AddMinutes(offset);
        }

        private static decimal Amount(Dictionary<string, object> node)
        {
            return decimal.Parse((string)Dict(Dict(node, "totalPriceSet"), "shopMoney")["amount"], CultureInfo.InvariantCulture);
        }

        private static void Add(ShopPeriod p, bool inside, decimal amount)
        {
            if (inside)
            {
                p.Orders++;
                p.Revenue += amount;
            }
        }

        internal static string NormalizeDomain(string domain)
        {
            string d = (domain ?? "").Trim().ToLowerInvariant();
            if (d.StartsWith("https://"))
            {
                d = d.Substring(8);
            }
            return d.TrimEnd('/');
        }

        // Access token: given directly, or fetched with client ID and secret.
        private string Token(ShopConfig cfg, string domain)
        {
            string direct = cfg.AccessToken;
            if (!string.IsNullOrEmpty(direct))
            {
                return direct;
            }
            if (cachedToken != null && DateTime.Now < cachedUntil)
            {
                return cachedToken;
            }
            string id = cfg.ClientId, secret = cfg.ClientSecret;
            if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(secret))
            {
                throw new ShopifyException("Kein Zugang eingetragen.", ShopErrors.Access);
            }
            var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "grant_type", "client_credentials" }, { "client_id", id }, { "client_secret", secret }
            });
            using (HttpResponseMessage resp = Http.PostAsync("https://" + domain + "/admin/oauth/access_token", form).GetAwaiter().GetResult())
            {
                string body = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                if (!resp.IsSuccessStatusCode)
                {
                    throw new ShopifyException(Explain(true, (int)resp.StatusCode, body), ShopErrors.Access);
                }
                var j = (Dictionary<string, object>)new JavaScriptSerializer().DeserializeObject(body);
                cachedToken = (string)j["access_token"];
                int seconds = Convert.ToInt32(j["expires_in"], CultureInfo.InvariantCulture);
                cachedUntil = DateTime.Now.AddSeconds(Math.Max(60, seconds - 300));
                return cachedToken;
            }
        }

        // One GraphQL call. Waits and tries again if Shopify reports THROTTLED.
        private static Dictionary<string, object> Query(string domain, string token, string query, Dictionary<string, object> vars)
        {
            var ser = new JavaScriptSerializer();
            string payload = ser.Serialize(new Dictionary<string, object> { { "query", query }, { "variables", vars ?? new Dictionary<string, object>() } });
            for (int attempt = 0; attempt < 4; attempt++)
            {
                using (var req = new HttpRequestMessage(HttpMethod.Post, "https://" + domain + "/admin/api/" + ApiVersion + "/graphql.json"))
                {
                    req.Headers.TryAddWithoutValidation("X-Shopify-Access-Token", token);
                    req.Content = new StringContent(payload, Encoding.UTF8, "application/json");
                    using (HttpResponseMessage resp = Http.SendAsync(req).GetAwaiter().GetResult())
                    {
                        string body = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                        if ((int)resp.StatusCode == 429)
                        {
                            Thread.Sleep(2000 * (attempt + 1));
                            continue;
                        }
                        if (!resp.IsSuccessStatusCode)
                        {
                            throw new ShopifyException(Explain(false, (int)resp.StatusCode, body), KindOf(false, (int)resp.StatusCode, body));
                        }
                        var j = (Dictionary<string, object>)ser.DeserializeObject(body);
                        object errors;
                        if (j.TryGetValue("errors", out errors) && errors != null)
                        {
                            if (ser.Serialize(errors).Contains("THROTTLED"))
                            {
                                Thread.Sleep(2000 * (attempt + 1));
                                continue;
                            }
                            throw new ShopifyException(Explain(false, 200, ser.Serialize(errors)), KindOf(false, 200, ser.Serialize(errors)));
                        }
                        WaitForBucket(j);
                        return Dict(j, "data");
                    }
                }
            }
            throw new ShopifyException("Shopify bremst, später erneut.", ShopErrors.TooMany);
        }

        // Shopify reports the remaining query cost. Below a reserve the next
        // query waits until enough is restored, so a long yearly reload never
        // runs into THROTTLED.
        private static void WaitForBucket(Dictionary<string, object> answer)
        {
            const double reserve = 400;
            object ext;
            if (!answer.TryGetValue("extensions", out ext))
            {
                return;
            }
            var throttle = Dict(Dict(ext as Dictionary<string, object>, "cost"), "throttleStatus");
            object available, restore;
            if (!throttle.TryGetValue("currentlyAvailable", out available) || !throttle.TryGetValue("restoreRate", out restore) || available == null || restore == null)
            {
                return;
            }
            double left = Convert.ToDouble(available, CultureInfo.InvariantCulture);
            double rate = Math.Max(1, Convert.ToDouble(restore, CultureInfo.InvariantCulture));
            if (left < reserve)
            {
                Thread.Sleep(TimeSpan.FromSeconds(Math.Min(20, (reserve - left) / rate)));
            }
        }

        // Child object; a plain value comes back under the key "__value".
        private static Dictionary<string, object> Dict(Dictionary<string, object> d, string key)
        {
            object o;
            if (d == null || !d.TryGetValue(key, out o) || o == null)
            {
                return new Dictionary<string, object> { { "__value", null } };
            }
            var child = o as Dictionary<string, object>;
            return child ?? new Dictionary<string, object> { { "__value", o } };
        }

        private static IEnumerable<object> List(Dictionary<string, object> d, string key)
        {
            object o;
            if (d != null && d.TryGetValue(key, out o) && o is object[])
            {
                return (object[])o;
            }
            return new object[0];
        }
    }
}
