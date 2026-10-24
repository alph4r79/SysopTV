// SysopTV, Copyright (C) 2026 Christoph Scheel
// Licensed under the GNU General Public License v3.0, see LICENSE.
//
// Page "Würfel": the list of cubes on the left with the firmware version and
// "Firmware aktualisieren", the selected cube on the right in two parts.
// "Verbindung und Funktion" is stored in the program and saved as soon as a
// field is left. The function is Claude Limits, Shopify or WooCommerce, with
// "Im Wechsel zeigen" a second one in turn; the view fields below edit the
// function chosen under "Bearbeiten". The program draws every picture itself,
// cubes without picture mode (firmware before 0.4.0) need a firmware update.
// "Einstellungen im Würfel" is stored in the cube: loaded on its own when an
// admin password is set, and written only with "Auf dem Würfel speichern",
// which stays locked until a load worked.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SysopTV.Tray
{
    internal sealed class CubesPage : Page
    {
        private static readonly string[] CycleLabels = { "aus", "alle 30 Sekunden", "alle 60 Sekunden" };

        internal const string OldFirmwareText = "Firmware zu alt für Bilder. Bitte links „Firmware aktualisieren“ drücken.";

        // List
        private readonly ListBox list = new ListBox { Width = 170, Height = 150, IntegralHeight = false };
        private readonly Button searchButton = new Button { Text = "Suchen" };
        private readonly Button addButton = new Button { Text = "Von Hand hinzufügen" };
        private readonly Button removeButton = new Button { Text = "Entfernen" };
        private readonly ListBox found = new ListBox { Width = 170, Height = 80, IntegralHeight = false, Visible = false };
        private readonly Label listStatus = new Label { AutoSize = true, MaximumSize = new Size(170, 0), ForeColor = SystemColors.GrayText };
        private readonly Label firmwareLabel = new Label { AutoSize = true, MaximumSize = new Size(170, 0), Margin = new Padding(0, 10, 0, 0) };
        private readonly Button updateButton = new Button { Text = "Firmware aktualisieren", Width = 170, Height = 28, Margin = new Padding(0, 4, 0, 0), Visible = false };

        // Verbindung und Funktion (stored in the program)
        private readonly TableLayoutPanel connGrid = Grid();
        private readonly TextBox address = new TextBox { Width = 190 };
        private readonly ComboBox function = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 140 };
        private readonly LinkLabel functionNote = new LinkLabel { AutoSize = true, MaximumSize = new Size(310, 0), Margin = new Padding(0, 0, 0, 4) };
        private readonly ComboBox design = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
        private readonly RadioButton showUsed = new RadioButton { Text = "verbraucht", AutoSize = true };
        private readonly RadioButton showLeft = new RadioButton { Text = "übrig", AutoSize = true };
        private readonly ComboBox cycle = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
        private readonly TextBox apiKey = new TextBox { Width = 240, UseSystemPasswordChar = true };
        private readonly TextBox adminPassword = new TextBox { Width = 240, UseSystemPasswordChar = true };
        private readonly Button testButton = new Button { Text = "Verbindung testen", AutoSize = true, Margin = new Padding(6, 0, 0, 0) };
        private readonly Label cubeStatus = new Label { AutoSize = true, MaximumSize = new Size(170, 0), ForeColor = SystemColors.GrayText, Margin = new Padding(0, 6, 0, 0) };
        private readonly ComboBox shopView = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110 };
        private readonly ComboBox period = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120 };
        private readonly ComboBox periodCycle = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 130 };
        private readonly ComboBox content = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };
        private readonly ComboBox bigStyle = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120 };
        private readonly CheckBox claudeFlip = new CheckBox { Text = "Klapp-Animation", AutoSize = true, Margin = new Padding(8, 5, 0, 0) };
        private readonly CheckBox bigFlip = new CheckBox { Text = "Klapp-Animation", AutoSize = true, Margin = new Padding(8, 5, 0, 0) };
        private readonly Label styleNote = new Label { AutoSize = true, MaximumSize = new Size(200, 0), ForeColor = SystemColors.GrayText, Margin = new Padding(8, 0, 0, 0), Visible = false };
        private readonly Label viewCaption = Caption("Ansicht");
        private readonly CheckBox alternate = new CheckBox { Text = "Im Wechsel zeigen, alle", AutoSize = true, Margin = new Padding(0, 5, 4, 0) };
        private readonly NumericUpDown alternateSeconds = new NumericUpDown
        {
            Minimum = CubeEntry.MinAlternateSeconds, Maximum = CubeEntry.MaxAlternateSeconds, Value = CubeEntry.DefaultAlternateSeconds, Width = 55
        };
        // Which function the view fields below edit. A list, not two radio
        // buttons: those check themselves when they get the focus.
        private readonly ComboBox editWhich = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };
        private Control designRow, modeRow, cycleRow, periodRow, contentRow, styleRow, editRow;

        // Einstellungen im Würfel (stored in the cube)
        private readonly TableLayoutPanel cubeGrid = Grid();
        private readonly Panel browserPanel = new Panel { AutoSize = true, Visible = false };
        private readonly NumericUpDown brightness = new NumericUpDown { Minimum = 10, Maximum = 100, Increment = 5, Width = 60 };
        private readonly CheckBox night = new CheckBox { Text = "an", AutoSize = true, Margin = new Padding(0, 5, 8, 0) };
        private readonly TextBox nightFrom = new TextBox { Width = 50 };
        private readonly TextBox nightTo = new TextBox { Width = 50 };
        private readonly NumericUpDown nightBrightness = new NumericUpDown { Minimum = 0, Maximum = 100, Increment = 5, Width = 60 };
        private readonly TextBox deviceName = new TextBox { Width = 180, MaxLength = 24 };
        private readonly Button reloadButton = new Button { Text = "Neu laden", AutoSize = true };
        private readonly Button saveButton = new Button { Text = "Auf dem Würfel speichern", AutoSize = true };
        private readonly Label settingsStatus = new Label { AutoSize = true, MaximumSize = new Size(400, 0), ForeColor = SystemColors.GrayText, Margin = new Padding(0, 2, 0, 4) };

        // Reading the cube; the pictures of the documentation put samples here, so they need no network.
        internal static Func<string, TimeSpan, CubeInfo> ReadInfo = Cube.GetInfo;
        internal static Func<string, string, CubeSettings> ReadSettings = Cube.GetSettings;
        // The firmware that comes with the program; the offline tests put their own here.
        internal static Func<BundledFirmware> FindFirmware = BundledFirmware.FindLocal;

        private CubeEntry selected;
        private bool editingSecond;       // the view fields show the second function
        private CubeSettings loaded;      // values of the selected cube, null until a load worked
        private int loadToken;            // a newer load makes older answers worthless
        private bool busy;
        private bool filling;
        private readonly Dictionary<string, CubeInfo> infos = new Dictionary<string, CubeInfo>();
        private CancellationTokenSource searchCancel;

        internal CubesPage(PageContext ctx) : base(ctx)
        {
            cycle.Items.AddRange(CycleLabels);

            var left = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, Margin = new Padding(0, 0, 16, 0) };
            left.Controls.Add(list);
            foreach (Button b in new[] { searchButton, addButton, removeButton })
            {
                b.Width = 170;
                b.Height = 28;
                b.Margin = new Padding(0, 4, 0, 0);
                left.Controls.Add(b);
            }
            // Firmware of the selected cube and the update, then the result of
            // "Verbindung testen" or of the update.
            left.Controls.Add(firmwareLabel);
            left.Controls.Add(updateButton);
            left.Controls.Add(cubeStatus);
            left.Controls.Add(found);
            left.Controls.Add(listStatus);

            function.Items.AddRange(CubeFunction.All);
            shopView.Items.AddRange(ShopBig.Views);
            period.Items.AddRange(ShopBig.Periods);
            periodCycle.Items.AddRange(new object[] { "nicht wechseln", "alle 30 Sekunden", "alle 60 Sekunden" });
            content.Items.AddRange(ShopBig.Contents);
            bigStyle.Items.AddRange(ShopBig.Styles);

            AddRow(connGrid, "Adresse (IP oder Name)", Row(address, testButton));
            AddRow(connGrid, "Wechsel", Row(alternate, alternateSeconds, Caption("Sekunden")));
            editRow = editWhich;
            AddRow(connGrid, "Bearbeiten", editRow);
            // Function and view share one row, so the page fits without scrolling.
            AddRow(connGrid, "Funktion", Row(function, viewCaption, shopView));
            AddRow(connGrid, null, functionNote);
            designRow = Row(design, claudeFlip);
            AddRow(connGrid, "Design", designRow);
            modeRow = Row(showUsed, showLeft);
            AddRow(connGrid, "Prozent zeigen", modeRow);
            cycleRow = cycle;
            AddRow(connGrid, "Automatisch wechseln", cycleRow);
            periodRow = Row(period, periodCycle);
            AddRow(connGrid, "Zeitraum", periodRow);
            contentRow = content;
            AddRow(connGrid, "Inhalt", contentRow);
            styleRow = Row(bigStyle, bigFlip, styleNote);
            AddRow(connGrid, "Stil", styleRow);
            AddRow(connGrid, "API-Schlüssel (optional)", apiKey);
            AddRow(connGrid, "Admin-Passwort", adminPassword);

            AddRow(cubeGrid, "Helligkeit", Row(brightness, Caption("%")));
            AddRow(cubeGrid, "Nachtmodus", Row(night, Caption("von"), nightFrom, Caption("bis"), nightTo));
            AddRow(cubeGrid, "Helligkeit nachts", Row(nightBrightness, Caption("%")));
            AddRow(cubeGrid, "Gerätename", deviceName);

            var info = Note("Dieser Würfel hat noch keine Schnittstelle für Einstellungen (ab Firmware 0.4.0). Die Seite im Browser kann alles einstellen.");
            var open = new Button { Text = "Einstellungen am Würfel öffnen", AutoSize = true, Top = 40 };
            info.MaximumSize = new Size(440, 0);
            open.Click += (s, e) => OpenBrowser("settings");
            browserPanel.Controls.Add(info);
            browserPanel.Controls.Add(open);

            // The buttons sit in the header of their part, so they never slide out of view.
            var cubeHeader = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Margin = new Padding(0, 4, 0, 0) };
            cubeHeader.Controls.Add(SectionTitle("Einstellungen im Würfel"), 0, 0);
            cubeHeader.Controls.Add(Row(reloadButton, saveButton), 1, 0);

            var right = Stack(SectionTitle("Verbindung und Funktion"), connGrid, Line(), cubeHeader, settingsStatus, cubeGrid, browserPanel);
            right.Dock = DockStyle.None;

            var columns = new TableLayoutPanel { ColumnCount = 2, AutoSize = true };
            columns.Controls.Add(left, 0, 0);
            columns.Controls.Add(right, 1, 0);
            Controls.Add(Stack(Heading("Würfel"), columns));

            FillList();
            list.SelectedIndexChanged += (s, e) => SelectCube();
            searchButton.Click += async (s, e) => await Search();
            addButton.Click += (s, e) => AddCube(new CubeEntry { Address = "" }, true);
            removeButton.Click += (s, e) => RemoveCube();
            testButton.Click += async (s, e) => await TestCube();
            found.SelectedIndexChanged += (s, e) =>
            {
                var ci = found.SelectedItem as CubeInfo;
                if (ci != null)
                {
                    AddFound(ci);
                }
            };
            functionNote.LinkClicked += (s, e) =>
            {
                Action<string> openPage = Ctx.OpenPage;
                if (openPage != null && selected != null)
                {
                    openPage(CubeFunction.Label(Editing.Function));
                }
            };
            updateButton.Click += async (s, e) => await UpdateFirmware();
            alternate.CheckedChanged += (s, e) =>
            {
                if (filling)
                {
                    return;
                }
                Commit();
                if (!alternate.Checked)
                {
                    editingSecond = false;
                }
                FillFields();
            };
            alternateSeconds.ValueChanged += (s, e) => Commit();
            editWhich.SelectedIndexChanged += (s, e) =>
            {
                if (filling)
                {
                    return;
                }
                // Keep what the fields show for the function edited so far, then switch.
                Commit();
                editingSecond = editWhich.SelectedIndex == 1;
                FillFields();
            };

            // Program settings: saved when a field is left or a choice changes.
            address.Leave += (s, e) => Commit();
            apiKey.Leave += (s, e) => Commit();
            adminPassword.Leave += (s, e) =>
            {
                bool changed = adminPassword.Text != (selected != null ? selected.AdminPassword : "");
                Commit();
                if (changed)
                {
                    var _ = LoadCubeSettings();
                }
            };
            function.SelectedIndexChanged += (s, e) => { Commit(); ShowFunctionRows(); };
            design.SelectedIndexChanged += (s, e) => { CommitDesign(); ShowFlip(); };
            showUsed.CheckedChanged += (s, e) => Commit();
            cycle.SelectedIndexChanged += (s, e) => { Commit(); ShowFlip(); };
            shopView.SelectedIndexChanged += (s, e) => { Commit(); ShowFunctionRows(); };
            period.SelectedIndexChanged += (s, e) => Commit();
            periodCycle.SelectedIndexChanged += (s, e) => Commit();
            content.SelectedIndexChanged += (s, e) => Commit();
            bigStyle.SelectedIndexChanged += (s, e) => { Commit(); ShowFunctionRows(); };
            claudeFlip.CheckedChanged += (s, e) => { if (!filling) { bigFlip.Checked = claudeFlip.Checked; Commit(); } };
            bigFlip.CheckedChanged += (s, e) => { if (!filling) { claudeFlip.Checked = bigFlip.Checked; Commit(); } };

            reloadButton.Click += async (s, e) => await LoadCubeSettings();
            saveButton.Click += async (s, e) => await SaveCubeSettings();
            Disposed += (s, e) =>
            {
                if (searchCancel != null)
                {
                    searchCancel.Cancel();
                }
            };
            if (list.Items.Count > 0)
            {
                list.SelectedIndex = 0;
            }
            else
            {
                SelectCube();
            }
        }

        internal override string Title
        {
            get { return "Würfel"; }
        }

        // Opening the page loads the settings of the selected cube again.
        internal override void PageShown()
        {
            if (selected != null && !busy)
            {
                var _ = LoadCubeSettings();
            }
        }

        // "Auf dem Würfel speichern" only after a load worked and while nothing runs.
        internal static bool CanSave(CubeSettings loaded, bool busy)
        {
            return loaded != null && !busy;
        }

        // The functions to choose: all three for the first function, for the
        // second all but the one of the first.
        internal static DesignInfo[] FunctionsFor(CubeEntry cube, bool second)
        {
            if (!second)
            {
                return CubeFunction.All;
            }
            return CubeFunction.All.Where(f => f.Id != cube.Function).ToArray();
        }

        // The function the view fields edit now.
        private CubeEntry Editing
        {
            get { return editingSecond && selected != null && selected.Alternate ? selected.EnsureSecond() : selected; }
        }

        // The hint below the function, null if there is none. Link is the page
        // to open, null for a hint without a link.
        internal static string FunctionHint(CubeInfo info, CubeEntry c, AppConfig cfg, out string link)
        {
            link = null;
            if (info != null && !info.CanFrame)
            {
                return OldFirmwareText;
            }
            if (c.IsShop && !cfg.SourceConfigured(c.ShopSource))
            {
                link = CubeFunction.Label(c.Function);
                return "Zugang fehlt noch: Seite „" + link + "“ öffnen";
            }
            return null;
        }

        private static Label SectionTitle(string text)
        {
            return new Label
            {
                Text = text,
                AutoSize = true,
                Font = new Font(SystemFonts.MessageBoxFont, FontStyle.Bold),
                Anchor = AnchorStyles.Left,
                Margin = new Padding(0, 6, 16, 4)
            };
        }

        private static Label Line()
        {
            return new Label { Height = 1, Width = 460, BackColor = SystemColors.ControlDark, Margin = new Padding(0, 2, 0, 0) };
        }

        private sealed class Item
        {
            internal CubeEntry Cube;
            public override string ToString()
            {
                return Cube.Title.Length > 0 ? Cube.Title : "(neu, ohne Adresse)";
            }
        }

        private void FillList()
        {
            list.Items.Clear();
            foreach (CubeEntry c in Ctx.Config.Cubes)
            {
                list.Items.Add(new Item { Cube = c });
            }
        }

        private void RefreshList()
        {
            for (int i = 0; i < list.Items.Count; i++)
            {
                list.Items[i] = list.Items[i];  // redraws the text
            }
        }

        private CubeInfo InfoOf(CubeEntry c)
        {
            CubeInfo info = null;
            if (c != null)
            {
                infos.TryGetValue(c.Address, out info);
            }
            return info;
        }

        // Fields of the selected cube into the working copy, then save. The
        // design is taken only when it is chosen (CommitDesign), so a shorter
        // list for old firmware never overwrites it.
        private void Commit()
        {
            if (selected == null || filling)
            {
                return;
            }
            CubeEntry c = selected;
            string before = Snapshot(c);
            string addr = address.Text.Trim();
            if (addr.Length > 0)
            {
                c.Address = addr;
            }
            // The view fields belong to the function edited now.
            CubeEntry v = Editing;
            v.Function = Picked(function, v.Function);
            v.ShowUsed = showUsed.Checked;
            v.CycleSeconds = CubeEntry.CycleChoices[Math.Max(0, cycle.SelectedIndex)];
            if (v.IsShop)
            {
                v.ShopView = Picked(shopView, v.ShopView);
                v.BigPeriod = Picked(period, v.BigPeriod);
                v.BigContent = Picked(content, v.BigContent);
                v.BigStyle = Picked(bigStyle, v.BigStyle);
                v.PeriodCycleSeconds = CubeEntry.CycleChoices[Math.Max(0, periodCycle.SelectedIndex)];
            }
            v.Animate = claudeFlip.Checked;
            c.Alternate = alternate.Checked;
            c.AlternateSeconds = (int)alternateSeconds.Value;
            if (c.Alternate || c.Second != null)
            {
                // The second function never equals the first.
                c.EnsureSecond();
            }
            if (apiKey.Text.Trim() != c.ApiKey)
            {
                c.ApiKey = apiKey.Text.Trim();
            }
            if (adminPassword.Text != c.AdminPassword)
            {
                c.AdminPassword = adminPassword.Text;
            }
            string after = Snapshot(c);
            if (before != after)
            {
                RefreshList();
                Ctx.Save();
            }
            if (addr.Length == 0)
            {
                cubeStatus.Text = "Ohne Adresse sendet das Programm nichts an diesen Würfel.";
            }
        }

        private void CommitDesign()
        {
            if (selected == null || filling)
            {
                return;
            }
            var d = design.SelectedItem as DesignInfo;
            CubeEntry v = Editing;
            if (d != null && v.Designs.Contains(d) && d.Id != v.Design)
            {
                v.Design = d.Id;
                selected.AdoptCubeDesign = false;
                Ctx.Save();
            }
        }

        private static string Snapshot(CubeEntry c)
        {
            return string.Join("|", c.Address, c.Function, c.Design, c.ShowUsed, c.CycleSeconds, c.ApiKeyProtected, c.AdminPasswordProtected,
                               c.ShopView, c.BigPeriod, c.BigContent, c.BigStyle, c.PeriodCycleSeconds, c.Animate,
                               c.Alternate, c.AlternateSeconds, c.Second != null ? Snapshot(c.Second) : "");
        }

        private static string Picked(ComboBox box, string current)
        {
            var d = box.SelectedItem as DesignInfo;
            return d != null ? d.Id : current;
        }

        private static void Pick(ComboBox box, DesignInfo[] list, string id)
        {
            box.SelectedItem = Array.Find(list, d => d.Id == id) ?? list[0];
        }

        private void SelectCube()
        {
            var item = list.SelectedItem as Item;
            // RefreshList() after a save selects the same cube again: nothing to do.
            if (item != null && item.Cube == selected)
            {
                return;
            }
            Commit();
            selected = item != null ? item.Cube : null;
            editingSecond = false;
            loaded = null;
            loadToken++;
            bool on = selected != null;
            foreach (Control c in new Control[] { address, alternate, alternateSeconds, function, design, showUsed, showLeft, cycle, apiKey, adminPassword, testButton, removeButton, reloadButton })
            {
                c.Enabled = on;
            }
            cubeStatus.Text = "";
            ShowFirmware(null);
            UpdateSaveButton();
            if (!on)
            {
                settingsStatus.Text = "";
                ShowRow(connGrid, functionNote, false);
                return;
            }
            FillFields();
            CubeInfo info = InfoOf(selected);
            ShowCubeArea(info);
            ShowFirmware(info);
            ClearCubeFields();
            var _ = LoadCubeSettings();
            if (info == null && selected.Address.Length > 0)
            {
                var __ = TestCube();
            }
        }

        // The stored values of the selected cube into the fields; the view
        // fields from the function edited now.
        private void FillFields()
        {
            filling = true;
            address.Text = selected.Address;
            apiKey.Text = selected.ApiKey;
            adminPassword.Text = selected.AdminPassword;
            alternate.Checked = selected.Alternate;
            alternateSeconds.Value = Math.Max(alternateSeconds.Minimum, Math.Min(alternateSeconds.Maximum, selected.AlternateSeconds));
            if (!selected.Alternate)
            {
                editingSecond = false;
            }
            CubeEntry v = Editing;
            FillEditWhich();
            showUsed.Checked = v.ShowUsed;
            showLeft.Checked = !v.ShowUsed;
            cycle.SelectedIndex = Math.Max(0, Array.IndexOf(CubeEntry.CycleChoices, v.CycleSeconds));
            Pick(shopView, ShopBig.Views, v.ShopView);
            Pick(period, ShopBig.Periods, v.BigPeriod);
            Pick(content, ShopBig.Contents, v.BigContent);
            Pick(bigStyle, ShopBig.Styles, v.BigStyle);
            periodCycle.SelectedIndex = Math.Max(0, Array.IndexOf(CubeEntry.CycleChoices, v.PeriodCycleSeconds));
            claudeFlip.Checked = bigFlip.Checked = v.Animate;
            FillFunctions();
            filling = false;
            ShowFunctionRows();
        }

        internal void SelectIndex(int index)
        {
            if (index >= 0 && index < list.Items.Count)
            {
                list.SelectedIndex = index;
            }
        }

        // The service took over the design of the cube (a cube from "usage").
        internal void Adopted(CubeEntry c)
        {
            if (c == selected)
            {
                FillFields();
            }
        }

        // "1: Claude Limits" and "2: Shopify", the function edited now chosen.
        private void FillEditWhich()
        {
            bool wasFilling = filling;
            filling = true;
            string second = selected.Second != null ? selected.Second.Function : CubeEntry.OtherFunction(selected.Function);
            string[] items = { "1: " + CubeFunction.Label(selected.Function), "2: " + CubeFunction.Label(second) };
            if (editWhich.Items.Count != 2 || (string)editWhich.Items[0] != items[0] || (string)editWhich.Items[1] != items[1])
            {
                editWhich.Items.Clear();
                editWhich.Items.AddRange(items);
            }
            editWhich.SelectedIndex = editingSecond ? 1 : 0;
            filling = wasFilling;
        }

        private void FillFunctions()
        {
            DesignInfo[] list = FunctionsFor(selected, Editing != selected);
            function.Items.Clear();
            function.Items.AddRange(list);
            Pick(function, list, Editing.Function);
        }

        private static void ShowRow(TableLayoutPanel grid, Control c, bool visible)
        {
            c.Visible = visible;
            Control caption = grid.GetControlFromPosition(0, grid.GetRow(c));
            if (caption != null)
            {
                caption.Visible = visible;
            }
        }

        // Only the fields that count for the function.
        private void ShowFunctionRows()
        {
            if (selected == null)
            {
                return;
            }
            CubeInfo info = InfoOf(selected);
            CubeEntry v = Editing;
            bool claude = !v.IsShop;
            bool big = v.ShopBigView;
            bool wasFilling = filling;
            filling = true;
            DesignInfo[] designs = claude ? ClaudeDesigns.All : ShopDesigns.All;
            design.Items.Clear();
            design.Items.AddRange(designs);
            design.SelectedItem = designs.FirstOrDefault(d => d.Id == v.DesignOrDefault)
                                  ?? designs.FirstOrDefault(d => d.Id == ClaudeDesigns.Default) ?? designs[0];
            filling = wasFilling;
            string link;
            string hint = FunctionHint(info, v, Ctx.Config, out link);
            int linkStart = link != null ? hint.IndexOf("Seite", StringComparison.Ordinal) : -1;
            int linkLength = linkStart >= 0 ? hint.Length - linkStart : 0;
            // Styles with several periods: period and content do not count, they stay stored.
            string style = Picked(bigStyle, v.BigStyle);
            bool allPeriods = big && ShopBig.ShowsAllPeriods(style);
            styleNote.Text = allPeriods ? ShopBig.AllPeriodsHint(style) : "";
            styleNote.Visible = allPeriods;
            functionNote.Text = hint ?? "";
            functionNote.LinkArea = linkStart >= 0 ? new LinkArea(linkStart, linkLength) : new LinkArea(0, 0);
            FillEditWhich();
            SuspendLayout();
            ShowRow(connGrid, editRow, selected.Alternate);
            alternateSeconds.Enabled = selected.Alternate;
            ShowRow(connGrid, functionNote, hint != null);
            EnableRow(connGrid, periodRow, !allPeriods);
            EnableRow(connGrid, contentRow, !allPeriods);
            shopView.Visible = viewCaption.Visible = !claude;
            ShowRow(connGrid, designRow, !big);
            ShowRow(connGrid, modeRow, claude);
            ShowRow(connGrid, cycleRow, !big);
            ShowRow(connGrid, periodRow, big);
            ShowRow(connGrid, contentRow, big);
            ShowRow(connGrid, styleRow, big);
            ShowFlip();
            ResumeLayout();
        }

        private static void EnableRow(TableLayoutPanel grid, Control c, bool enabled)
        {
            c.Enabled = enabled;
            Control caption = grid.GetControlFromPosition(0, grid.GetRow(c));
            if (caption != null)
            {
                caption.Enabled = enabled;
            }
        }

        // The flip animation only counts where Fallblatt can appear.
        private void ShowFlip()
        {
            if (selected == null)
            {
                return;
            }
            var d = design.SelectedItem as DesignInfo;
            bool claudePictures = !Editing.IsShop;
            claudeFlip.Visible = claudePictures && ((d != null && d.Id == "fallblatt") || cycle.SelectedIndex > 0);
            var st = bigStyle.SelectedItem as DesignInfo;
            bigFlip.Visible = st != null && st.Id == "fallblatt";
        }

        private void ShowCubeArea(CubeInfo info)
        {
            bool api = info == null || info.Can("settings");
            cubeGrid.Visible = api;
            reloadButton.Visible = saveButton.Visible = api;
            browserPanel.Visible = !api;
            settingsStatus.Visible = api;
        }

        private void ClearCubeFields()
        {
            night.Checked = false;
            nightFrom.Text = nightTo.Text = deviceName.Text = "";
        }

        // The fields of the cube are only editable with loaded values, so nothing
        // that looks like a setting is a mere placeholder.
        private void UpdateSaveButton()
        {
            cubeGrid.Enabled = selected != null && loaded != null;
            saveButton.Enabled = selected != null && CanSave(loaded, busy);
            reloadButton.Enabled = selected != null && !busy;
        }

        private void AddCube(CubeEntry entry, bool focusAddress)
        {
            Commit();
            Ctx.Config.Cubes.Add(entry);
            FillList();
            list.SelectedIndex = list.Items.Count - 1;
            if (entry.Address.Length > 0)
            {
                Ctx.Save();
            }
            if (focusAddress)
            {
                address.Focus();
            }
        }

        private void AddFound(CubeInfo info)
        {
            infos[info.Ip] = info;
            CubeEntry existing = Ctx.Config.Cubes.FirstOrDefault(c => c.Address == info.Ip);
            if (existing != null)
            {
                existing.Name = info.DeviceName ?? "";
                RefreshList();
                Ctx.Save();
                listStatus.Text = "Der Würfel " + info.Ip + " ist schon in der Liste.";
                return;
            }
            AddCube(new CubeEntry { Address = info.Ip, Name = info.DeviceName ?? "", Function = CubeFunction.Claude }, false);
            listStatus.Text = "Hinzugefügt: " + info.Ip;
        }

        private void RemoveCube()
        {
            if (selected == null)
            {
                return;
            }
            if (MessageBox.Show(FindForm(), "Würfel " + selected.Title + " aus der Liste entfernen?", "SysopTV",
                                MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            {
                return;
            }
            Ctx.Config.Cubes.Remove(selected);
            selected = null;
            FillList();
            Ctx.Save();
            if (list.Items.Count > 0)
            {
                list.SelectedIndex = 0;
            }
            else
            {
                SelectCube();
            }
        }

        private async Task Search()
        {
            searchButton.Enabled = false;
            found.Items.Clear();
            found.Visible = false;
            searchCancel = new CancellationTokenSource();
            var progress = new Progress<string>(text => listStatus.Text = text);
            try
            {
                List<CubeInfo> result = await Cube.DiscoverAsync(progress, searchCancel.Token);
                if (IsDisposed)
                {
                    return;
                }
                foreach (CubeInfo info in result)
                {
                    infos[info.Ip] = info;
                    found.Items.Add(info);
                }
                found.Visible = result.Count > 0;
                listStatus.Text = result.Count == 0
                    ? "Kein Würfel gefunden. Ist er eingeschaltet und im selben Netz?"
                    : result.Count + " gefunden, zum Hinzufügen anklicken.";
                Log.Write("Suche: " + result.Count + " Würfel gefunden");
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                if (!IsDisposed)
                {
                    searchButton.Enabled = true;
                }
            }
        }

        private async Task TestCube()
        {
            Commit();
            CubeEntry c = selected;
            if (c == null || c.Address.Length == 0)
            {
                cubeStatus.Text = "Bitte zuerst eine Adresse eingeben.";
                return;
            }
            testButton.Enabled = false;
            cubeStatus.Text = "Verbinde ...";
            CubeInfo info = await Task.Run(() => ReadInfo(c.Address, TimeSpan.FromSeconds(4)));
            if (IsDisposed)
            {
                return;
            }
            testButton.Enabled = true;
            if (c != selected)
            {
                return;
            }
            if (info == null)
            {
                cubeStatus.Text = "Keine Antwort von SysopTV.";
                return;
            }
            infos[c.Address] = info;
            if (!string.IsNullOrEmpty(info.DeviceName) && info.DeviceName != c.Name)
            {
                c.Name = info.DeviceName;
                RefreshList();
                Ctx.Save();
            }
            cubeStatus.Text = "Erreicht, Laufzeit " + info.UptimeText + (info.Rescue ? ", Rettungsmodus" : "");
            ShowFunctionRows();
            ShowCubeArea(info);
            ShowFirmware(info);
        }

        private void OpenBrowser(string path)
        {
            string addr = selected != null && selected.Address.Length > 0 ? selected.Address : "sysoptv.local";
            try
            {
                Process.Start(Net.BaseUrl(addr) + path);
            }
            catch (Exception ex)
            {
                MessageBox.Show(FindForm(), "Der Browser ließ sich nicht öffnen: " + ex.Message, "SysopTV");
            }
        }

        // ---- settings stored in the cube, over /api/settings ----

        // Loads without asking when an admin password is set. A failed load keeps
        // saving locked, the error stands next to the buttons.
        private async Task<bool> LoadCubeSettings()
        {
            CubeEntry c = selected;
            if (c == null)
            {
                return false;
            }
            int token = ++loadToken;
            loaded = null;
            UpdateSaveButton();
            if (c.Address.Length == 0)
            {
                settingsStatus.Text = "Ohne Adresse lässt sich nichts laden.";
                return false;
            }
            if (c.AdminPassword.Length == 0)
            {
                settingsStatus.Text = "Zum Laden bitte oben das Admin-Passwort eintragen.";
                return false;
            }
            busy = true;
            UpdateSaveButton();
            settingsStatus.Text = "Lade ...";
            CubeSettings s = null;
            string error = null;
            try
            {
                s = await Task.Run(() => ReadSettings(c.Address, c.AdminPassword));
            }
            catch (InvalidOperationException ex)
            {
                error = ex.Message;
            }
            if (IsDisposed || token != loadToken || c != selected)
            {
                return false;
            }
            busy = false;
            if (s == null)
            {
                settingsStatus.Text = "Laden fehlgeschlagen: " + error;
                UpdateSaveButton();
                return false;
            }
            ShowLoaded(s);
            settingsStatus.Text = "Geladen um " + DateTime.Now.ToString("HH:mm:ss") + ".";
            UpdateSaveButton();
            return true;
        }

        private void ShowLoaded(CubeSettings s)
        {
            loaded = s;
            brightness.Value = Math.Min(100, Math.Max(10, s.Brightness));
            night.Checked = s.NightEnabled;
            nightFrom.Text = s.NightFrom;
            nightTo.Text = s.NightTo;
            nightBrightness.Value = Math.Min(100, Math.Max(0, s.NightBrightness));
            deviceName.Text = s.DeviceName;
        }

        private async Task SaveCubeSettings()
        {
            Commit();
            CubeEntry c = selected;
            if (c == null || !CanSave(loaded, busy))
            {
                return;
            }
            var s = new CubeSettings
            {
                Brightness = (int)brightness.Value,
                NightEnabled = night.Checked,
                NightFrom = nightFrom.Text.Trim(),
                NightTo = nightTo.Text.Trim(),
                NightBrightness = (int)nightBrightness.Value,
                DeviceName = deviceName.Text.Trim()
            };
            busy = true;
            UpdateSaveButton();
            settingsStatus.Text = "Speichere ...";
            string error = null;
            try
            {
                await Task.Run(() => Cube.SaveSettings(c.Address, c.AdminPassword, s));
            }
            catch (InvalidOperationException ex)
            {
                error = ex.Message;
            }
            busy = false;
            if (IsDisposed || c != selected)
            {
                return;
            }
            if (error != null)
            {
                settingsStatus.Text = "Speichern fehlgeschlagen: " + error;
                UpdateSaveButton();
                return;
            }
            // Read back what the cube really stored.
            if (await LoadCubeSettings())
            {
                settingsStatus.Text = "Gespeichert um " + DateTime.Now.ToString("HH:mm:ss") + ".";
                if (loaded.DeviceName != c.Name)
                {
                    c.Name = loaded.DeviceName;
                    RefreshList();
                    Ctx.Save();
                }
            }
        }

        // ---- firmware update ----

        // "Firmware 0.4.3" below the list, and the button if the program has a
        // newer one. Only for cubes that answer as SysopTV.
        // The newest firmware: local, or from the GitHub release while updates may be used.
        private BundledFirmware CurrentFirmware()
        {
            return BundledFirmware.Best(FindFirmware(), Ctx.Service.UsableRelease);
        }

        private void ShowFirmware(CubeInfo info)
        {
            BundledFirmware fw = CurrentFirmware();
            if (info == null)
            {
                firmwareLabel.Text = selected != null ? "Firmware: unbekannt" : "";
                updateButton.Visible = false;
                return;
            }
            bool older = fw != null && fw.NewerThan(info);
            firmwareLabel.Text = "Firmware " + info.Version + (older ? ", " + fw.Version + " verfügbar" + (fw.FromGitHub ? " (GitHub)" : "") : fw != null ? ", aktuell" : "");
            updateButton.Visible = older;
            updateButton.Enabled = !busy;
        }

        private async Task UpdateFirmware()
        {
            Commit();
            CubeEntry c = selected;
            BundledFirmware fw = CurrentFirmware();
            if (c == null || fw == null || busy)
            {
                return;
            }
            updateButton.Enabled = false;
            cubeStatus.Text = "Prüfe den Würfel ...";
            CubeInfo info = await Task.Run(() => ReadInfo(c.Address, TimeSpan.FromSeconds(4)));
            if (IsDisposed || c != selected)
            {
                return;
            }
            if (info == null || !fw.NewerThan(info))
            {
                cubeStatus.Text = info == null ? "Keine Antwort von SysopTV. Aktualisiert werden nur Würfel mit SysopTV-Firmware." : "Die Firmware ist schon aktuell.";
                ShowFirmware(info);
                return;
            }
            string question = "Firmware von " + c.Title + " von " + info.Version + " auf " + fw.Version + " aktualisieren?\n\n" +
                              "Der Würfel startet dabei neu und zeigt etwa eine Minute lang nichts vom Programm. Während des Updates nicht den Strom trennen.";
            if (MessageBox.Show(FindForm(), question, "SysopTV", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            {
                updateButton.Enabled = true;
                cubeStatus.Text = "";
                return;
            }
            if (fw.FromGitHub)
            {
                // Download from the release, checked against SHA256SUMS.txt, kept in the data folder.
                cubeStatus.Text = "Lade Firmware " + fw.Version + " von GitHub ...";
                string dlError = null;
                bool ok = await Task.Run(() => fw.EnsureLocal(out dlError));
                if (IsDisposed)
                {
                    return;
                }
                if (!ok)
                {
                    cubeStatus.Text = dlError;
                    updateButton.Enabled = true;
                    Log.Write("Firmware " + fw.Version + " von GitHub nicht verwendet: " + dlError);
                    MessageBox.Show(FindForm(), dlError + " Es wurde nichts aufgespielt.", "SysopTV", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
            }
            byte[] image;
            try
            {
                image = System.IO.File.ReadAllBytes(fw.Path);
            }
            catch (Exception ex)
            {
                cubeStatus.Text = "Die Firmware-Datei ließ sich nicht lesen: " + ex.Message;
                updateButton.Enabled = true;
                return;
            }

            string password = c.AdminPassword;
            busy = true;
            UpdateSaveButton();
            UpdateResult result = null;
            Log.Write("Firmware-Update " + c.Title + ": " + info.Version + " auf " + fw.Version);
            while (true)
            {
                string pw = password;
                Action<string> progress = text => { if (!IsDisposed) { BeginInvoke((Action)(() => cubeStatus.Text = text)); } };
                Ctx.Service.BeginUpdate(c.Address);
                try
                {
                    result = await Task.Run(() => new FirmwareUpdater().Run(c.Address, pw, System.IO.Path.GetFileName(fw.Path), image, fw.Version, progress));
                }
                finally
                {
                    Ctx.Service.EndUpdate(c.Address);
                }
                if (IsDisposed)
                {
                    return;
                }
                if (result.Outcome != UpdateOutcome.PasswordNeeded && result.Outcome != UpdateOutcome.WrongPassword)
                {
                    break;
                }
                // Ask for the admin password, then try again; kept once it worked.
                string asked = PasswordPrompt.Ask(FindForm(), result.Outcome == UpdateOutcome.WrongPassword
                    ? "Das Admin-Passwort von " + c.Title + " stimmt nicht. Bitte noch einmal eingeben:"
                    : "Für das Update braucht das Programm das Admin-Passwort von " + c.Title + ":");
                if (asked == null)
                {
                    break;
                }
                password = asked;
            }
            busy = false;
            UpdateSaveButton();
            Log.Write("Firmware-Update " + c.Title + ": " + result.Outcome + ", " + result.Message);
            bool authWorked = result.Outcome != UpdateOutcome.PasswordNeeded && result.Outcome != UpdateOutcome.WrongPassword &&
                              result.Outcome != UpdateOutcome.NotReachable;
            if (authWorked && password != c.AdminPassword && !string.IsNullOrEmpty(password))
            {
                c.AdminPassword = password;
                if (c == selected)
                {
                    filling = true;
                    adminPassword.Text = password;
                    filling = false;
                }
                Ctx.Save();
            }
            if (c != selected)
            {
                return;
            }
            cubeStatus.Text = result.Message;
            if (result.Outcome != UpdateOutcome.Done)
            {
                MessageBox.Show(FindForm(), result.Message, "SysopTV", MessageBoxButtons.OK,
                                result.Outcome == UpdateOutcome.NoRestart || result.Outcome == UpdateOutcome.Aborted ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
            }
            await TestCube();
            if (result.Outcome == UpdateOutcome.Done)
            {
                cubeStatus.Text = result.Message;
            }
        }
    }
}
