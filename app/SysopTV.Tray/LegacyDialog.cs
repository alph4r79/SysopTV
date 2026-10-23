// SysopTV, Copyright (C) 2026 Christoph Scheel
// Licensed under the GNU General Public License v3.0, see LICENSE.
//
// Hint about the old PowerShell sender. "Verknuepfung entfernen" returns OK.

using System.Drawing;
using System.Windows.Forms;

namespace SysopTV.Tray
{
    internal sealed class LegacyDialog : Form
    {
        internal LegacyDialog(bool hasShortcut, bool isRunning)
        {
            Text = "SysopTV: altes Skript gefunden";
            Icon = TrayIcons.AppIcon();
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.Font;
            Font = SystemFonts.MessageBoxFont;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Padding = new Padding(12);

            string text = "Das alte PowerShell-Skript sysoptv-sender.ps1 ";
            if (hasShortcut && isRunning)
            {
                text += "steht im Autostart und läuft gerade.";
            }
            else if (hasShortcut)
            {
                text += "steht im Autostart.";
            }
            else
            {
                text += "läuft gerade.";
            }
            text += " Es sendet parallel zu diesem Programm und fragt die Limits doppelt ab. Das kann zu Sperren (Fehler 429) führen.";
            if (isRunning)
            {
                text += "\n\nLaufende Skripte beendet dieses Programm nicht. Beende sie im Task-Manager (powershell.exe) oder starte den PC neu.";
            }

            var label = new Label { Text = text, AutoSize = true, MaximumSize = new Size(440, 0), Margin = new Padding(0, 0, 0, 12) };
            var buttons = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.RightToLeft,
                AutoSize = true,
                Dock = DockStyle.Fill,
                Margin = new Padding(0)
            };
            var later = new Button { Text = hasShortcut ? "Später" : "OK", DialogResult = DialogResult.Cancel, AutoSize = true };
            buttons.Controls.Add(later);
            if (hasShortcut)
            {
                var remove = new Button { Text = "Verknüpfung entfernen", DialogResult = DialogResult.OK, AutoSize = true };
                buttons.Controls.Add(remove);
                AcceptButton = remove;
            }
            CancelButton = later;

            var layout = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Dock = DockStyle.Fill };
            layout.Controls.Add(label);
            layout.Controls.Add(buttons);
            Controls.Add(layout);
        }
    }
}
