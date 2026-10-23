// SysopTV, Copyright (C) 2026 Christoph Scheel
// Licensed under the GNU General Public License v3.0, see LICENSE.
//
// Page "Würfel": the list of cubes on the left, the details of the selected
// cube on the right. The upper part is stored in the program and saved as
// soon as a field is left. The lower part is stored in the cube itself and
// only changed with "Auf dem Würfel speichern".

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

        // List
        private readonly ListBox list = new ListBox { Width = 180, Height = 160, IntegralHeight = false };
        private readonly Button searchButton = new Button { Text = "Suchen", AutoSize = true };
        private readonly Button addButton = new Button { Text = "Von Hand hinzufügen", AutoSize = true };
        private readonly Button removeButton = new Button { Text = "Entfernen", AutoSize = true };
        private readonly ListBox found = new ListBox { Width = 180, Height = 80, IntegralHeight = false, Visible = false };
        private readonly Label listStatus = new Label { AutoSize = true, MaximumSize = new Size(180, 0), ForeColor = SystemColors.GrayText };

        // Stored in the program
        private readonly TextBox address = new TextBox { Width = 240 };
        private readonly ComboBox function = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 240 };
        private readonly ComboBox design = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
        private readonly RadioButton showUsed = new RadioButton { Text = "verbraucht", AutoSize = true };
        private readonly RadioButton showLeft = new RadioButton { Text = "übrig", AutoSize = true };
        private readonly ComboBox cycle = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
        private readonly TextBox apiKey = new TextBox { Width = 240, UseSystemPasswordChar = true };
        private readonly TextBox adminPassword = new TextBox { Width = 240, UseSystemPasswordChar = true };
        private readonly Button testButton = new Button { Text = "Verbindung testen", AutoSize = true };
        private readonly Label cubeStatus = new Label { AutoSize = true, MaximumSize = new Size(300, 0), ForeColor = SystemColors.GrayText, Margin = new Padding(0, 6, 0, 0) };
        private readonly Label designNote = new Label { AutoSize = true, MaximumSize = new Size(300, 0), ForeColor = SystemColors.GrayText, Margin = new Padding(0, 4, 0, 4) };
        private readonly TableLayoutPanel details = Grid();
        private Control designRow, modeRow, cycleRow;

        // Stored in the cube
        private readonly Panel cubePanel = new Panel { AutoSize = true };
        private readonly Panel browserPanel = new Panel { AutoSize = true, Visible = false };
        private readonly TableLayoutPanel cubeGrid = Grid();
        private readonly ComboBox fwDesign = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
        private readonly ComboBox fwCycle = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
        private readonly RadioButton fwUsed = new RadioButton { Text = "verbraucht", AutoSize = true };
        private readonly RadioButton fwLeft = new RadioButton { Text = "übrig", AutoSize = true };
        private readonly NumericUpDown brightness = new NumericUpDown { Minimum = 10, Maximum = 100, Increment = 5, Width = 60 };
        private readonly CheckBox night = new CheckBox { Text = "an", AutoSize = true, Margin = new Padding(0, 4, 8, 0) };
        private readonly TextBox nightFrom = new TextBox { Width = 50 };
        private readonly TextBox nightTo = new TextBox { Width = 50 };
        private readonly NumericUpDown nightBrightness = new NumericUpDown { Minimum = 0, Maximum = 100, Increment = 5, Width = 60 };
        private readonly TextBox deviceName = new TextBox { Width = 180, MaxLength = 24 };
        private readonly Button loadSettings = new Button { Text = "Vom Würfel laden", AutoSize = true };
        private readonly Button saveSettings = new Button { Text = "Auf dem Würfel speichern", AutoSize = true };
        private readonly Label settingsStatus = new Label { AutoSize = true, MaximumSize = new Size(300, 0), ForeColor = SystemColors.GrayText, Margin = new Padding(0, 6, 0, 0) };
        private Control fwDesignRow, fwCycleRow, fwModeRow;

        private CubeEntry selected;
        private CubeSettings loaded;
        private bool filling;
        private readonly Dictionary<string, CubeInfo> infos = new Dictionary<string, CubeInfo>();
        private CancellationTokenSource searchCancel;

        internal CubesPage(PageContext ctx) : base(ctx)
        {
            cycle.Items.AddRange(CycleLabels);
            fwCycle.Items.AddRange(CycleLabels);

            var left = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, Margin = new Padding(0, 0, 16, 0) };
            left.Controls.Add(list);
            left.Controls.Add(Stack(searchButton, addButton, removeButton));
            left.Controls.Add(found);
            left.Controls.Add(listStatus);
            foreach (Button b in new[] { searchButton, addButton, removeButton })
            {
                b.Width = 180;
                b.AutoSize = false;
                b.Height = 28;
                b.Margin = new Padding(0, 4, 0, 0);
            }

            AddRow(details, "Adresse (IP oder Name)", address);
            AddRow(details, "Funktion", function);
            designRow = Row(design);
            AddRow(details, "Design", designRow);
            modeRow = Row(showUsed, showLeft);
            AddRow(details, "Prozent zeigen", modeRow);
            cycleRow = Row(cycle);
            AddRow(details, "Automatisch wechseln", cycleRow);
            AddRow(details, null, designNote);
            AddRow(details, "API-Schlüssel (optional)", apiKey);
            AddRow(details, "Admin-Passwort", adminPassword);
            AddRow(details, null, testButton);
            AddRow(details, null, cubeStatus);

            fwDesignRow = fwDesign;
            AddRow(cubeGrid, "Design im Würfel", fwDesignRow);
            fwCycleRow = fwCycle;
            AddRow(cubeGrid, "Automatisch wechseln", fwCycleRow);
            fwModeRow = Row(fwUsed, fwLeft);
            AddRow(cubeGrid, "Prozent zeigen", fwModeRow);
            AddRow(cubeGrid, "Helligkeit", Row(brightness, Caption("%")));
            AddRow(cubeGrid, "Nachtmodus", Row(night, Caption("von"), nightFrom, Caption("bis"), nightTo));
            AddRow(cubeGrid, "Helligkeit nachts", Row(nightBrightness, Caption("%")));
            AddRow(cubeGrid, "Gerätename", deviceName);
            AddRow(cubeGrid, null, Row(loadSettings, saveSettings));
            AddRow(cubeGrid, null, settingsStatus);
            cubeGrid.Dock = DockStyle.Fill;
            cubePanel.Controls.Add(cubeGrid);

            var info = Note("Dieser Würfel hat noch keine Schnittstelle für Einstellungen (ab Firmware 0.4.0). Die Seite im Browser kann alles einstellen.");
            var open = new Button { Text = "Einstellungen am Würfel öffnen", AutoSize = true, Top = 40 };
            open.Click += (s, e) => OpenBrowser("settings");
            browserPanel.Controls.Add(info);
            browserPanel.Controls.Add(open);

            var right = Stack(details, new Label
            {
                Text = "Im Würfel gespeichert",
                AutoSize = true,
                Font = new Font(SystemFonts.MessageBoxFont, FontStyle.Bold),
                Margin = new Padding(0, 8, 0, 4)
            }, cubePanel, browserPanel);
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

            // Program settings: saved when a field is left or a choice changes.
            address.Leave += (s, e) => Commit();
            apiKey.Leave += (s, e) => Commit();
            adminPassword.Leave += (s, e) => Commit();
            function.SelectedIndexChanged += (s, e) => { Commit(); ShowFunctionRows(); };
            design.SelectedIndexChanged += (s, e) => Commit();
            showUsed.CheckedChanged += (s, e) => Commit();
            cycle.SelectedIndexChanged += (s, e) => Commit();

            loadSettings.Click += async (s, e) => await LoadCubeSettings();
            saveSettings.Click += async (s, e) => await SaveCubeSettings();
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
        }

        internal override string Title
        {
            get { return "Würfel"; }
        }

        private sealed class Item
        {
            internal CubeEntry Cube;
            public override string ToString()
            {
                return Cube.Title.Length > 0 ? Cube.Title : "(neu, ohne Adresse)";
            }
        }

        private sealed class Choice
        {
            internal string Value;
            internal string Label;
            public override string ToString() { return Label; }
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

        // Fields of the selected cube into the working copy, then save.
        private void Commit()
        {
            if (selected == null || filling)
            {
                return;
            }
            CubeEntry c = selected;
            string before = c.Address + "|" + c.Function + "|" + c.Design + "|" + c.ShowUsed + "|" + c.CycleSeconds + "|" + c.ApiKeyProtected + "|" + c.AdminPasswordProtected;
            string addr = address.Text.Trim();
            if (addr.Length > 0)
            {
                c.Address = addr;
            }
            var f = function.SelectedItem as Choice;
            if (f != null)
            {
                c.Function = f.Value;
            }
            var d = design.SelectedItem as DesignInfo;
            if (d != null && c.Designs != null && c.Designs.Contains(d))
            {
                c.Design = d.Id;
            }
            c.ShowUsed = showUsed.Checked;
            c.CycleSeconds = CubeEntry.CycleChoices[Math.Max(0, cycle.SelectedIndex)];
            if (apiKey.Text.Trim() != c.ApiKey)
            {
                c.ApiKey = apiKey.Text.Trim();
            }
            if (adminPassword.Text != c.AdminPassword)
            {
                c.AdminPassword = adminPassword.Text;
            }
            string after = c.Address + "|" + c.Function + "|" + c.Design + "|" + c.ShowUsed + "|" + c.CycleSeconds + "|" + c.ApiKeyProtected + "|" + c.AdminPasswordProtected;
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

        private void SelectCube()
        {
            Commit();
            var item = list.SelectedItem as Item;
            selected = item != null ? item.Cube : null;
            loaded = null;
            bool on = selected != null;
            foreach (Control c in new Control[] { address, function, design, showUsed, showLeft, cycle, apiKey, adminPassword, testButton, removeButton, loadSettings, saveSettings })
            {
                c.Enabled = on;
            }
            cubeStatus.Text = "";
            settingsStatus.Text = "";
            if (!on)
            {
                return;
            }
            filling = true;
            address.Text = selected.Address;
            apiKey.Text = selected.ApiKey;
            adminPassword.Text = selected.AdminPassword;
            showUsed.Checked = selected.ShowUsed;
            showLeft.Checked = !selected.ShowUsed;
            cycle.SelectedIndex = Math.Max(0, Array.IndexOf(CubeEntry.CycleChoices, selected.CycleSeconds));
            CubeInfo info;
            infos.TryGetValue(selected.Address, out info);
            FillFunctions(info);
            filling = false;
            ShowFunctionRows();
            ShowCubeArea(info);
            ClearCubeFields();
            if (info == null && selected.Address.Length > 0)
            {
                var _ = TestCube();
            }
        }

        // Picture functions only for cubes that can show pictures (firmware 0.4.0 and newer).
        private void FillFunctions(CubeInfo info)
        {
            function.Items.Clear();
            var values = new List<string> { CubeFunction.Usage };
            if (info != null && info.Can("frame") || selected.Function != CubeFunction.Usage)
            {
                values.Add(CubeFunction.ClaudeFrame);
                values.Add(CubeFunction.ShopFrame);
            }
            foreach (string v in values)
            {
                function.Items.Add(new Choice { Value = v, Label = CubeFunction.Label(v) });
            }
            function.SelectedIndex = Math.Max(0, values.IndexOf(selected.Function));
        }

        // Design, mode and change in the program only for pictures; for "usage" the cube decides.
        private void ShowFunctionRows()
        {
            if (selected == null)
            {
                return;
            }
            bool picture = selected.Function != CubeFunction.Usage;
            bool claude = selected.Function == CubeFunction.ClaudeFrame;
            filling = true;
            design.Items.Clear();
            if (picture)
            {
                design.Items.AddRange(selected.Designs);
                design.SelectedItem = selected.Designs.First(d => d.Id == selected.DesignOrDefault);
            }
            filling = false;
            designRow.Enabled = cycleRow.Enabled = picture;
            modeRow.Enabled = claude;
            designNote.Text = picture
                ? "Das Programm zeichnet das Bild selbst."
                : "Das stellt der Würfel selbst ein, siehe unten.";
            fwDesignRow.Visible = fwCycleRow.Visible = fwModeRow.Visible = !picture;
            foreach (Control c in new[] { fwDesignRow, fwCycleRow, fwModeRow })
            {
                cubeGrid.GetControlFromPosition(0, cubeGrid.GetRow(c)).Visible = !picture;
            }
        }

        private void ShowCubeArea(CubeInfo info)
        {
            bool api = info == null || info.Can("settings");
            cubePanel.Visible = api;
            browserPanel.Visible = !api;
        }

        private void ClearCubeFields()
        {
            fwDesign.Items.Clear();
            fwCycle.SelectedIndex = -1;
            fwUsed.Checked = fwLeft.Checked = false;
            night.Checked = false;
            nightFrom.Text = nightTo.Text = deviceName.Text = "";
            settingsStatus.Text = "Mit „Vom Würfel laden“ die Werte holen.";
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
            AddCube(new CubeEntry { Address = info.Ip, Name = info.DeviceName ?? "", Function = CubeFunction.Usage }, false);
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
            cubeStatus.Text = "Verbinde mit " + c.Address + " ...";
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
                cubeStatus.Text = "Unter " + c.Address + " antwortet kein SysopTV.";
                return;
            }
            infos[c.Address] = info;
            if (!string.IsNullOrEmpty(info.DeviceName) && info.DeviceName != c.Name)
            {
                c.Name = info.DeviceName;
                RefreshList();
                Ctx.Save();
            }
            cubeStatus.Text = "Verbunden: SysopTV " + info.Version + ", Laufzeit " + info.UptimeText + (info.Rescue ? ", Rettungsmodus" : "");
            filling = true;
            FillFunctions(info);
            filling = false;
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

        private async Task LoadCubeSettings()
        {
            Commit();
            CubeEntry c = selected;
            if (c == null || c.Address.Length == 0)
            {
                return;
            }
            settingsStatus.Text = "Lade ...";
            try
            {
                CubeSettings s = await Task.Run(() => Cube.GetSettings(c.Address, c.AdminPassword));
                if (c != selected || IsDisposed)
                {
                    return;
                }
                loaded = s;
                fwDesign.Items.Clear();
                foreach (string n in s.DesignNames)
                {
                    fwDesign.Items.Add(n);
                }
                if (fwDesign.Items.Count > 0)
                {
                    fwDesign.SelectedIndex = Math.Min(Math.Max(0, s.Design), fwDesign.Items.Count - 1);
                }
                fwCycle.SelectedIndex = Math.Max(0, Array.IndexOf(CubeEntry.CycleChoices, s.CycleSeconds));
                fwUsed.Checked = s.ShowUsed;
                fwLeft.Checked = !s.ShowUsed;
                brightness.Value = Math.Min(100, Math.Max(10, s.Brightness));
                night.Checked = s.NightEnabled;
                nightFrom.Text = s.NightFrom;
                nightTo.Text = s.NightTo;
                nightBrightness.Value = Math.Min(100, Math.Max(0, s.NightBrightness));
                deviceName.Text = s.DeviceName;
                settingsStatus.Text = "Geladen.";
            }
            catch (InvalidOperationException ex)
            {
                settingsStatus.Text = ex.Message;
            }
        }

        private async Task SaveCubeSettings()
        {
            Commit();
            CubeEntry c = selected;
            if (c == null || loaded == null)
            {
                settingsStatus.Text = "Bitte zuerst vom Würfel laden.";
                return;
            }
            bool usage = c.Function == CubeFunction.Usage;
            var s = new CubeSettings
            {
                // For picture cubes the design settings of the cube stay as they were.
                Design = usage && fwDesign.SelectedIndex >= 0 ? fwDesign.SelectedIndex : loaded.Design,
                CycleSeconds = usage ? CubeEntry.CycleChoices[Math.Max(0, fwCycle.SelectedIndex)] : loaded.CycleSeconds,
                ShowUsed = usage ? fwUsed.Checked : loaded.ShowUsed,
                Brightness = (int)brightness.Value,
                NightEnabled = night.Checked,
                NightFrom = nightFrom.Text.Trim(),
                NightTo = nightTo.Text.Trim(),
                NightBrightness = (int)nightBrightness.Value,
                DeviceName = deviceName.Text.Trim()
            };
            settingsStatus.Text = "Speichere ...";
            try
            {
                CubeSettings saved = await Task.Run(() => Cube.SaveSettings(c.Address, c.AdminPassword, s));
                loaded = saved;
                if (saved.DeviceName != c.Name)
                {
                    c.Name = saved.DeviceName;
                    RefreshList();
                    Ctx.Save();
                }
                settingsStatus.Text = "Auf dem Würfel gespeichert.";
            }
            catch (InvalidOperationException ex)
            {
                settingsStatus.Text = ex.Message;
            }
        }
    }
}
