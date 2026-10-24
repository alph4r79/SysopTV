// SysopTV, Copyright (C) 2026 Christoph Scheel
// Licensed under the GNU General Public License v3.0, see LICENSE.
//
// Works in the background for all cubes:
// - every interval (at least 120 s) one query at Anthropic, no matter how many
//   cubes show Claude Limits, also without such a cube if limit warnings are
//   on. Time of the last query, a 429 block and the last valid values are
//   kept in state.json, so a restart neither asks too early nor shows nothing.
// - every 5 minutes Shopify or WooCommerce, if a cube shows that shop
// - every second the pictures for all cubes with picture mode are drawn;
//   only the changed parts are sent (up to four), a full picture after a
//   restart of the cube, on every change between two functions and at least
//   every 10 minutes. Without any data the cube gets a notice. The program
//   sends no numbers to the cube any more; cubes without picture mode
//   (firmware before 0.4.0) get nothing until their firmware is updated.
// - on exit, at the end of the Windows session and before sleep every cube
//   gets NO CARRIER at once (SignOff), during a firmware update the cube gets
//   no pictures.
// - once a day the update check, if it is switched on.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
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
            internal bool? CanFrame;   // picture mode (firmware 0.4.0 and newer), null until /api/info answered
            internal bool? CanNoCarrier; // POST /api/nocarrier (firmware 0.5.0 and newer), null until known
            internal int ShownView = -1; // 0 first function, 1 second function, -1 nothing yet
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
            internal string LastKind;
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
        private bool timersRunning;

        // True once the timers run; the offline tests never start them.
        private bool Started
        {
            get { return timersRunning; }
        }
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

        // A cube taken over from "usage" got the design of the cube (address).
        // The tray saves the settings then.
        internal event Action<string> CubeAdopted;

        // A limit warning to show as a Windows message.
        internal event Action<string> LimitAlert;

        // The update check found a newer program or a newer firmware (shown once per version).
        internal event Action<ReleaseInfo> UpdateFound;
        internal event Action<ReleaseInfo> FirmwareFound;

        // The answer of the GitHub API. The offline tests put a fake here.
        internal Func<string> FetchRelease = UpdateCheck.Fetch;

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
            shopify.Cache = state.ShopifyYear ?? new YearCache();
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
                Log.Write("Würfel " + c.Title + ": " + SourceText(c, c.PictureAt(DateTime.Now)));
            }
            if (nextRound > DateTime.Now)
            {
                Log.Write("Letzte Abfrage " + lastQuery.ToString("HH:mm:ss") + ", nächste frühestens " + nextRound.ToString("HH:mm:ss"));
            }
            if (BlockedUntil.HasValue)
            {
                Log.Write("429-Pause aus dem letzten Lauf bis " + claude.BlockedUntil.ToString("HH:mm"));
            }
            timersRunning = true;
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
            bool redraw = false;
            lock (gate)
            {
                foreach (CubeEntry c in newConfig.Cubes)
                {
                    CubeEntry before = config.Cubes.FirstOrDefault(o => o.Address == c.Address);
                    CubeState st;
                    // Another function or view: a full picture of the new one at once.
                    if (before != null && states.TryGetValue(c.Address, out st) &&
                        (before.Function != c.Function || before.ShopView != c.ShopView))
                    {
                        st.LastSent = null;
                        redraw = true;
                    }
                }
                config = newConfig;
                foreach (string address in states.Keys.ToList())
                {
                    if (!newConfig.Cubes.Any(c => c.Address == address))
                    {
                        states.Remove(address);
                    }
                }
            }
            if (redraw && Started)
            {
                ThreadPool.QueueUserWorkItem(_ => RunFrames());
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

        // A cube shows this function, as first or second function.
        private bool AnyCube(string function)
        {
            return config.Cubes.Any(c => c.Views.Any(v => v.Function == function));
        }

        // Is a query at Anthropic due? Public for the tests.
        internal static bool QueryDue(DateTime now, DateTime lastQuery, int interval, DateTime blockedUntil)
        {
            return now >= blockedUntil && now - lastQuery >= TimeSpan.FromSeconds(interval) - DueTolerance;
        }

        // Every 5 seconds: at the start send what state.json has, then a round when it is due.
        private void OnTick()
        {
            if (suspended)
            {
                return;
            }
            if (UpdateCheck.Due(config.UpdateCheck, state.UpdateCheckedAt, DateTimeOffset.Now) && DateTime.Now >= nextUpdateTry)
            {
                ThreadPool.QueueUserWorkItem(_ => CheckForUpdate(false));
            }
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

        private int updateBusy;
        private DateTime nextUpdateTry = DateTime.MinValue;  // after a check without network

        private volatile bool manualCheckDone;

        // Asks GitHub for the latest release: automatically at most once a day
        // while the update check is on, or once after "Jetzt nach Updates
        // suchen" (manual, also with the check switched off). Without network it
        // says nothing and the automatic check tries again in an hour. Each new
        // program and each new firmware is reported once.
        internal UpdateCheckResult CheckForUpdate(bool manual)
        {
            var result = new UpdateCheckResult();
            if (Interlocked.Exchange(ref updateBusy, 1) == 1)
            {
                return result;
            }
            try
            {
                long now = DateTimeOffset.Now.ToUnixTimeSeconds();
                string json = FetchRelease();
                if (manual)
                {
                    manualCheckDone = true;
                }
                if (string.IsNullOrEmpty(json))
                {
                    nextUpdateTry = DateTime.Now.AddHours(1);
                    return result;
                }
                result.Reached = true;
                state.UpdateCheckedAt = now;
                ReleaseInfo r = UpdateCheck.Parse(json);
                if (r == null)
                {
                    // A draft, a pre-release or no usable files: nothing new.
                    state.Save(statePath);
                    return result;
                }
                result.Release = r;
                state.UpdateCheckedAt = now;
                state.UpdateRelease = r;
                result.AppNewer = UpdateCheck.Newer(r.AppVersion, Program.Version);
                result.FirmwareNewer = UpdateCheck.Newer(r.FirmwareVersion, FirmwareBaseline());
                bool showApp = result.AppNewer && state.UpdateNotified != r.AppVersion;
                bool showFirmware = result.FirmwareNewer && state.UpdateNotifiedFirmware != r.FirmwareVersion;
                if (showApp)
                {
                    state.UpdateNotified = r.AppVersion;
                    Log.Write("Neue Version " + r.AppVersion + " auf GitHub");
                }
                if (showFirmware)
                {
                    state.UpdateNotifiedFirmware = r.FirmwareVersion;
                    Log.Write("Neue Firmware " + r.FirmwareVersion + " auf GitHub");
                }
                state.Save(statePath);
                Action<ReleaseInfo> app = UpdateFound;
                if (showApp && app != null)
                {
                    app(r);
                }
                Action<ReleaseInfo> fw = FirmwareFound;
                if (showFirmware && fw != null)
                {
                    fw(r);
                }
            }
            catch (Exception ex)
            {
                Log.Write("Update-Prüfung: " + ex.GetType().Name);
            }
            finally
            {
                Interlocked.Exchange(ref updateBusy, 0);
            }
            return result;
        }

        // The firmware a new release is compared with: the oldest SysopTV
        // firmware of the cubes, or the firmware that comes with the program.
        private string FirmwareBaseline()
        {
            string oldest = null;
            lock (gate)
            {
                foreach (CubeState st in states.Values)
                {
                    if (st.Firmware != null && (oldest == null || UpdateCheck.Newer(oldest, st.Firmware)))
                    {
                        oldest = st.Firmware;
                    }
                }
            }
            if (oldest != null)
            {
                return oldest;
            }
            BundledFirmware b = BundledFirmware.Find();
            return b != null ? b.Version : "0.0.0";
        }

        // The release of the last check, but only while updates may be used:
        // the update check is on, or "Jetzt nach Updates suchen" ran in this session.
        internal ReleaseInfo UsableRelease
        {
            get { return config.UpdateCheck || manualCheckDone ? state.UpdateRelease : null; }
        }

        // The release, if its program is newer than this one.
        internal ReleaseInfo NewerRelease
        {
            get
            {
                ReleaseInfo r = UsableRelease;
                return r != null && UpdateCheck.Newer(r.AppVersion, Program.Version) ? r : null;
            }
        }

        internal DateTime? UpdateCheckedAt
        {
            get { return AppState.ToLocal(state.UpdateCheckedAt); }
        }

        // One round: Anthropic once if due (for the pictures and the limit
        // warnings), then the shops.
        internal void RunRound(bool manual)
        {
            if (Interlocked.Exchange(ref roundBusy, 1) == 1)
            {
                return;
            }
            try
            {
                AppConfig cfg = config;
                if (AnyCube(CubeFunction.Claude) || cfg.Notify.Enabled)
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
                        List<string> alerts = new List<string>();
                        if (data.State == "ok" && !data.Held)
                        {
                            lastOk = data;
                            lastOkTime = now;
                            state.SetLastOk(data, now);
                            alerts = LimitNotifier.Check(data.Session, data.Week, cfg.Notify, state, DateTimeOffset.Now);
                        }
                        state.LastQuery = AppState.ToUnix(LastQuery);
                        state.BlockedUntil = AppState.ToUnix(BlockedUntil);
                        state.Save(statePath);
                        Action<string> alert = LimitAlert;
                        foreach (string text in alerts)
                        {
                            Log.Write("Hinweis: " + text);
                            if (alert != null)
                            {
                                alert(text);
                            }
                        }
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
                bool shown = cfg.Cubes.Any(c => c.Views.Any(v => v.Function == source.Id));
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
                    feed.LastKind = d.ErrorKind;
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
                feed.LastKind = null;
                feed.Data = d;
                feed.OkTime = now;
                state.SetShop(source, d, now);
                if (source == ShopSources.Shopify)
                {
                    state.ShopifyYear = shopify.Cache;
                }
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

        // What /api/info says about a cube.
        private void Learn(CubeEntry c, CubeState st, CubeInfo info)
        {
            st.Firmware = info.Version;
            st.FrameValid = info.FrameValid;
            st.CanFrame = info.CanFrame;
            st.CanNoCarrier = info.CanNoCarrier;
            if (Adopt(c, info))
            {
                Log.Write("Würfel " + c.Title + ": Design vom Würfel übernommen, " + SourceText(c, c.DesignOrDefault));
                Action<string> handler = CubeAdopted;
                if (handler != null)
                {
                    handler(c.Address);
                }
            }
        }

        // A cube taken over from "usage" gets the design, used or remaining and
        // the change the cube showed itself, once. Without a design in
        // /api/info (firmware before 0.3.1) it stays DOS. Public for the tests.
        internal static bool Adopt(CubeEntry c, CubeInfo info)
        {
            if (!c.AdoptCubeDesign || info == null)
            {
                return false;
            }
            DesignInfo d = ClaudeDesigns.All.Take(ClaudeDesigns.FirmwareCount).FirstOrDefault(x => x.Label == info.Design);
            c.Design = d != null ? d.Id : ClaudeDesigns.Default;
            if (info.ShowUsed.HasValue)
            {
                c.ShowUsed = info.ShowUsed.Value;
            }
            if (info.DesignCycle.HasValue && Array.IndexOf(CubeEntry.CycleChoices, info.DesignCycle.Value) >= 0)
            {
                c.CycleSeconds = info.DesignCycle.Value;
            }
            c.AdoptCubeDesign = false;
            return true;
        }

        // At most this many bytes of the name reach the cube (firmware: SYSOPTV_FRAME_SOURCE_MAX).
        private const int SourceMax = 32;

        // Name of the picture for the overview page of the cube and the log, like
        // "Claude Limits: DOS", "Shopify: Großanzeige Kassenbon" or
        // "WooCommerce: Tabelle". Too long for the cube: without "Großanzeige",
        // then cut after a whole word.
        internal static string SourceText(CubeEntry c, string picture)
        {
            string head = CubeFunction.Label(c.Function) + ": ";
            string text = head + c.PictureLabel(picture);
            if (Encoding.UTF8.GetByteCount(text) > SourceMax && c.ShopBigView)
            {
                text = head + ShopBig.Label(ShopBig.Styles, picture);
            }
            while (Encoding.UTF8.GetByteCount(text) > SourceMax && text.LastIndexOf(' ') > head.Length)
            {
                text = text.Substring(0, text.LastIndexOf(' '));
            }
            return text;
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
                foreach (CubeEntry c in cfg.Cubes)
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

        // Sending a part, NO CARRIER and reading /api/info. The offline tests put fakes here.
        internal Func<string, byte[], string, string, SendResult> SendFrameTo = Cube.SendFrame;
        internal Func<string, string, TimeSpan, SendResult> SendNoCarrierTo = Cube.SendNoCarrier;
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

        // One second for one cube: draw, decide what to send, send it. Cubes
        // without picture mode get only the numbers (RunRound). Returns true if
        // something was sent or the state changed.
        internal bool FrameRound(CubeEntry c, DateTime now)
        {
            if (signedOff || Updating(c.Address))
            {
                return false;
            }
            CubeState st;
            lock (gate)
            {
                st = StateFor(c);
            }
            bool restarted = false;
            if (now - st.LastInfo >= InfoEvery)
            {
                st.LastInfo = now;
                CubeInfo info = ReadInfo(c.Address);
                if (info != null)
                {
                    // Smaller uptime than before: the cube restarted and lost its picture.
                    restarted = st.LastUptime >= 0 && info.Uptime < st.LastUptime;
                    st.LastUptime = info.Uptime;
                    Learn(c, st, info);
                }
                else if (st.CanFrame == null)
                {
                    Remember(c, new SendResult { Ok = false, Message = "keine Antwort von SysopTV" });
                    return true;
                }
            }
            if (st.CanFrame != true)
            {
                return false;
            }
            // With a second function: the one to show now, the other one only
            // while this one has no data.
            CubeEntry view = c.ViewAt(now, HasData);
            int viewIndex = view == c ? 0 : 1;
            string design = view.PictureAt(now);
            Canvas canvas = Render(view, design, now);
            if (canvas == null)
            {
                return false;
            }
            bool switched = st.ShownView != viewIndex;
            st.ShownView = viewIndex;
            st.ShownDesign = design;
            // Another function: always a full picture.
            bool full = switched || restarted || st.LastSent == null || now - st.LastFull >= FullFrameEvery;
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
            string source = SourceText(view, design);
            // Fallblatt: the changing cards flip over in three small steps first.
            if (!full && design == "fallblatt" && view.Animate)
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

        // Does this function have numbers to show (or the login notice of Claude)?
        internal bool HasData(CubeEntry view)
        {
            if (view.Function == CubeFunction.Claude)
            {
                return lastOk != null || (lastSent != null && lastSent.State == "login");
            }
            return view.IsShop && feeds[view.ShopSource].Data != null;
        }

        // The picture for one function of a cube (the cube itself or its
        // second function), a notice without any data.
        internal Canvas Render(CubeEntry c, string design, DateTime now)
        {
            if (c.Function == CubeFunction.Claude)
            {
                bool login = lastSent != null && lastSent.State == "login";
                if (lastOk == null && !login)
                {
                    return ClaudeNotice(design, now);
                }
                string stamp = Stamp(lastOkTime, TimeSpan.FromSeconds(config.Interval + 60), now);
                ViewModel v = UsageView.Build(lastOk, login, stamp, c.ShowUsed, new DateTimeOffset(now));
                return ClaudeDesigns.Render(design, v);
            }
            if (!c.IsShop)
            {
                return null;
            }
            // The same views for Shopify and WooCommerce, only the source differs.
            ShopFeed feed = feeds[c.ShopSource];
            if (feed.Data == null)
            {
                return ShopNotice(c, design, feed, now);
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
                    Content = c.BigContent,
                    Source = c.ShopSource
                };
                return ShopBig.Render(design, big);
            }
            var details = new ShopView { Data = feed.Data, ShopName = name, Now = now, Stamp = shopStamp, Source = c.ShopSource };
            return ShopDesigns.Render(design, details);
        }

        // No valid Claude numbers at all: a notice instead of no picture, so
        // the cube never falls back to its own design with old numbers.
        private Canvas ClaudeNotice(string design, DateTime now)
        {
            if (lastSent == null)
            {
                return Notice.Render(design, "CLAUDE", "Claude-Daten werden geladen", null, now);
            }
            return Notice.Render(design, "CLAUDE", "Keine Daten von Anthropic", "nächster Versuch " + NextQuery.ToString("HH:mm"), now);
        }

        // No valid shop numbers at all: what is going on, in the style of the cube.
        internal Canvas ShopNotice(CubeEntry c, string design, DateTime now)
        {
            return ShopNotice(c, design, feeds[c.ShopSource], now);
        }

        private Canvas ShopNotice(CubeEntry c, string design, ShopFeed feed, DateTime now)
        {
            string heading = ShopBig.Label(ShopSources.All, c.ShopSource).ToUpperInvariant();
            DateTime next = feed.LastQuery.Add(ShopInterval);
            if (feed.BlockedUntil > next)
            {
                next = feed.BlockedUntil;
            }
            string retry = "nächster Versuch " + next.ToString("HH:mm");
            if (!config.SourceConfigured(c.ShopSource))
            {
                return Notice.Render(design, heading, "Shop nicht eingetragen", "im Programm einrichten", now);
            }
            if (feed.LastQuery == DateTime.MinValue || feed.LastError == null)
            {
                return Notice.Render(design, heading, "Shopdaten werden geladen", null, now);
            }
            switch (feed.LastKind)
            {
                case ShopErrors.TooMany:
                    return Notice.Render(design, heading, "Zu viele Anfragen", retry, now);
                case ShopErrors.Access:
                    return Notice.Render(design, heading, c.ShopSource == ShopSources.WooCommerce ? "Schlüssel ungültig" : "Zugang prüfen", "im Programm prüfen", now);
                case ShopErrors.Offline:
                    return Notice.Render(design, heading, "Shop nicht erreichbar", retry, now);
                default:
                    return Notice.Render(design, heading, "Shop meldet einen Fehler", retry, now);
            }
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
                    var row = new CubeRow { Title = c.Title, Function = FunctionText(c) };
                    bool oldFirmware = st != null && st.CanFrame == false;
                    if (oldFirmware)
                    {
                        row.Design = "Firmware zu alt";
                    }
                    else
                    {
                        var designs = new List<string>();
                        int index = 0;
                        foreach (CubeEntry v in c.Views)
                        {
                            bool shown = st != null && st.ShownDesign != null && st.ShownView == index;
                            designs.Add(DesignText(v, shown ? st.ShownDesign : v.PictureAt(DateTime.Now)));
                            index++;
                        }
                        row.Design = string.Join(" / ", designs);
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

        // "Claude Limits" or, with a second function, "Claude Limits / Shopify (20 s)".
        internal static string FunctionText(CubeEntry c)
        {
            if (!c.Alternates)
            {
                return CubeFunction.Label(c.Function);
            }
            return CubeFunction.Label(c.Function) + " / " + CubeFunction.Label(c.Second.Function) + " (" + c.AlternateSeconds + " s)";
        }

        private static string DesignText(CubeEntry v, string id)
        {
            bool change = v.ShopBigView ? v.PeriodCycleSeconds > 0 && !ShopBig.ShowsAllPeriods(id) : v.CycleSeconds > 0;
            return v.PictureLabel(id) + (change ? ", Wechsel" : "");
        }

        // Firmware version of a cube from /api/info, null until known.
        internal string FirmwareOf(string address)
        {
            lock (gate)
            {
                CubeState st;
                return states.TryGetValue(address, out st) ? st.Firmware : null;
            }
        }

        // ---- firmware update: no pictures to this cube meanwhile ----

        private readonly HashSet<string> updating = new HashSet<string>();

        internal void BeginUpdate(string address)
        {
            lock (gate)
            {
                updating.Add(address);
            }
            // A picture that is just being sent finishes first.
            WaitForFrames(TimeSpan.FromSeconds(5));
        }

        // After the update everything is read again and a full picture follows.
        internal void EndUpdate(string address)
        {
            lock (gate)
            {
                updating.Remove(address);
                CubeState st;
                if (states.TryGetValue(address, out st))
                {
                    Forget(st);
                }
            }
        }

        private bool Updating(string address)
        {
            lock (gate)
            {
                return updating.Contains(address);
            }
        }

        private static void Forget(CubeState st)
        {
            st.LastSent = null;
            st.LastInfo = DateTime.MinValue;
            st.LastUptime = -1;
            st.FrameValid = null;
            st.CanFrame = null;
            st.CanNoCarrier = null;
            st.Firmware = null;
            st.ShownView = -1;
        }

        private void WaitForFrames(TimeSpan max)
        {
            DateTime until = DateTime.Now + max;
            while (Volatile.Read(ref frameBusy) == 1 && DateTime.Now < until)
            {
                Thread.Sleep(20);
            }
        }

        // ---- NO CARRIER on exit, end of session and sleep ----

        private volatile bool signedOff;
        private volatile bool suspended;

        // The picture for cubes with firmware before 0.5.0, which have no
        // /api/nocarrier. They keep it until their own fall back after 3 minutes.
        internal static Canvas NoCarrierPicture(DateTime now)
        {
            var c = new Canvas();
            c.Clear(0);
            c.Text("NO CARRIER", 120, 92, Align.Center, 2, ClaudeDesigns.AmberMain, 0);
            c.Text("PC aus seit " + now.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture), 120, 136, Align.Center, 1, ClaudeDesigns.AmberDim, 0);
            return c;
        }

        // Every cube shows NO CARRIER at once: firmware 0.5.0 and newer over
        // /api/nocarrier, older cubes with picture mode get a NO CARRIER
        // picture. All cubes at the same time, never longer than timeout in
        // total. Afterwards no more pictures until Resume().
        internal void SignOff(TimeSpan timeout)
        {
            if (signedOff)
            {
                return;  // already done, for example before the program update
            }
            signedOff = true;
            WaitForFrames(TimeSpan.FromMilliseconds(Math.Min(1000, timeout.TotalMilliseconds / 2)));
            List<CubeEntry> cubes = config.Cubes.Where(c => !string.IsNullOrWhiteSpace(c.Address)).ToList();
            var tasks = new List<System.Threading.Tasks.Task>();
            DateTime now = DateTime.Now;
            foreach (CubeEntry c in cubes)
            {
                CubeState st;
                lock (gate)
                {
                    st = StateFor(c);
                }
                tasks.Add(System.Threading.Tasks.Task.Run(() => SignOffCube(c, st, timeout, now)));
            }
            try
            {
                System.Threading.Tasks.Task.WaitAll(tasks.ToArray(), timeout);
            }
            catch (AggregateException)
            {
            }
        }

        private void SignOffCube(CubeEntry c, CubeState st, TimeSpan timeout, DateTime now)
        {
            try
            {
                if (Updating(c.Address) || st.CanFrame == false)
                {
                    return;  // never during an update; without picture mode there is nothing to end
                }
                SendResult r = null;
                bool modern = st.CanNoCarrier == true || (st.Firmware != null && CubeInfo.CompareVersion(st.Firmware, 0, 5, 0) >= 0);
                if (modern || st.CanNoCarrier == null)
                {
                    r = SendNoCarrierTo(c.Address, c.ApiKey, timeout);
                }
                // Firmware before 0.5.0 answers 404: it gets a picture instead.
                if (!modern && (r == null || r.Status == 404 || st.CanNoCarrier == false))
                {
                    r = SendFrameTo(c.Address, FrameCodec.Encode(NoCarrierPicture(now), WholeScreen), c.ApiKey, "SysopTV: NO CARRIER");
                }
                lock (gate)
                {
                    st.LastSent = null;
                    st.FrameValid = false;
                    st.ShownView = -1;
                }
                if (r != null)
                {
                    Log.Write("Würfel " + c.Title + ": NO CARRIER " + (r.Ok ? "gesendet" : "nicht gesendet, " + r.Message));
                }
            }
            catch (Exception ex)
            {
                Log.Write("Würfel " + c.Title + ": NO CARRIER nicht gesendet, " + ex.GetType().Name);
            }
        }

        // Before sleep: NO CARRIER, then nothing until Resume().
        internal void Suspend(TimeSpan timeout)
        {
            suspended = true;
            if (timersRunning)
            {
                roundTimer.Change(Timeout.Infinite, Timeout.Infinite);
                frameTimer.Change(Timeout.Infinite, Timeout.Infinite);
            }
            SignOff(timeout);
            Log.Write("Ruhezustand, Würfel zeigen NO CARRIER");
        }

        // After sleep: everything as after a start, every cube gets a full picture.
        internal void Resume()
        {
            lock (gate)
            {
                foreach (CubeState st in states.Values)
                {
                    Forget(st);
                }
            }
            signedOff = false;
            suspended = false;
            Log.Write("Zurück aus dem Ruhezustand");
            if (timersRunning)
            {
                roundTimer.Change(TimeSpan.FromSeconds(5), Tick);
                frameTimer.Change(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(1));
            }
        }

        // Only for the pictures of the documentation (--docs): values as if the
        // program had run for a while, every cube reached with firmware 0.5.0.
        // Nothing is asked and nothing is sent.
        internal void ShowSamples(UsageData claudeData, DateTime okTime, Dictionary<string, ShopData> shops)
        {
            lastSent = claudeData;
            lastOk = claudeData;
            lastOkTime = okTime;
            lastQuery = okTime;
            nextRound = okTime.AddSeconds(config.Interval);
            foreach (KeyValuePair<string, ShopData> s in shops)
            {
                feeds[s.Key].Data = s.Value;
                feeds[s.Key].OkTime = okTime;
                feeds[s.Key].LastQuery = okTime;
            }
            lock (gate)
            {
                int i = 0;
                foreach (CubeEntry c in config.Cubes)
                {
                    CubeState st = StateFor(c);
                    st.Firmware = "0.5.0";
                    st.CanFrame = true;
                    st.CanNoCarrier = true;
                    st.FrameValid = true;
                    st.LastResult = new SendResult { Ok = true, Status = 200, Message = "HTTP 200" };
                    st.LastSend = okTime.AddSeconds(30 - 4 * i++);
                }
            }
            UpdateStatus();
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
                KeyValuePair<string, ShopFeed> feed = feeds.First(f => f.Value.Data != null);
                ShopData shop = feed.Value.Data;
                string label = ShopBig.Label(ShopSources.All, feed.Key);
                s.Kind = shop.Error != null ? StatusKind.Error : StatusKind.Ok;
                s.Category = shop.Error != null ? "error" : "ok";
                s.Line = label + ": heute " + ShopFormat.Orders(shop.Today.Orders) + ", " + ShopFormat.Money(shop.Today.Revenue) + " (" + time + ")";
                s.Tooltip = "SysopTV: " + label + " heute " + ShopFormat.Orders(shop.Today.Orders);
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
