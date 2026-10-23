// SysopTV, Copyright (C) 2026 Christoph Scheel
// Licensed under the GNU General Public License v3.0, see LICENSE.
//
// Works in the background for all cubes:
// - every interval (at least 120 s) one query at Anthropic, no matter how many
//   cubes show Claude; cubes with "usage" get the numbers at once
// - every 5 minutes Shopify, if a cube shows the shop
// - every second the pictures for cubes with "claude" or "shop" are drawn;
//   only the changed area is sent, a full picture after a restart of the
//   cube and at least every 10 minutes

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

    internal sealed class SenderService : IDisposable
    {
        private const int WarnBelowLeft = 20;
        private static readonly TimeSpan MinQueryGap = TimeSpan.FromSeconds(AppConfig.MinInterval);
        private static readonly TimeSpan ShopInterval = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan FullFrameEvery = TimeSpan.FromMinutes(10);
        private static readonly TimeSpan InfoEvery = TimeSpan.FromSeconds(60);

        private sealed class CubeState
        {
            internal Canvas LastSent;
            internal DateTime LastFull = DateTime.MinValue;
            internal DateTime LastInfo = DateTime.MinValue;
            internal long LastUptime = -1;
            internal bool ShowUsed = true;
            internal SendResult LastResult;
        }

        private readonly ClaudeClient claude = new ClaudeClient();
        private readonly FailurePolicy policy = new FailurePolicy();
        private readonly ShopifyClient shopify = new ShopifyClient();
        private readonly Timer roundTimer;
        private readonly Timer frameTimer;
        private readonly Dictionary<string, CubeState> states = new Dictionary<string, CubeState>();
        private readonly object gate = new object();
        private AppConfig config;
        private int roundBusy, frameBusy, shopBusy;
        private DateTime lastQuery = DateTime.MinValue;
        private DateTime lastShop = DateTime.MinValue;
        private UsageData lastSent;
        private UsageData lastOk;
        private DateTime? lastOkTime;
        private ShopData shopData;
        private string lastSummary = "";
        private readonly Dictionary<string, string> lastErrors = new Dictionary<string, string>();

        internal event Action StatusChanged;

        internal SenderStatus Status { get; private set; }

        internal SenderService(AppConfig config)
        {
            this.config = config;
            Status = new SenderStatus();
            roundTimer = new Timer(_ => RunRound(false), null, Timeout.Infinite, Timeout.Infinite);
            frameTimer = new Timer(_ => RunFrames(), null, Timeout.Infinite, Timeout.Infinite);
        }

        internal void Start()
        {
            Log.Write("Start, Programm " + Program.Version + ", " + config.Cubes.Count + " Würfel, alle " + config.Interval + " Sekunden");
            foreach (CubeEntry c in config.Cubes)
            {
                Log.Write("Würfel " + c.Title + ": " + CubeFunction.Label(c.Function));
            }
            roundTimer.Change(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(config.Interval));
            frameTimer.Change(TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(1));
        }

        internal void ApplyConfig(AppConfig newConfig)
        {
            lock (gate)
            {
                config = newConfig;
                states.Clear();
            }
            Log.Write("Einstellungen gespeichert, " + newConfig.Cubes.Count + " Würfel, alle " + newConfig.Interval + " Sekunden");
            lastShop = DateTime.MinValue;
            roundTimer.Change(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(newConfig.Interval));
        }

        internal void SendNow()
        {
            ThreadPool.QueueUserWorkItem(_ => RunRound(true));
        }

        private bool AnyCube(params string[] functions)
        {
            return config.Cubes.Any(c => functions.Contains(c.Function));
        }

        // One round: Anthropic once, then usage to every "usage" cube.
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
                    UsageData data;
                    if (manual && lastSent != null && DateTime.Now - lastQuery < MinQueryGap)
                    {
                        // Protects against HTTP 429: resend what was sent last, with the current time.
                        data = lastSent;
                    }
                    else
                    {
                        UsageData raw = claude.Query();
                        if (raw.State != "error" || raw.Attempted)
                        {
                            lastQuery = DateTime.Now;
                        }
                        data = policy.Resolve(raw, DateTime.Now);
                        if (data.State == "ok" && !data.Held)
                        {
                            lastOk = data;
                            lastOkTime = DateTime.Now;
                        }
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
                        shopData.Error = d.Error;  // keep the last numbers, show the error in the footer
                        return;
                    }
                }
                shopData = d;
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
                StateFor(c).LastResult = r;
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
                if (AnyCube(CubeFunction.ShopFrame) && cfg.Shop.Configured && DateTime.Now - lastShop >= ShopInterval && shopBusy == 0)
                {
                    ThreadPool.QueueUserWorkItem(_ => { FetchShop(cfg); UpdateStatus(); });
                }
                foreach (CubeEntry c in cfg.Cubes.Where(c => c.Function != CubeFunction.Usage))
                {
                    CubeState st;
                    lock (gate)
                    {
                        st = StateFor(c);
                    }
                    Canvas canvas = Render(c, st);
                    if (canvas == null)
                    {
                        continue;
                    }
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
                            if (c.Function == CubeFunction.ClaudeFrame && info.Can("settings"))
                            {
                                try
                                {
                                    st.ShowUsed = Cube.GetSettings(c.Address, c.AdminPassword).ShowUsed;
                                }
                                catch (InvalidOperationException)
                                {
                                    // Keep the last value, default "verbraucht".
                                }
                            }
                        }
                    }
                    Rectangle area = full ? new Rectangle(0, 0, Canvas.Size, Canvas.Size)
                                          : FrameCodec.ChangedArea(st.LastSent, canvas);
                    if (area.IsEmpty)
                    {
                        continue;
                    }
                    SendResult r = Cube.SendFrame(c.Address, FrameCodec.Encode(canvas, area), c.ApiKey);
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

        private Canvas Render(CubeEntry c, CubeState st)
        {
            if (c.Function == CubeFunction.ClaudeFrame)
            {
                if (lastSent == null)
                {
                    return null;
                }
                ViewModel v = UsageView.Build(lastSent, lastOk, lastOkTime, st.ShowUsed, DateTimeOffset.Now);
                v.Address = c.Address;
                return DosRenderer.Render(v);
            }
            if (c.Function == CubeFunction.ShopFrame && shopData != null)
            {
                return ShopRenderer.Render(shopData, config.Shop.DisplayName, DateTime.Now);
            }
            return null;
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
                s.Line = "Shop: heute " + shopData.Today.Orders + " Bestellungen, " + ShopRenderer.Money(shopData.Today.Revenue) + " (" + time + ")";
                s.Tooltip = "SysopTV: Shop heute " + shopData.Today.Orders + " Bestellungen";
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
