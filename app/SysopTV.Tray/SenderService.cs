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
            internal string Firmware;  // version from /api/info, null until known
            internal bool? FrameValid; // the cube shows our whole picture (firmware 0.4.3 and newer), null if unknown
            internal DateTime LastFrameSend = DateTime.MinValue;
        }

        private readonly ClaudeClient claude = new ClaudeClient();
        private readonly FailurePolicy policy = new FailurePolicy();
        private readonly ShopifyClient shopify = new ShopifyClient();
        private readonly WooClient woo = new WooClient();

        // One shop source: its last valid numbers, when it was asked and how
        // long it asked to wait (Retry-After).
        private sealed class ShopFeed
        {
            internal ShopData Data;
            internal DateTime? OkTime;
            internal DateTime LastQuery = DateTime.MinValue;
            internal DateTime BlockedUntil = DateTime.MinValue;
            internal string LastError;
            internal int Busy;
        }

        private readonly Dictionary<string, ShopFeed> feeds = new Dictionary<string, ShopFeed>
        {
            { ShopSources.Shopify, new ShopFeed() },
            { ShopSources.WooCommerce, new ShopFeed() }
        };

        // The query at a shop source. The offline tests put a fake here.
        internal Func<string, AppConfig, ShopData> FetchShopFrom;
        private readonly Timer roundTimer;
        private readonly Timer frameTimer;
        private readonly Dictionary<string, CubeState> states = new Dictionary<string, CubeState>();
        private readonly object gate = new object();
        private readonly string statePath;
        private readonly AppState state;
        private AppConfig config;
        private int roundBusy, frameBusy;
        private bool started;
        private DateTime lastQuery = DateTime.MinValue;
        private DateTime nextRound = DateTime.MinValue;
        private UsageData lastSent;
        private UsageData lastOk;
        private DateTime? lastOkTime;
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
            FetchShopFrom = (source, cfg) => source == ShopSources.WooCommerce ? woo.Fetch(cfg.Woo) : shopify.Fetch(cfg.Shop);
            foreach (KeyValuePair<string, ShopFeed> f in feeds)
            {
                // Pictures can start with the numbers of the last run, they get "Stand HH:MM".
                f.Value.Data = state.Shop(f.Key);
                f.Value.OkTime = f.Value.Data != null ? AppState.ToLocal(state.ShopOkAt(f.Key)) : null;
            }
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

        internal ShopData ShopOf(string source)
        {
            return feeds[source].Data;
        }

        internal DateTime? LastShopQueryOf(string source)
        {
            DateTime t = feeds[source].LastQuery;
            return t == DateTime.MinValue ? (DateTime?)null : t;
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
            bool wooChanged = newConfig.Woo.Url != config.Woo.Url || newConfig.Woo.KeyProtected != config.Woo.KeyProtected;
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
                feeds[ShopSources.Shopify].LastQuery = DateTime.MinValue;
                feeds[ShopSources.Shopify].BlockedUntil = DateTime.MinValue;
            }
            if (wooChanged)
            {
                feeds[ShopSources.WooCommerce].LastQuery = DateTime.MinValue;
                feeds[ShopSources.WooCommerce].BlockedUntil = DateTime.MinValue;
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
            else if (FetchShops(config))
            {
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
                    foreach (CubeEntry c in cfg.Cubes.Where(c => c.Function == CubeFunction.Usage || NumbersWelcome(c)))
                    {
                        Remember(c, Cube.SendUsage(c.Address, json, c.ApiKey));
                    }
                }
                FetchShops(cfg);
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

        // Every source a cube shows is asked every 5 minutes, not while it asked
        // to wait. Returns true if a source was asked.
        internal bool FetchShops(AppConfig cfg)
        {
            bool asked = false;
            DateTime now = DateTime.Now;
            foreach (DesignInfo source in ShopSources.All)
            {
                ShopFeed feed = feeds[source.Id];
                bool shown = cfg.Cubes.Any(c => c.Function == CubeFunction.ShopFrame && c.ShopSource == source.Id);
                if (shown && cfg.SourceConfigured(source.Id) && now - feed.LastQuery >= ShopInterval && now >= feed.BlockedUntil)
                {
                    FetchShop(source.Id, cfg, now);
                    asked = true;
                }
            }
            return asked;
        }

        private void FetchShop(string source, AppConfig cfg, DateTime now)
        {
            ShopFeed feed = feeds[source];
            if (Interlocked.Exchange(ref feed.Busy, 1) == 1)
            {
                return;
            }
            try
            {
                feed.LastQuery = now;
                ShopData d = FetchShopFrom(source, cfg);
                string label = ShopBig.Label(ShopSources.All, source);
                if (d.Error != null)
                {
                    if (d.Error != feed.LastError)
                    {
                        Log.Write(label + ": " + d.Error);
                    }
                    feed.LastError = d.Error;
                    if (d.RetryAfterSeconds > 0)
                    {
                        feed.BlockedUntil = now.AddSeconds(d.RetryAfterSeconds);
                    }
                    if (feed.Data != null)
                    {
                        feed.Data.Error = d.Error;  // keep the last numbers, they get "Stand HH:MM" when old
                    }
                    return;
                }
                if (feed.LastError != null)
                {
                    Log.Write(label + ": wieder erreichbar");
                }
                feed.LastError = null;
                feed.Data = d;
                feed.OkTime = now;
                state.SetShop(source, d, now);
                state.Save(statePath);
            }
            finally
            {
                Interlocked.Exchange(ref feed.Busy, 0);
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

        // "Claude als Bild" gets the numbers as well, so the cube can fall back to
        // its own design. Only firmware 0.4.2 and newer keeps pictures in front of
        // the numbers; older firmware would leave picture mode on every packet.
        private bool NumbersWelcome(CubeEntry c)
        {
            if (c.Function != CubeFunction.ClaudeFrame)
            {
                return false;
            }
            CubeState st;
            lock (gate)
            {
                st = StateFor(c);
            }
            if (st.Firmware == null)
            {
                CubeInfo info = Cube.GetInfo(c.Address, TimeSpan.FromSeconds(3));
                if (info == null)
                {
                    return false;
                }
                st.Firmware = info.Version;
            }
            return CubeInfo.CompareVersion(st.Firmware, 0, 4, 2) >= 0;
        }

        // Name of the picture for the overview page of the cube.
        internal static string SourceText(CubeEntry c, string picture)
        {
            string label = c.PictureLabel(picture);
            return c.Function == CubeFunction.ShopFrame ? "Shop: " + label : "Claude als Bild: " + label;
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
                    changed |= FrameRound(c, DateTime.Now);
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

        // Sending a part and reading /api/info. The offline tests put fakes here.
        internal Func<string, byte[], string, string, SendResult> SendFrameTo = Cube.SendFrame;
        internal Func<string, CubeInfo> ReadInfo = address => Cube.GetInfo(address, TimeSpan.FromSeconds(3));

        // A picture at least this often, so the cube never leaves picture mode
        // (it falls back after 3 minutes without one).
        internal static readonly TimeSpan KeepAlive = TimeSpan.FromSeconds(60);

        // Firmware before 0.4.3 cannot say whether its screen still shows our
        // picture. After this long without a picture it may have fallen back.
        private static readonly TimeSpan MaybeFallenBack = TimeSpan.FromSeconds(170);

        private static readonly Rectangle WholeScreen = new Rectangle(0, 0, Canvas.Size, Canvas.Size);

        // A small part in the corner, which is background in every style.
        private static readonly Rectangle KeepAlivePart = new Rectangle(0, 0, 8, 8);

        // The cube answered 409: its screen is not our picture, only a full one helps.
        internal static bool NeedsFullFrame(SendResult r)
        {
            return r != null && !r.Ok && r.Status == 409 && (r.Message ?? "").Contains("full_frame_required");
        }

        // One second for one picture cube: draw, decide what to send, send it.
        // Returns true if something was sent.
        internal bool FrameRound(CubeEntry c, DateTime now)
        {
            CubeState st;
            lock (gate)
            {
                st = StateFor(c);
            }
            string design = c.PictureAt(now);
            Canvas canvas = Render(c, design, now);
            if (canvas == null)
            {
                return false;
            }
            st.ShownDesign = design;
            bool full = st.LastSent == null || now - st.LastFull >= FullFrameEvery;
            if (now - st.LastInfo >= InfoEvery)
            {
                st.LastInfo = now;
                CubeInfo info = ReadInfo(c.Address);
                if (info != null)
                {
                    // Smaller uptime than before: the cube restarted and lost its picture.
                    if (st.LastUptime >= 0 && info.Uptime < st.LastUptime)
                    {
                        full = true;
                    }
                    st.LastUptime = info.Uptime;
                    st.Firmware = info.Version;
                    st.FrameValid = info.FrameValid;
                }
            }
            bool knowsValid = st.Firmware != null && CubeInfo.CompareVersion(st.Firmware, 0, 4, 3) >= 0;
            TimeSpan quiet = now - st.LastFrameSend;
            // Never a part on a screen the cube drew itself.
            if (st.FrameValid == false || (!knowsValid && quiet >= MaybeFallenBack))
            {
                full = true;
            }
            List<Rectangle> parts = full ? new List<Rectangle> { WholeScreen } : FrameCodec.ChangedAreas(st.LastSent, canvas);
            if (parts.Count == 0)
            {
                if (quiet < KeepAlive)
                {
                    return false;
                }
                // Sign of life: a tiny part if the cube reports a valid picture,
                // otherwise (and for firmware before 0.4.3) the full picture.
                full = !(knowsValid && st.FrameValid == true);
                parts = new List<Rectangle> { full ? WholeScreen : KeepAlivePart };
            }
            SendResult r = null;
            string source = SourceText(c, design);
            // Fallblatt: the changing cards flip over in three small steps first.
            if (!full && design == "fallblatt" && c.Animate)
            {
                Canvas shown = st.LastSent;
                foreach (Canvas frame in FlipAnimation.Frames(st.LastSent, canvas))
                {
                    r = SendParts(c, frame, FrameCodec.ChangedAreas(shown, frame), source);
                    if (!r.Ok)
                    {
                        break;
                    }
                    shown = frame;
                    Thread.Sleep(FlipAnimation.StepMilliseconds);
                }
                if (r == null || r.Ok)
                {
                    parts = FrameCodec.ChangedAreas(shown, canvas);
                }
            }
            if (r == null || r.Ok)
            {
                r = parts.Count > 0 ? SendParts(c, canvas, parts, source) : new SendResult { Ok = true };
            }
            if (NeedsFullFrame(r))
            {
                // At once, not in the next round: otherwise the cube shows a mixed picture.
                Log.Write("Würfel " + c.Title + " verlangt ein volles Bild, wird gesendet");
                full = true;
                r = SendParts(c, canvas, new List<Rectangle> { WholeScreen }, source);
            }
            Remember(c, r);
            if (r.Ok)
            {
                st.LastSent = canvas;
                st.LastFrameSend = now;
                if (full)
                {
                    st.LastFull = now;
                    st.FrameValid = knowsValid ? true : (bool?)null;
                }
            }
            else
            {
                st.LastSent = null;  // next time a full picture
            }
            return true;
        }

        private SendResult SendParts(CubeEntry c, Canvas canvas, List<Rectangle> parts, string source)
        {
            SendResult r = new SendResult { Ok = true };
            foreach (Rectangle part in parts)
            {
                r = SendFrameTo(c.Address, FrameCodec.Encode(canvas, part), c.ApiKey, source);
                if (!r.Ok)
                {
                    break;
                }
            }
            return r;
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
            if (c.Function != CubeFunction.ShopFrame)
            {
                return null;
            }
            // The same views for Shopify and WooCommerce, only the source differs.
            ShopFeed feed = feeds[c.ShopSource];
            if (feed.Data == null)
            {
                return null;
            }
            string name = config.SourceName(c.ShopSource);
            string shopStamp = Stamp(feed.OkTime, ShopInterval + ShopInterval + TimeSpan.FromMinutes(1), now);
            if (c.ShopBigView)
            {
                var big = new ShopBigView
                {
                    Data = feed.Data,
                    ShopName = name,
                    Now = now,
                    Stamp = shopStamp,
                    Period = c.PeriodAt(now),
                    Content = c.BigContent
                };
                return ShopBig.Render(design, big);
            }
            var details = new ShopView { Data = feed.Data, ShopName = name, Now = now, Stamp = shopStamp };
            return ShopDesigns.Render(design, details);
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
                    string function = CubeFunction.Label(c.Function) + (c.Function == CubeFunction.ShopFrame ? " (" + ShopBig.Label(ShopSources.All, c.ShopSource) + ")" : "");
                    var row = new CubeRow { Title = c.Title, Function = function };
                    if (c.Function == CubeFunction.Usage)
                    {
                        row.Design = "vom Würfel";
                    }
                    else
                    {
                        string id = st != null && st.ShownDesign != null ? st.ShownDesign : c.PictureAt(DateTime.Now);
                        bool change = c.ShopBigView ? c.PeriodCycleSeconds > 0 : c.CycleSeconds > 0;
                        row.Design = c.PictureLabel(id) + (change ? ", Wechsel" : "");
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

        private bool legacyRunning;

        // Set by the check for the old PowerShell sender every 10 minutes.
        internal void SetLegacyRunning(bool running)
        {
            if (running == legacyRunning)
            {
                return;
            }
            legacyRunning = running;
            UpdateStatus();
        }

        internal bool LegacyRunning
        {
            get { return legacyRunning; }
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
                values = UsageLimit.Short(data.Session, true) + ", " + UsageLimit.Short(data.Week, false) + " verbraucht";
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
            else if (feeds.Values.Any(f => f.Data != null))
            {
                ShopData shop = feeds.Values.First(f => f.Data != null).Data;
                s.Kind = shop.Error != null ? StatusKind.Error : StatusKind.Ok;
                s.Category = shop.Error != null ? "error" : "ok";
                s.Line = "Shop: heute " + ShopFormat.Orders(shop.Today.Orders) + ", " + ShopFormat.Money(shop.Today.Revenue) + " (" + time + ")";
                s.Tooltip = "SysopTV: Shop heute " + ShopFormat.Orders(shop.Today.Orders);
                s.Detail = shop.Error ?? "";
            }
            if (legacyRunning)
            {
                // The old script sends as well: shown first, in the warning colour.
                s.Line = "Altes PowerShell-Skript sendet parallel. " + s.Line;
                s.Tooltip = "SysopTV: altes PowerShell-Skript sendet parallel";
                if (s.Kind != StatusKind.Error)
                {
                    s.Kind = StatusKind.Warn;
                }
            }
            Status = s;
            Action handler = StatusChanged;
            if (handler != null)
            {
                handler();
            }
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
