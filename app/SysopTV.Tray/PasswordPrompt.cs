// SysopTV, Copyright (C) 2026 Christoph Scheel
// Licensed under the GNU General Public License v3.0, see LICENSE.
//
// Small dialog that asks for the admin password of a cube, for example
// before a firmware update when none is stored.

using System.Drawing;
using System.Windows.Forms;

namespace SysopTV.Tray
{
    internal static class PasswordPrompt
    {
        // The entered password, null after "Abbrechen".
        internal static string Ask(IWin32Window owner, string text)
        {
            using (var form = new Form
            {
                Text = "SysopTV",
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.CenterParent,
                MinimizeBox = false,
                MaximizeBox = false,
                ShowInTaskbar = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Font = SystemFonts.MessageBoxFont,
                Padding = new Padding(12)
            })
            {
                var label = new Label { Text = text, AutoSize = true, MaximumSize = new Size(340, 0), Margin = new Padding(0, 0, 0, 8) };
                var box = new TextBox { Width = 260, UseSystemPasswordChar = true };
                var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, AutoSize = true };
                var cancel = new Button { Text = "Abbrechen", DialogResult = DialogResult.Cancel, AutoSize = true };
                var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, Margin = new Padding(0, 10, 0, 0) };
                buttons.Controls.Add(cancel);
                buttons.Controls.Add(ok);
                var stack = new TableLayoutPanel { ColumnCount = 1, AutoSize = true };
                stack.Controls.Add(label);
                stack.Controls.Add(box);
                stack.Controls.Add(buttons);
                form.Controls.Add(stack);
                form.AcceptButton = ok;
                form.CancelButton = cancel;
                if (form.ShowDialog(owner) != DialogResult.OK || box.Text.Length == 0)
                {
                    return null;
                }
                return box.Text;
            }
        }
    }
}
