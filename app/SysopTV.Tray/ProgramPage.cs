// SysopTV, Copyright (C) 2026 Christoph Scheel
// Licensed under the GNU General Public License v3.0, see LICENSE.
//
// Page "Programm": start with Windows, log folder, version and the link to
// the repository.

using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;

namespace SysopTV.Tray
{
    internal sealed class ProgramPage : Page
    {
        private const string RepoUrl = "https://github.com/alph4r79/SysopTV";
        private readonly CheckBox autostart = new CheckBox { Text = "Mit Windows starten", AutoSize = true };
        private bool filling;

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
            filling = false;

            var logButton = new Button { Text = "Log-Ordner öffnen", AutoSize = true };
            var link = new LinkLabel { Text = "github.com/alph4r79/SysopTV", AutoSize = true, Margin = new Padding(0, 6, 0, 0) };

            TableLayoutPanel g = Grid();
            AddRow(g, null, autostart);
            AddRow(g, "Log", Row(logButton, Note(AppPaths.LogDir)));
            AddRow(g, "Version", new Label { Text = Program.Version, AutoSize = true, Margin = new Padding(0, 6, 0, 0) });
            AddRow(g, "Quelltext", link);
            AddRow(g, "Lizenz", new Label { Text = "GPL-3.0", AutoSize = true, Margin = new Padding(0, 6, 0, 0) });
            Controls.Add(Stack(Heading("Programm"), g));

            autostart.CheckedChanged += (s, e) => SetAutostart();
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
            link.LinkClicked += (s, e) =>
            {
                try
                {
                    Process.Start(RepoUrl);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(FindForm(), "Der Browser ließ sich nicht öffnen: " + ex.Message, "SysopTV");
                }
            };
        }

        internal override string Title
        {
            get { return "Programm"; }
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
