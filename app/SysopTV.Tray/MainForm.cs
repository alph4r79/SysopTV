// SysopTV, Copyright (C) 2026 Christoph Scheel
// Licensed under the GNU General Public License v3.0, see LICENSE.
//
// The window: status line on top, the side menu on the left, the chosen page
// on the right. Every page is a Page (see below). Settings of the program are
// saved as soon as a field is left, there is no save button.
//
// A new page, for example for another service, is one class derived from
// Page plus one line in CreatePages(). If it overrides Summary, the overview
// shows its lines as well.

using System;
using System.Drawing;
using System.Windows.Forms;

namespace SysopTV.Tray
{
    // What every page can use: the working copy of the settings, the background
    // service and Save(), which writes config.json and hands the copy over.
    internal sealed class PageContext
    {
        internal AppConfig Config;
        internal SenderService Service;
        internal Action<AppConfig> Saved;
        internal Form Owner;

        internal void Save()
        {
            try
            {
                Config.Save();
            }
            catch (Exception ex)
            {
                MessageBox.Show(Owner, "Die Einstellungen ließen sich nicht speichern: " + ex.Message, "SysopTV");
                return;
            }
            Action<AppConfig> handler = Saved;
            if (handler != null)
            {
                handler(Config.Clone());
            }
        }
    }

    internal abstract class Page : Panel
    {
        protected readonly PageContext Ctx;

        protected Page(PageContext ctx)
        {
            Ctx = ctx;
            AutoScroll = true;
            Dock = DockStyle.Fill;
            Padding = new Padding(12, 8, 12, 8);
        }

        internal abstract string Title { get; }

        // Lines for the overview, null if the page has nothing to show there.
        internal virtual string[] Summary
        {
            get { return null; }
        }

        // Called when the page is shown, on status changes and once a second.
        internal virtual void UpdateView()
        {
        }

        // ---- layout helpers, the same look on every page ----

        protected static Label Heading(string text)
        {
            return new Label
            {
                Text = text,
                AutoSize = true,
                Font = new Font(SystemFonts.MessageBoxFont.FontFamily, SystemFonts.MessageBoxFont.Size + 2, FontStyle.Bold),
                Margin = new Padding(0, 4, 0, 8)
            };
        }

        protected static Label Caption(string text)
        {
            return new Label { Text = text, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 12, 0) };
        }

        protected static Label Note(string text)
        {
            return new Label { Text = text, AutoSize = true, ForeColor = SystemColors.GrayText, MaximumSize = new Size(520, 0), Margin = new Padding(0, 6, 0, 0) };
        }

        protected static Label Value()
        {
            return new Label { AutoSize = true, MaximumSize = new Size(480, 0), Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 0, 0) };
        }

        protected static TableLayoutPanel Grid()
        {
            var t = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Margin = new Padding(0, 0, 0, 8) };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            return t;
        }

        protected static void AddRow(TableLayoutPanel t, string caption, Control c)
        {
            int row = t.RowCount;
            t.RowCount = row + 1;
            t.Controls.Add(caption != null ? Caption(caption) : new Label { AutoSize = true }, 0, row);
            t.Controls.Add(c, 1, row);
        }

        protected static FlowLayoutPanel Row(params Control[] controls)
        {
            var row = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0), WrapContents = false };
            row.Controls.AddRange(controls);
            return row;
        }

        // A column of blocks from top to bottom.
        protected static TableLayoutPanel Stack(params Control[] controls)
        {
            var t = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, Dock = DockStyle.Top };
            foreach (Control c in controls)
            {
                t.Controls.Add(c);
            }
            return t;
        }

        protected static string Time(DateTime? t)
        {
            return t.HasValue ? t.Value.ToString(t.Value.Date == DateTime.Today ? "HH:mm:ss" : "dd.MM. HH:mm") : "noch nie";
        }
    }

    internal sealed class MainForm : Form
    {
        private readonly PageContext ctx;
        private readonly Label status = new Label { AutoSize = true, Font = new Font(SystemFonts.MessageBoxFont, FontStyle.Bold), Margin = new Padding(12, 8, 12, 8) };
        private readonly ListBox menu = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false, BorderStyle = BorderStyle.None };
        private readonly Panel content = new Panel { Dock = DockStyle.Fill };
        private readonly Timer ticker = new Timer { Interval = 1000 };
        private readonly Page[] pages;
        private Page current;

        internal MainForm(AppConfig config, SenderService service, Action<AppConfig> saved)
        {
            ctx = new PageContext { Config = config.Clone(), Service = service, Saved = saved, Owner = this };

            Text = "SysopTV " + Program.Version;
            Icon = TrayIcons.AppIcon();
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.Font;
            Font = SystemFonts.MessageBoxFont;
            // Fits 1366 x 768 with the taskbar: the content stays below 640 pixels.
            ClientSize = new Size(880, 600);
            MinimumSize = new Size(700, 460);

            pages = CreatePages();

            var top = new Panel { Dock = DockStyle.Top, Height = 36 };
            status.Location = new Point(12, 10);
            top.Controls.Add(status);
            var line = new Label { Dock = DockStyle.Top, Height = 1, BackColor = SystemColors.ControlDark };

            var side = new Panel { Dock = DockStyle.Left, Width = 170, Padding = new Padding(8, 8, 0, 8), BackColor = SystemColors.Window };
            menu.BackColor = SystemColors.Window;
            menu.ItemHeight = menu.Font.Height + 10;
            menu.DrawMode = DrawMode.OwnerDrawFixed;
            menu.DrawItem += DrawMenuItem;
            foreach (Page p in pages)
            {
                menu.Items.Add(p.Title);
            }
            side.Controls.Add(menu);
            var divider = new Label { Dock = DockStyle.Left, Width = 1, BackColor = SystemColors.ControlDark };

            Controls.Add(content);
            Controls.Add(divider);
            Controls.Add(side);
            Controls.Add(line);
            Controls.Add(top);

            menu.SelectedIndexChanged += (s, e) => ShowPage(menu.SelectedIndex);
            menu.SelectedIndex = 0;

            ShowStatus();
            service.StatusChanged += OnStatusChanged;
            ticker.Tick += (s, e) => UpdateCurrent();
            ticker.Start();
            FormClosed += (s, e) =>
            {
                ticker.Stop();
                ticker.Dispose();
                service.StatusChanged -= OnStatusChanged;
            };
        }

        // All pages in menu order. A new page only needs a line here.
        private Page[] CreatePages()
        {
            return new Page[]
            {
                new OverviewPage(ctx, () => pages),
                new CubesPage(ctx),
                new ClaudePage(ctx),
                new ShopifyPage(ctx),
                new ProgramPage(ctx)
            };
        }

        private void DrawMenuItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0)
            {
                return;
            }
            bool sel = (e.State & DrawItemState.Selected) != 0;
            using (var back = new SolidBrush(sel ? SystemColors.Highlight : SystemColors.Window))
            {
                e.Graphics.FillRectangle(back, e.Bounds);
            }
            TextRenderer.DrawText(e.Graphics, (string)menu.Items[e.Index], menu.Font,
                new Rectangle(e.Bounds.X + 8, e.Bounds.Y, e.Bounds.Width - 8, e.Bounds.Height),
                sel ? SystemColors.HighlightText : SystemColors.WindowText, TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
        }

        private void ShowPage(int index)
        {
            if (index < 0 || index >= pages.Length || pages[index] == current)
            {
                return;
            }
            // Leaving a page saves what is in its fields, like leaving a field.
            Validate();
            content.Controls.Clear();
            current = pages[index];
            content.Controls.Add(current);
            current.UpdateView();
        }

        private void UpdateCurrent()
        {
            if (current != null)
            {
                current.UpdateView();
            }
        }

        private void OnStatusChanged()
        {
            if (IsHandleCreated && !IsDisposed)
            {
                BeginInvoke((Action)(() =>
                {
                    ShowStatus();
                    UpdateCurrent();
                }));
            }
        }

        private void ShowStatus()
        {
            SenderStatus st = ctx.Service.Status;
            status.Text = st.Line;
            status.ForeColor = Colour(st.Kind);
        }

        internal static Color Colour(StatusKind kind)
        {
            switch (kind)
            {
                case StatusKind.Error: return Color.Firebrick;
                case StatusKind.Warn: return Color.DarkOrange;
                case StatusKind.Ok: return Color.ForestGreen;
                default: return SystemColors.GrayText;
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            Validate();
            base.OnFormClosing(e);
        }
    }
}
