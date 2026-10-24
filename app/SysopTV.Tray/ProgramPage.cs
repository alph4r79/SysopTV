// SysopTV, Copyright (C) 2026 Christoph Scheel
// Licensed under the GNU General Public License v3.0, see LICENSE.
//
// Page "Programm": start with Windows, the update check (daily, off by
// default, or once with "Jetzt nach Updates suchen"), the program update for
// the installed program, log folder, version and the link to the repository.

using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SysopTV.Tray
{
    internal sealed class ProgramPage : Page
    {
        private const string RepoUrl = "https://github.com/alph4r79/SysopTV";
        private readonly CheckBox autostart = new CheckBox { Text = "Mit Windows starten", AutoSize = true };
        private readonly CheckBox updateCheck = new CheckBox { Text = "Einmal täglich nach neuen Versionen suchen", AutoSize = true };
        private readonly Button checkNow = new Button { Text = "Jetzt nach Updates suchen", AutoSize = true };
        private readonly Button install = new Button { Text = "Jetzt aktualisieren", AutoSize = true, Visible = false, Margin = new Padding(8, 3, 0, 0) };
        private readonly Label updateState = Note("");
        private readonly LinkLabel updateLink = new LinkLabel { AutoSize = true, Visible = false, Margin = new Padding(0, 6, 0, 0) };
        private string updateUrl;
        private string checkText;   // result of the last click on "Jetzt nach Updates suchen"
        private bool busy;
        private bool filling;

        // Installed by the setup or portable; the offline tests put their answer here.
        internal static Func<bool> Installed = () => AppUpdate.Installed;

        internal ProgramPage(PageContext ctx) : base(ctx)
        {
            filling = true;
            try
            {
                autostart.Checked = Autostart.IsEnabled();
            }
            catch (Exception)
            {
                autostart.Checked = false;
            }
            updateCheck.Checked = ctx.Config.UpdateCheck;
            filling = false;

            var logButton = new Button { Text = "Log-Ordner öffnen", AutoSize = true };
            var link = new LinkLabel { Text = "github.com/alph4r79/SysopTV", AutoSize = true, Margin = new Padding(0, 6, 0, 0) };

            TableLayoutPanel g = Grid();
            AddRow(g, null, autostart);
            AddRow(g, null, updateCheck);
            AddRow(g, null, checkNow);
            AddRow(g, null, Row(updateLink, install));
            AddRow(g, null, updateState);
            AddRow(g, "Log", Row(logButton, Note(AppPaths.LogDir)));
            AddRow(g, "Version", new Label { Text = Program.Version, AutoSize = true, Margin = new Padding(0, 6, 0, 0) });
            AddRow(g, "Quelltext", link);
            AddRow(g, "Lizenz", new Label { Text = "GPL-3.0", AutoSize = true, Margin = new Padding(0, 6, 0, 0) });
            Controls.Add(Stack(Heading("Programm"), g,
                Note("Die Suche fragt bei GitHub nach dem neuesten Release, nur bei eingeschalteter Suche oder nach einem Klick auf „Jetzt nach Updates suchen“. "
                     + "Installiert wird nur nach einem Klick auf „Jetzt aktualisieren“ und einer Rückfrage, jede Datei wird vorher mit SHA256SUMS.txt geprüft.")));

            autostart.CheckedChanged += (s, e) => SetAutostart();
            updateCheck.CheckedChanged += (s, e) =>
            {
                if (filling)
                {
                    return;
                }
                Ctx.Config.UpdateCheck = updateCheck.Checked;
                Ctx.Save();
                UpdateView();
            };
            updateLink.LinkClicked += (s, e) => Open(updateUrl);
            checkNow.Click += async (s, e) => await CheckNow();
            install.Click += async (s, e) => await InstallUpdate();
            logButton.Click += (s, e) =>
            {
                try
                {
                    Directory.CreateDirectory(AppPaths.LogDir);
                    Process.Start(AppPaths.LogDir);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(FindForm(), "Der Ordner ließ sich nicht öffnen: " + ex.Message, "SysopTV");
                }
            };
            link.LinkClicked += (s, e) => Open(RepoUrl);
        }

        internal override string Title
        {
            get { return "Programm"; }
        }

        private void Open(string url)
        {
            if (string.IsNullOrEmpty(url))
            {
                return;
            }
            try
            {
                Process.Start(url);
            }
            catch (Exception ex)
            {
                MessageBox.Show(FindForm(), "Der Browser ließ sich nicht öffnen: " + ex.Message, "SysopTV");
            }
        }

        // Asks GitHub once, also with the daily check switched off.
        private async Task CheckNow()
        {
            checkNow.Enabled = false;
            checkText = "Suche ...";
            UpdateView();
            UpdateCheckResult r = await Task.Run(() => Ctx.Service.CheckForUpdate(true));
            if (IsDisposed)
            {
                return;
            }
            checkNow.Enabled = true;
            checkText = ResultText(r);
            UpdateView();
        }

        internal static string ResultText(UpdateCheckResult r)
        {
            if (r == null || !r.Reached)
            {
                return "GitHub ist nicht erreichbar, bitte später noch einmal versuchen.";
            }
            if (!r.AppNewer && !r.FirmwareNewer)
            {
                return "Keine neuere Version, alles aktuell.";
            }
            string text = r.AppNewer ? "Programm " + r.Release.AppVersion + " verfügbar." : "";
            if (r.FirmwareNewer)
            {
                text += (text.Length > 0 ? " " : "") + "Firmware " + r.Release.FirmwareVersion + " verfügbar, siehe Seite Würfel.";
            }
            return text;
        }

        // A newer program as a link to its release, the button only for the installed program.
        internal override void UpdateView()
        {
            ReleaseInfo r = Ctx.Service.NewerRelease;
            DateTime? checkedAt = Ctx.Service.UpdateCheckedAt;
            bool installed = Installed();
            if (r != null)
            {
                updateUrl = r.Url;
                updateLink.Text = "Version " + r.AppVersion + " verfügbar";
                updateLink.Visible = true;
                install.Visible = installed && r.SetupUrl != null;
                install.Enabled = !busy;
            }
            else
            {
                updateLink.Visible = false;
                install.Visible = false;
            }
            string text = checkText;
            if (text == null)
            {
                text = !Ctx.Config.UpdateCheck ? "" :
                    r != null ? "" :
                    checkedAt.HasValue ? "Zuletzt gesucht " + Time(checkedAt) + ", keine neuere Version." : "Noch nicht gesucht.";
            }
            if (r != null && !installed)
            {
                text = (text.Length > 0 ? text + " " : "") + "Portable Version: das neue ZIP von der Seite des Releases laden und entpacken.";
            }
            updateState.Text = text;
        }

        // Downloads and checks the setup, asks, gives the cubes NO CARRIER and
        // hands over to the silent setup, which starts the program again.
        private async Task InstallUpdate()
        {
            ReleaseInfo r = Ctx.Service.NewerRelease;
            if (r == null || busy || !Installed())
            {
                return;
            }
            busy = true;
            install.Enabled = false;
            checkText = "Lade " + r.SetupName + " ...";
            UpdateView();
            string sha = null, error = null;
            string setup = await Task.Run(() => AppUpdate.DownloadSetup(r, out sha, out error));
            if (IsDisposed)
            {
                AppUpdate.Discard(setup);
                return;
            }
            busy = false;
            if (setup == null)
            {
                checkText = error;
                UpdateView();
                Log.Write("Programm-Update nicht verwendet: " + error);
                MessageBox.Show(FindForm(), error + " Es wurde nichts installiert.", "SysopTV", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            string question = "SysopTV " + r.AppVersion + " installieren?\n\nDas Programm beendet sich dafür, die Würfel zeigen kurz NO CARRIER. "
                              + "Danach startet SysopTV von selbst wieder, deine Einstellungen bleiben erhalten.";
            if (MessageBox.Show(FindForm(), question, "SysopTV", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            {
                AppUpdate.Discard(setup);
                checkText = null;
                UpdateView();
                return;
            }
            Log.Write("Programm-Update auf " + r.AppVersion + ": Setup geprüft, starte es still");
            Ctx.Service.SignOff(TrayApp.SignOffTimeout);
            bool started;
            try
            {
                started = AppUpdate.StartProcess(AppUpdate.HelperStart(Application.ExecutablePath, setup, sha, Process.GetCurrentProcess().Id));
            }
            catch (Exception ex)
            {
                Log.Write("Programm-Update: Start fehlgeschlagen, " + ex.GetType().Name);
                started = false;
            }
            if (!started)
            {
                AppUpdate.Discard(setup);
                Ctx.Service.Resume();
                checkText = "Das Setup ließ sich nicht starten.";
                UpdateView();
                return;
            }
            Action exit = Program.RequestExit;
            if (exit != null)
            {
                exit();
            }
        }

        private void SetAutostart()
        {
            if (filling)
            {
                return;
            }
            try
            {
                Autostart.Set(autostart.Checked);
            }
            catch (Exception ex)
            {
                MessageBox.Show(FindForm(), "Der Autostart ließ sich nicht ändern: " + ex.Message, "SysopTV");
            }
        }
    }
}
