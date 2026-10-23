// SysopTV, Copyright (C) 2026 Christoph Scheel
// Licensed under the GNU General Public License v3.0, see LICENSE.
//
// Works in the background for all cubes:
// - every interval (at least 120 s) one query at Anthropic, no matter how many
//   cubes show Claude; cubes with "usage" get the numbers at once. Time of the
//   last query, a 429 block and the last valid values are kept in state.json,
//   so a restart neither asks too early nor shows nothing.
// - every 5 minutes Shopify, if a cube shows the shop
// - every second the pictures for cubes with "claude" or "shop" are drawn;
//   only the changed parts are sent (up to four), a full picture after a
//   restart of the cube and at least every 10 minutes. Without any data no
//   picture goes out.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;

namespace SysopTV.Tray
{
    internal enum StatusKind
    {
        None,   // grey: no data yet
        Ok,     // green: data is being sent
        Warn,   // amber: less than 20 % left or login expired
        Error   // red: error or a cube not reachable
    }

    internal sealed class SenderStatus
    {
        internal StatusKind Kind = StatusKind.None;
        internal string Line = "Noch keine Daten";
        internal string Tooltip = "SysopTV: noch keine Daten";
        internal string Category = "none";  // for balloon tips on changes only
        internal string Detail = "";
        internal DateTime When;
    }

    // One line of the overview.
    internal sealed class CubeRow
    {
        internal string Title;
        internal string Function;
        internal string Design;
        internal DateTime? LastSend;
        internal StatusKind Kind;
        internal string State;
    }

    internal sealed class SenderService : IDisposable
    {
        private const int WarnBelowLeft = 20;
        private static readonly TimeSpan Tick = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan DueTolerance = TimeSpan.FromSeconds(5);
        internal static readonly TimeSpan ShopInterval = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan FullFrameEvery = TimeSpan.FromMinutes(10);
        private static readonly TimeSpan InfoEvery = TimeSpan.FromSeconds(60);

        private sealed class CubeState
        {
            internal Canvas LastSent;
            internal DateTime LastFull = DateTime.MinValue;
            internal DateTime LastInfo = DateTime.MinValue;
            internal long LastUptime = -1;
            internal SendResult LastResult;
            internal DateTime? LastSend;
            internal string ShownDesign;
        }

        private readonly ClaudeClient claude = new ClaudeClient();
        private readonly FailurePolicy policy = new FailurePolicy();
        private readonly ShopifyClient shopify = new ShopifyClient();
        private readonly Timer roundTimer;
        private readonly Timer frameTimer;
        private readonly Dictionary<string, CubeState> states = new Dictionary<string, CubeState>();
        private readonly object gate = new object();
        private readonly string statePath;
        private readonly AppState state;
        private AppConfig config;
        private int roundBusy, frameBusy, shopBusy;
        private bool started;
        private DateTime lastQuery = DateTime.MinValue;
        private DateTime nextRound = DateTime.MinValue;
        private DateTime lastShop = DateTime.MinValue;
        private DateTime? shopOkTime;
        private UsageData lastSent;
        private UsageData lastOk;
        private DateTime? lastOkTime;
        private ShopData shopData;
        private string lastSummary = "";
        private readonly Dictionary<string, string> lastErrors = new Dictionary<string, string>();

        // The query at Anthropic. The offline tests put a fake here.
        internal Func<UsageData> Query;

        internal event Action StatusChanged;

        internal SenderStatus Status { get; private set; }

        internal SenderService(AppConfig config) : this(config, AppState.DefaultPath)
        {
        }

        internal SenderService(AppConfig config, string statePath)
        {
            this.config = config;
            this.statePath = statePath;
            Status = new SenderStatus();
            Query = claude.Query;
            roundTimer = new Timer(_ => OnTick(), null, Timeout.Infinite, Timeout.Infinite);
            frameTimer = new Timer(_ => RunFrames(), null, Timeout.Infinite, Timeout.Infinite);

            state = AppState.Load(statePath);
            lastQuery = AppState.ToLocal(state.LastQuery) ?? DateTime.MinValue;
            claude.BlockedUntil = AppState.ToLocal(state.BlockedUntil) ?? DateTime.MinValue;
            lastOk = state.LastOk();
            lastOkTime = lastOk != null ? AppState.ToLocal(state.LastOkAt) : null;
            if (lastOk != null)
            {
                policy.Seed(lastOk, lastOkTime.Value);
            }
            nextRound = lastQuery == DateTime.MinValue ? DateTime.Now : lastQuery.AddSeconds(config.Interval);
        }

        // Read only by the window.
        internal DateTime? LastQuery
        {
            get { return lastQuery == DateTime.MinValue ? (DateTime?)null : lastQuery; }
        }

        internal DateTime NextQuery
        {
            get
            {
                DateTime next = nextRound;
                return claude.BlockedUntil > next ? claude.BlockedUntil : next;
            }
        }

        internal DateTime? BlockedUntil
        {
            get { return claude.BlockedUntil > DateTime.Now ? claude.BlockedUntil : (DateTime?)null; }
        }

        internal UsageData LastData
        {
            get { return lastSent; }
        }

        internal UsageData LastOk
        {
            get { return lastOk; }
        }

        internal DateTime? LastOkTime
        {
            get { return lastOkTime; }
        }

        internal ShopData Shop
        {
            get { return shopData; }
        }

        internal DateTime? LastShopQuery
        {
            get { return lastShop == DateTime.MinValue ? (DateTime?)null : lastShop; }
        }

        internal void Start()
        {
            Log.Write("Start, Programm " + Program.Version + ", " + config.Cubes.Count + " Würfel, alle " + config.Interval + " Sekunden");
            foreach (CubeEntry c in config.Cubes)
            {
                Log.Write("Würfel " + c.Title + ": " + CubeFunction.Label(c.Function));
            }
            if (nextRound > DateTime.Now)
            {
                Log.Write("Letzte Abfrage " + lastQuery.ToString("HH:mm:ss") + ", nächste frühestens " + nextRound.ToString("HH:mm:ss"));
            }
            if (BlockedUntil.HasValue)
            {
                Log.Write("429-Pause aus dem letzten Lauf bis " + claude.BlockedUntil.ToString("HH:mm"));
            }
            roundTimer.Change(TimeSpan.FromSeconds(2), Tick);
            frameTimer.Change(TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(1));
        }

        // New settings from the window. Pictures of unchanged cubes go on without a full resend.
        internal void ApplyConfig(AppConfig newConfig)
        {
            bool intervalChanged = newConfig.Interval != config.Interval;
            bool shopChanged = newConfig.Shop.Domain != config.Shop.Domain || newConfig.Shop.AccessTokenProtected != config.Shop.AccessTokenProtected ||
                               newConfig.Shop.ClientId != config.Shop.ClientId || newConfig.Shop.ClientSecretProtected != config.Shop.ClientSecretProtected;
            lock (gate)
            {
                config = newConfig;
                foreach (string address in states.Keys.ToList())
                {
                    if (!newConfig.Cubes.Any(c => c.Address == address))
                    {
                        states.Remove(address);
                    }
                }
            }
            if (intervalChanged)
            {
                nextRound = (lastQuery == DateTime.MinValue ? DateTime.Now : lastQuery.AddSeconds(newConfig.Interval));
                Log.Write("Intervall jetzt " + newConfig.Interval + " Sekunden");
            }
            if (shopChanged)
            {
                lastShop = DateTime.MinValue;
            }
        }

        internal void SendNow()
        {
            ThreadPool.QueueUserWorkItem(_ => RunRound(true));
        }

        private bool AnyCube(params string[] functions)
        {
            return config.Cubes.Any(c => functions.Contains(c.Function));
        }

        // Is a query at Anthropic due? Public for the tests.
        internal static bool QueryDue(DateTime now, DateTime lastQuery, int interval, DateTime blockedUntil)
        {
            return now >= blockedUntil && now - lastQuery >= TimeSpan.FromSeconds(interval) - DueTolerance;
        }

        // Every 5 seconds: at the start send what state.json has, then a round when it is due.
        private void OnTick()
        {
            if (!started)
            {
                started = true;
                if (!QueryDue(DateTime.Now, lastQuery, config.Interval, claude.BlockedUntil) && lastOk != null)
                {
                    RunRound(false);
                    return;
                }
            }
            if (DateTime.Now >= nextRound - DueTolerance)
            {
                RunRound(false);
            }
            else if (AnyCube(CubeFunction.ShopFrame) && config.Shop.Configured && DateTime.Now - lastShop >= ShopInterval)
            {
                FetchShop(config);
                UpdateStatus();
            }
        }

        // One round: Anthropic once if due, then usage to every "usage" cube.
        internal void RunRound(bool manual)
        {
            if (Interlocked.Exchange(ref roundBusy, 1) == 1)
            {
                return;
            }
            try
            {
                AppConfig cfg = config;
                if (AnyCube(CubeFunction.Usage, CubeFunction.ClaudeFrame))
                {
                    DateTime now = DateTime.Now;
                    UsageData data;
                    // "Jetzt abfragen" may ask earlier than the interval, but never within 120 s.
                    bool due = QueryDue(now, lastQuery, manual ? AppConfig.MinInterval : cfg.Interval, claude.BlockedUntil);
                    if (!due && manual && lastSent != null)
                    {
                        // Protects against HTTP 429: resend what was sent last, with the current time.
                        data = lastSent;
                    }
                    else if (!due)
                    {
                        // Inside the interval or a 429 block: the last values, if they are recent enough.
                        string reason = BlockedUntil.HasValue ? "429-Pause bis " + claude.BlockedUntil.ToString("HH:mm") : "gespeicherte Werte";
                        data = policy.Resolve(new UsageData { State = "error", Reason = reason }, now);
                    }
                    else
                    {
                        UsageData raw = Query();
                        if (raw.State != "error" || raw.Attempted)
                        {
                            lastQuery = now;
                            nextRound = now.AddSeconds(cfg.Interval);
                        }
                        data = policy.Resolve(raw, now);
                        if (data.State == "ok" && !data.Held)
                        {
                            lastOk = data;
                            lastOkTime = now;
                            state.SetLastOk(data, now);
                        }
                        state.LastQuery = AppState.ToUnix(LastQuery);
                        state.BlockedUntil = AppState.ToUnix(BlockedUntil);
                        state.Save(statePath);
                    }
                    if (nextRound <= now)
                    {
                        nextRound = now.AddSeconds(cfg.Interval);
                    }
                    lastSent = data;
                    string summary = data.Summary();
                    if (summary != lastSummary)
                    {
                        Log.Write("Daten: " + summary);
                        lastSummary = summary;
                    }
                    string json = data.ToJson(DateTimeOffset.Now);
                    foreach (CubeEntry c in cfg.Cubes.Where(c => c.Function == CubeFunction.Usage))
                    {
                        Remember(c, Cube.SendUsage(c.Address, json, c.ApiKey));
                    }
                }
                if (AnyCube(CubeFunction.ShopFrame) && cfg.Shop.Configured && DateTime.Now - lastShop >= ShopInterval)
                {
                    FetchShop(cfg);
                }
                UpdateStatus();
            }
            catch (Exception ex)
            {
                Log.Write("Fehler: " + ex.GetType().Name + ": " + ex.Message);
            }
            finally
            {
                Interlocked.Exchange(ref roundBusy, 0);
            }
        }

        private void FetchShop(AppConfig cfg)
        {
            if (Interlocked.Exchange(ref shopBusy, 1) == 1)
            {
                return;
            }
            try
            {
                lastShop = DateTime.Now;
                ShopData d = shopify.Fetch(cfg.Shop);
                if (d.Error != null)
                {
                    Log.Write("Shopify: " + d.Error);
                    if (shopData != null)
                    {
                        shopData.Error = d.Error;  // keep the last numbers, they get "Stand HH:MM" when old
                    }
                    return;
                }
                shopData = d;
                shopOkTime = DateTime.Now;
            }
            finally
            {
                Interlocked.Exchange(ref shopBusy, 0);
            }
        }

        private void Remember(CubeEntry c, SendResult r)
        {
            string key = c.Address;
            string previous;
            lastErrors.TryGetValue(key, out previous);
            if (!r.Ok && r.Message != previous)
            {
                Log.Write("Würfel " + c.Title + " nicht erreicht: " + r.Message);
            }
            else if (r.Ok && !string.IsNullOrEmpty(previous))
            {
                Log.Write("Würfel " + c.Title + " hat die Daten angenommen.");
            }
            lastErrors[key] = r.Ok ? "" : r.Message;
            lock (gate)
            {
                CubeState st = StateFor(c);
                st.LastResult = r;
                if (r.Ok)
                {
                    st.LastSend = DateTime.Now;
                }
            }
        }

        private CubeState StateFor(CubeEntry c)
        {
            CubeState s;
            if (!states.TryGetValue(c.Address, out s))
            {
                s = new CubeState();
                states[c.Address] = s;
            }
            return s;
        }

        // Every second: draw the pictures, send what changed.
        private void RunFrames()
        {
            if (Interlocked.Exchange(ref frameBusy, 1) == 1)
            {
                return;
            }
            try
            {
                AppConfig cfg = config;
                bool changed = false;
                foreach (CubeEntry c in cfg.Cubes.Where(c => c.Function != CubeFunction.Usage))
                {
                    CubeState st;
                    lock (gate)
                    {
                        st = StateFor(c);
                    }
                    string design = c.DesignAt(DateTime.Now);
                    Canvas canvas = Render(c, design, DateTime.Now);
                    if (canvas == null)
                    {
                        continue;
                    }
                    st.ShownDesign = design;
                    bool full = st.LastSent == null || DateTime.Now - st.LastFull >= FullFrameEvery;
                    if (DateTime.Now - st.LastInfo >= InfoEvery)
                    {
                        st.LastInfo = DateTime.Now;
                        CubeInfo info = Cube.GetInfo(c.Address, TimeSpan.FromSeconds(3));
                        if (info != null)
                        {
                            // Smaller uptime than before: the cube restarted and lost its picture.
                            if (st.LastUptime >= 0 && info.Uptime < st.LastUptime)
                            {
                                full = true;
                            }
                            st.LastUptime = info.Uptime;
                        }
                    }
                    List<Rectangle> parts = full ? new List<Rectangle> { new Rectangle(0, 0, Canvas.Size, Canvas.Size) }
                                                 : FrameCodec.ChangedAreas(st.LastSent, canvas);
                    if (parts.Count == 0)
                    {
                        continue;
                    }
                    SendResult r = null;
                    foreach (Rectangle part in parts)
                    {
                        r = Cube.SendFrame(c.Address, FrameCodec.Encode(canvas, part), c.ApiKey);
                        if (!r.Ok)
                        {
                            break;
                        }
                    }
                    Remember(c, r);
                    if (r.Ok)
                    {
                        st.LastSent = canvas;
                        if (full)
                        {
                            st.LastFull = DateTime.Now;
                        }
                    }
                    else
                    {
                        st.LastSent = null;  // next time a full picture
                    }
                    changed = true;
                }
                if (changed)
                {
                    UpdateStatus();
                }
            }
            catch (Exception ex)
            {
                Log.Write("Fehler beim Bild: " + ex.GetType().Name + ": " + ex.Message);
            }
            finally
            {
                Interlocked.Exchange(ref frameBusy, 0);
            }
        }

        // Values not refreshed within the interval plus a minute count as old.
        internal static string Stamp(DateTime? okTime, TimeSpan freshFor, DateTime now)
        {
            if (!okTime.HasValue)
            {
                return null;
            }
            return now - okTime.Value > freshFor ? UsageView.StampText(okTime.Value) : null;
        }

        // The picture for one cube, or null without any data.
        internal Canvas Render(CubeEntry c, string design, DateTime now)
        {
            if (c.Function == CubeFunction.ClaudeFrame)
            {
                bool login = lastSent != null && lastSent.State == "login";
                if (lastOk == null && !login)
                {
                    return null;
                }
                string stamp = Stamp(lastOkTime, TimeSpan.FromSeconds(config.Interval + 60), now);
                ViewModel v = UsageView.Build(lastOk, login, stamp, c.ShowUsed, new DateTimeOffset(now));
                return ClaudeDesigns.Render(design, v);
            }
            if (c.Function == CubeFunction.ShopFrame && shopData != null)
            {
                var v = new ShopView
                {
                    Data = shopData,
                    ShopName = config.Shop.DisplayName,
                    Now = now,
                    Stamp = Stamp(shopOkTime, ShopInterval + ShopInterval + TimeSpan.FromMinutes(1), now)
                };
                return ShopDesigns.Render(design, v);
            }
            return null;
        }

        // Lines for the overview of the window.
        internal List<CubeRow> Rows()
        {
            var rows = new List<CubeRow>();
            lock (gate)
            {
                foreach (CubeEntry c in config.Cubes)
                {
                    CubeState st;
                    states.TryGetValue(c.Address, out st);
                    var row = new CubeRow { Title = c.Title, Function = CubeFunction.Label(c.Function) };
                    if (c.Function == CubeFunction.Usage)
                    {
                        row.Design = "vom Würfel";
                    }
                    else
                    {
                        string id = st != null && st.ShownDesign != null ? st.ShownDesign : c.DesignOrDefault;
                        DesignInfo info = c.Designs.FirstOrDefault(d => d.Id == id);
                        row.Design = (info != null ? info.Label : id) + (c.CycleSeconds > 0 ? ", Wechsel" : "");
                    }
                    if (st == null || st.LastResult == null)
                    {
                        row.Kind = StatusKind.None;
                        row.State = "noch nichts gesendet";
                    }
                    else if (st.LastResult.Ok)
                    {
                        row.Kind = StatusKind.Ok;
                        row.State = "ok";
                    }
                    else
                    {
                        row.Kind = StatusKind.Error;
                        row.State = st.LastResult.Message;
                    }
                    row.LastSend = st != null ? st.LastSend : null;
                    rows.Add(row);
                }
            }
            return rows;
        }

        private void UpdateStatus()
        {
            var s = new SenderStatus { When = DateTime.Now };
            string time = s.When.ToString("HH:mm:ss");
            List<string> failed;
            lock (gate)
            {
                failed = config.Cubes.Where(c => states.ContainsKey(c.Address) && states[c.Address].LastResult != null &&
                                                 !states[c.Address].LastResult.Ok)
                                     .Select(c => c.Title).ToList();
            }
            UsageData data = lastSent;
            string values = null;
            if (data != null && data.State == "ok")
            {
                values = "Session " + Percent(data.Session) + ", Woche " + Percent(data.Week) + " verbraucht";
            }
            if (failed.Count > 0)
            {
                s.Kind = StatusKind.Error;
                s.Category = "unreachable";
                s.Line = "Nicht erreichbar: " + string.Join(", ", failed) + " (" + time + ")";
                s.Tooltip = "SysopTV: Würfel nicht erreichbar";
                s.Detail = string.Join(", ", failed);
            }
            else if (data != null && data.State == "login")
            {
                s.Kind = StatusKind.Warn;
                s.Category = "login";
                s.Line = "Anmeldung abgelaufen, Claude Code kurz benutzen (" + time + ")";
                s.Tooltip = "SysopTV: Anmeldung abgelaufen";
            }
            else if (data != null && data.State == "error")
            {
                s.Kind = StatusKind.Error;
                s.Category = "error";
                s.Line = "Keine Daten von Anthropic (" + time + ")";
                s.Tooltip = "SysopTV: keine Daten von Anthropic";
                s.Detail = data.Reason;
            }
            else if (values != null)
            {
                s.Kind = Low(data.Session) || Low(data.Week) ? StatusKind.Warn : StatusKind.Ok;
                s.Category = "ok";
                s.Line = values + ", gesendet " + time;
                s.Tooltip = "SysopTV: " + values;
            }
            else if (shopData != null)
            {
                s.Kind = shopData.Error != null ? StatusKind.Error : StatusKind.Ok;
                s.Category = shopData.Error != null ? "error" : "ok";
                s.Line = "Shop: heute " + ShopFormat.Orders(shopData.Today.Orders) + ", " + ShopFormat.Money(shopData.Today.Revenue) + " (" + time + ")";
                s.Tooltip = "SysopTV: Shop heute " + ShopFormat.Orders(shopData.Today.Orders);
                s.Detail = shopData.Error ?? "";
            }
            Status = s;
            Action handler = StatusChanged;
            if (handler != null)
            {
                handler();
            }
        }

        private static string Percent(UsageLimit l)
        {
            return l == null ? "--" : l.Used + " %";
        }

        private static bool Low(UsageLimit l)
        {
            return l != null && 100 - l.Used < WarnBelowLeft;
        }

        public void Dispose()
        {
            roundTimer.Dispose();
            frameTimer.Dispose();
        }
    }
}
