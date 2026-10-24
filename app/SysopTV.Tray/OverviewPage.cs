// SysopTV, Copyright (C) 2026 Christoph Scheel
// Licensed under the GNU General Public License v3.0, see LICENSE.
//
// Page "Übersicht": one line per cube with name, function, design, last
// sending and state in colour, below the summaries of the other pages.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace SysopTV.Tray
{
    internal sealed class OverviewPage : Page
    {
        private readonly Func<Page[]> allPages;
        private readonly ListView cubes = new ListView
        {
            View = View.Details,
            FullRowSelect = true,
            HeaderStyle = ColumnHeaderStyle.Nonclickable,
            Width = 640,
            Height = 130,
            MultiSelect = false
        };
        private readonly TableLayoutPanel summaries = new TableLayoutPanel { ColumnCount = 1, AutoSize = true };
        private readonly Dictionary<string, Label> summaryLabels = new Dictionary<string, Label>();

        internal OverviewPage(PageContext ctx, Func<Page[]> allPages) : base(ctx)
        {
            this.allPages = allPages;
            cubes.Columns.Add("Würfel", 150);
            cubes.Columns.Add("Funktion", 165);
            cubes.Columns.Add("Design", 160);
            cubes.Columns.Add("Gesendet", 62);
            cubes.Columns.Add("Zustand", 97);
            Controls.Add(Stack(Heading("Übersicht"), cubes, summaries));
        }

        internal override string Title
        {
            get { return "Übersicht"; }
        }

        internal override void UpdateView()
        {
            List<CubeRow> rows = Ctx.Service.Rows();
            cubes.BeginUpdate();
            while (cubes.Items.Count > rows.Count)
            {
                cubes.Items.RemoveAt(cubes.Items.Count - 1);
            }
            for (int i = 0; i < rows.Count; i++)
            {
                CubeRow r = rows[i];
                string[] cells = { r.Title, r.Function, r.Design, r.LastSend.HasValue ? r.LastSend.Value.ToString("HH:mm:ss") : "--", r.State };
                ListViewItem item;
                if (i < cubes.Items.Count)
                {
                    item = cubes.Items[i];
                }
                else
                {
                    item = cubes.Items.Add(new ListViewItem(new string[cells.Length]) { UseItemStyleForSubItems = false });
                }
                for (int c = 0; c < cells.Length; c++)
                {
                    if (item.SubItems[c].Text != cells[c])
                    {
                        item.SubItems[c].Text = cells[c];
                    }
                }
                item.SubItems[4].ForeColor = MainForm.Colour(r.Kind);
                item.ToolTipText = r.State;
            }
            cubes.EndUpdate();

            foreach (Page p in allPages())
            {
                string[] lines = p.Summary;
                if (lines == null)
                {
                    continue;
                }
                Label label;
                if (!summaryLabels.TryGetValue(p.Title, out label))
                {
                    summaries.Controls.Add(new Label
                    {
                        Text = p.Title,
                        AutoSize = true,
                        Font = new Font(SystemFonts.MessageBoxFont, FontStyle.Bold),
                        Margin = new Padding(0, 12, 0, 2)
                    });
                    label = new Label { AutoSize = true, MaximumSize = new Size(640, 0) };
                    summaries.Controls.Add(label);
                    summaryLabels[p.Title] = label;
                }
                string text = string.Join(Environment.NewLine, lines);
                if (label.Text != text)
                {
                    label.Text = text;
                }
            }
        }
    }
}
