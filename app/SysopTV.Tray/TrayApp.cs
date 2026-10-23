// SysopTV, Copyright (C) 2026 Christoph Scheel
// Licensed under the GNU General Public License v3.0, see LICENSE.
//
// The program in the tray: icon, menu, balloon tips on state changes.

using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace SysopTV.Tray
{
    internal sealed class TrayApp : ApplicationContext
    {
        private readonly NotifyIcon notify;
        private readonly ToolStripMenuItem statusItem;
        private readonly Control invoker;
        private readonly SenderService service;
        private readonly RegisteredWaitHandle showWait;
        private AppConfig config;
        private SettingsForm settingsForm;
        private Icon currentIcon;
        private string lastCategory = "none";

        internal TrayApp(EventWaitHandle showEvent)
        {
            invoker = new Control();
            invoker.CreateControl();

            config = AppConfig.Load();
            service = new SenderService(config);
            service.StatusChanged += () => invoker.BeginInvoke((Action)UpdateUi);

            statusItem = new ToolStripMenuItem("Noch keine Daten") { Enabled = false };
            var menu = new ContextMenuStrip();
            menu.Items.Add(statusItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Jetzt senden", null, (s, e) => service.SendNow());
            menu.Items.Add("Würfel im Browser öffnen", null, (s, e) => OpenCube(""));
            menu.Items.Add("Einstellungen", null, (s, e) => ShowSettings());
            menu.Items.Add("Log öffnen", null, (s, e) => OpenLog());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Beenden", null, (s, e) => ExitThread());

            currentIcon = TrayIcons.Create(StatusKind.None);
            notify = new NotifyIcon
            {
                Icon = currentIcon,
                Text = "SysopTV: noch keine Daten",
                ContextMenuStrip = menu,
                Visible = true
            };
            notify.DoubleClick += (s, e) => ShowSettings();

            // A second start of the program sets this event.
            showWait = ThreadPool.RegisterWaitForSingleObject(showEvent,
                (state, timedOut) => invoker.BeginInvoke((Action)ShowSettings), null, Timeout.Infinite, false);

            service.Start();

            var legacyTimer = new System.Windows.Forms.Timer { Interval = 1500 };
            legacyTimer.Tick += (s, e) =>
            {
                legacyTimer.Stop();
                legacyTimer.Dispose();
                CheckLegacyScript();
            };
            legacyTimer.Start();
        }

        private void UpdateUi()
        {
            SenderStatus st = service.Status;
            statusItem.Text = st.Line;
            string tip = st.Tooltip;
            notify.Text = tip.Length > 63 ? tip.Substring(0, 63) : tip;

            Icon old = currentIcon;
            currentIcon = TrayIcons.Create(st.Kind);
            notify.Icon = currentIcon;
            if (old != null)
            {
                old.Dispose();
            }

            if (st.Category != lastCategory)
            {
                string text = null;
                ToolTipIcon icon = ToolTipIcon.Info;
                switch (st.Category)
                {
                    case "login":
                        text = "Die Anmeldung von Claude Code ist abgelaufen. Claude Code kurz benutzen, dann geht es weiter.";
                        icon = ToolTipIcon.Warning;
                        break;
                    case "unreachable":
                        text = "Nicht erreichbar: " + st.Detail;
                        icon = ToolTipIcon.Error;
                        break;
                    case "error":
                        text = "Keine Daten: " + st.Detail;
                        icon = ToolTipIcon.Error;
                        break;
                    case "ok":
                        if (lastCategory != "none")
                        {
                            text = "Läuft wieder, die Daten kommen an.";
                        }
                        break;
                }
                if (text != null)
                {
                    notify.ShowBalloonTip(5000, "SysopTV", text, icon);
                }
                lastCategory = st.Category;
            }
        }

        internal void ShowSettings()
        {
            if (settingsForm != null && !settingsForm.IsDisposed)
            {
                settingsForm.WindowState = FormWindowState.Normal;
                settingsForm.Activate();
                return;
            }
            settingsForm = new SettingsForm(config, service);
            settingsForm.Saved += cfg =>
            {
                config = cfg;
                service.ApplyConfig(cfg);
            };
            settingsForm.Show();
            settingsForm.Activate();
        }

        private void OpenCube(string path)
        {
            try
            {
                Process.Start(Net.BaseUrl(config.Cubes.Count > 0 ? config.Cubes[0].Address : "sysoptv.local") + path);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Der Browser ließ sich nicht öffnen: " + ex.Message, "SysopTV",
                                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private static void OpenLog()
        {
            try
            {
                if (File.Exists(AppPaths.LogFile))
                {
                    Process.Start(AppPaths.LogFile);
                }
                else
                {
                    Directory.CreateDirectory(AppPaths.LogDir);
                    Process.Start(AppPaths.LogDir);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Das Log ließ sich nicht öffnen: " + ex.Message, "SysopTV",
                                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        // Once per program start: warn about the old PowerShell sender.
        private void CheckLegacyScript()
        {
            var shortcuts = LegacyScript.FindShortcuts();
            int running = LegacyScript.CountRunning();
            if (shortcuts.Count == 0 && running == 0)
            {
                return;
            }
            Log.Write("Altes Skript gefunden: " + shortcuts.Count + " Verknüpfung(en) im Autostart, " + running + " laufend");
            using (var dlg = new LegacyDialog(shortcuts.Count > 0, running > 0))
            {
                if (dlg.ShowDialog() == DialogResult.OK && shortcuts.Count > 0)
                {
                    try
                    {
                        LegacyScript.MoveShortcuts(shortcuts);
                        string done = "Die Verknüpfung liegt jetzt in " + AppPaths.LegacyDir + ".";
                        if (running > 0)
                        {
                            done += "\n\nDas alte Skript läuft noch. Beende es im Task-Manager (powershell.exe) oder starte den PC neu.";
                        }
                        MessageBox.Show(done, "SysopTV", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("Die Verknüpfung ließ sich nicht verschieben: " + ex.Message, "SysopTV",
                                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                }
            }
        }

        protected override void ExitThreadCore()
        {
            showWait.Unregister(null);
            service.Dispose();
            notify.Visible = false;
            notify.Dispose();
            if (currentIcon != null)
            {
                currentIcon.Dispose();
            }
            if (settingsForm != null && !settingsForm.IsDisposed)
            {
                settingsForm.Close();
            }
            invoker.Dispose();
            Log.Write("Beendet");
            base.ExitThreadCore();
        }
    }
}
