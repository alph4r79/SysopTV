// SysopTV, Copyright (C) 2026 Christoph Scheel
// Licensed under the GNU General Public License v3.0, see LICENSE.
//
// Page "Würfel": the list of cubes on the left, the selected cube on the
// right in two parts. "Verbindung und Funktion" is stored in the program and
// saved as soon as a field is left. The function is Claude Limits, Shopify or
// WooCommerce; the program draws every picture itself, cubes without picture
// mode (firmware before 0.4.0) get only the numbers. "Einstellungen im Würfel"
// is stored in the cube: loaded on its own when an admin password is set, and
// written only with "Auf dem Würfel speichern", which stays locked until a
// load worked.

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

        internal const string OldFirmwareText = "Firmware zu alt für alle Designs. Bitte die Firmware aktualisieren.";
        internal const string FallbackText = "Zeigt der Würfel nur, wenn einige Minuten keine Bilder vom Programm kommen.";

        // List
        private readonly ListBox list = new ListBox { Width = 170, Height = 150, IntegralHeight = false };
        private readonly Button searchButton = new Button { Text = "Suchen" };
        private readonly Button addButton = new Button { Text = "Von Hand hinzufügen" };
        private readonly Button removeButton = new Button { Text = "Entfernen" };
        private readonly ListBox found = new ListBox { Width = 170, Height = 80, IntegralHeight = false, Visible = false };
        private readonly Label listStatus = new Label { AutoSize = true, MaximumSize = new Size(170, 0), ForeColor = SystemColors.GrayText };

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
        private readonly Label viewCaption = Caption("Ansicht");
        private Control designRow, modeRow, cycleRow, periodRow, contentRow, styleRow;

        // Einstellungen im Würfel (stored in the cube)
        private readonly TableLayoutPanel cubeGrid = Grid();
        private readonly Panel browserPanel = new Panel { AutoSize = true, Visible = false };
        private readonly ComboBox fwDesign = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120 };
        private readonly RadioButton fwUsed = new RadioButton { Text = "verbraucht", AutoSize = true, Margin = new Padding(8, 3, 0, 0) };
        private readonly RadioButton fwLeft = new RadioButton { Text = "übrig", AutoSize = true };
        private readonly NumericUpDown brightness = new NumericUpDown { Minimum = 10, Maximum = 100, Increment = 5, Width = 60 };
        private readonly CheckBox night = new CheckBox { Text = "an", AutoSize = true, Margin = new Padding(0, 5, 8, 0) };
        private readonly TextBox nightFrom = new TextBox { Width = 50 };
        private readonly TextBox nightTo = new TextBox { Width = 50 };
        private readonly NumericUpDown nightBrightness = new NumericUpDown { Minimum = 0, Maximum = 100, Increment = 5, Width = 60 };
        private readonly TextBox deviceName = new TextBox { Width = 180, MaxLength = 24 };
        private readonly Button reloadButton = new Button { Text = "Neu laden", AutoSize = true };
        private readonly Button saveButton = new Button { Text = "Auf dem Würfel speichern", AutoSize = true };
        private readonly Label settingsStatus = new Label { AutoSize = true, MaximumSize = new Size(400, 0), ForeColor = SystemColors.GrayText, Margin = new Padding(0, 2, 0, 4) };

        private CubeEntry selected;
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
            // The result of "Verbindung testen" stands below the list.
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
            styleRow = Row(bigStyle, bigFlip);
            AddRow(connGrid, "Stil", styleRow);
            AddRow(connGrid, "API-Schlüssel (optional)", apiKey);
            AddRow(connGrid, "Admin-Passwort", adminPassword);

            // The design of the cube itself only shows when the pictures stop.
            var fallbackCaption = Caption("Ersatzanzeige, wenn der PC nichts schickt");
            fallbackCaption.MaximumSize = new Size(170, 0);
            int fallbackRow = cubeGrid.RowCount;
            cubeGrid.RowCount = fallbackRow + 1;
            cubeGrid.Controls.Add(fallbackCaption, 0, fallbackRow);
            cubeGrid.Controls.Add(Row(fwDesign, fwUsed, fwLeft), 1, fallbackRow);
            var fallbackNote = Note(FallbackText);
            fallbackNote.MaximumSize = new Size(480, 0);
            fallbackNote.Margin = new Padding(0, 0, 0, 6);
            int noteRow = cubeGrid.RowCount;
            cubeGrid.RowCount = noteRow + 1;
            cubeGrid.Controls.Add(fallbackNote, 0, noteRow);
            cubeGrid.SetColumnSpan(fallbackNote, 2);
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
                    openPage(CubeFunction.Label(selected.Function));
                }
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
            bigStyle.SelectedIndexChanged += (s, e) => { Commit(); ShowFlip(); };
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

        // The functions a cube can have: Shopify and WooCommerce need picture
        // mode. A cube that is not reached yet gets all three. A shop cube keeps
        // its function in the list, so nothing changes without being chosen.
        internal static DesignInfo[] FunctionsFor(CubeInfo info, CubeEntry c)
        {
            if (info == null || info.CanFrame || c.IsShop)
            {
                return CubeFunction.All;
            }
            return new[] { CubeFunction.All[0] };
        }

        // The designs for Claude Limits: all ten with picture mode, otherwise
        // the six the firmware draws itself.
        internal static DesignInfo[] ClaudeDesignsFor(CubeInfo info)
        {
            if (info != null && !info.CanFrame)
            {
                return ClaudeDesigns.All.Take(ClaudeDesigns.FirmwareCount).ToArray();
            }
            return ClaudeDesigns.All;
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
            c.Function = Picked(function, c.Function);
            c.ShowUsed = showUsed.Checked;
            c.CycleSeconds = CubeEntry.CycleChoices[Math.Max(0, cycle.SelectedIndex)];
            if (c.IsShop)
            {
                c.ShopView = Picked(shopView, c.ShopView);
                c.BigPeriod = Picked(period, c.BigPeriod);
                c.BigContent = Picked(content, c.BigContent);
                c.BigStyle = Picked(bigStyle, c.BigStyle);
                c.PeriodCycleSeconds = CubeEntry.CycleChoices[Math.Max(0, periodCycle.SelectedIndex)];
            }
            c.Animate = claudeFlip.Checked;
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
            if (d != null && selected.Designs.Contains(d) && d.Id != selected.Design)
            {
                selected.Design = d.Id;
                selected.AdoptCubeDesign = false;
                Ctx.Save();
            }
        }

        private static string Snapshot(CubeEntry c)
        {
            return string.Join("|", c.Address, c.Function, c.Design, c.ShowUsed, c.CycleSeconds, c.ApiKeyProtected, c.AdminPasswordProtected,
                               c.ShopView, c.BigPeriod, c.BigContent, c.BigStyle, c.PeriodCycleSeconds, c.Animate);
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
            Commit();
            var item = list.SelectedItem as Item;
            selected = item != null ? item.Cube : null;
            loaded = null;
            loadToken++;
            bool on = selected != null;
            foreach (Control c in new Control[] { address, function, design, showUsed, showLeft, cycle, apiKey, adminPassword, testButton, removeButton, reloadButton })
            {
                c.Enabled = on;
            }
            cubeStatus.Text = "";
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
            ClearCubeFields();
            var _ = LoadCubeSettings();
            if (info == null && selected.Address.Length > 0)
            {
                var __ = TestCube();
            }
        }

        // The stored values of the selected cube into the fields.
        private void FillFields()
        {
            filling = true;
            address.Text = selected.Address;
            apiKey.Text = selected.ApiKey;
            adminPassword.Text = selected.AdminPassword;
            showUsed.Checked = selected.ShowUsed;
            showLeft.Checked = !selected.ShowUsed;
            cycle.SelectedIndex = Math.Max(0, Array.IndexOf(CubeEntry.CycleChoices, selected.CycleSeconds));
            Pick(shopView, ShopBig.Views, selected.ShopView);
            Pick(period, ShopBig.Periods, selected.BigPeriod);
            Pick(content, ShopBig.Contents, selected.BigContent);
            Pick(bigStyle, ShopBig.Styles, selected.BigStyle);
            periodCycle.SelectedIndex = Math.Max(0, Array.IndexOf(CubeEntry.CycleChoices, selected.PeriodCycleSeconds));
            claudeFlip.Checked = bigFlip.Checked = selected.Animate;
            FillFunctions(InfoOf(selected));
            filling = false;
            ShowFunctionRows();
        }

        // The service took over the design of the cube (a cube from "usage").
        internal void Adopted(CubeEntry c)
        {
            if (c == selected)
            {
                FillFields();
            }
        }

        private void FillFunctions(CubeInfo info)
        {
            DesignInfo[] list = FunctionsFor(info, selected);
            function.Items.Clear();
            function.Items.AddRange(list);
            Pick(function, list, selected.Function);
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
            bool claude = !selected.IsShop;
            bool big = selected.ShopBigView;
            bool wasFilling = filling;
            filling = true;
            DesignInfo[] designs = claude ? ClaudeDesignsFor(info) : ShopDesigns.All;
            design.Items.Clear();
            design.Items.AddRange(designs);
            design.SelectedItem = designs.FirstOrDefault(d => d.Id == selected.DesignOrDefault)
                                  ?? designs.FirstOrDefault(d => d.Id == ClaudeDesigns.Default) ?? designs[0];
            filling = wasFilling;
            string link;
            string hint = FunctionHint(info, selected, Ctx.Config, out link);
            functionNote.Text = hint ?? "";
            int linkStart = link != null ? hint.IndexOf("Seite", StringComparison.Ordinal) : -1;
            functionNote.LinkArea = linkStart >= 0 ? new LinkArea(linkStart, hint.Length - linkStart) : new LinkArea(0, 0);
            SuspendLayout();
            ShowRow(connGrid, functionNote, hint != null);
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

        // The flip animation only counts where Fallblatt can appear.
        private void ShowFlip()
        {
            if (selected == null)
            {
                return;
            }
            var d = design.SelectedItem as DesignInfo;
            bool claudePictures = !selected.IsShop && design.Items.Count > ClaudeDesigns.FirmwareCount;
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

        // Design names of the cube, or the built-in list, never an empty list.
        internal static string[] DesignNames(CubeSettings s)
        {
            if (s != null && s.DesignNames.Count > 0)
            {
                return s.DesignNames.ToArray();
            }
            return ClaudeDesigns.All.Take(ClaudeDesigns.FirmwareCount).Select(d => d.Label).ToArray();
        }

        private void ClearCubeFields()
        {
            fwDesign.Items.Clear();
            fwDesign.Items.AddRange(DesignNames(null));
            fwDesign.SelectedIndex = 0;
            fwUsed.Checked = true;
            fwLeft.Checked = false;
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
            CubeInfo info = await Task.Run(() => Cube.GetInfo(c.Address, TimeSpan.FromSeconds(4)));
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
            cubeStatus.Text = "Firmware " + info.Version + ", Laufzeit " + info.UptimeText + (info.Rescue ? ", Rettungsmodus" : "");
            filling = true;
            FillFunctions(info);
            filling = false;
            ShowFunctionRows();
            ShowCubeArea(info);
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
                s = await Task.Run(() => Cube.GetSettings(c.Address, c.AdminPassword));
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
            fwDesign.Items.Clear();
            fwDesign.Items.AddRange(DesignNames(s));
            fwDesign.SelectedIndex = Math.Min(Math.Max(0, s.Design), fwDesign.Items.Count - 1);
            fwUsed.Checked = s.ShowUsed;
            fwLeft.Checked = !s.ShowUsed;
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
                // The change of the cube is not shown here: the fall back keeps what the cube has.
                Design = fwDesign.SelectedIndex,
                CycleSeconds = loaded.CycleSeconds,
                ShowUsed = fwUsed.Checked,
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
    }
}
