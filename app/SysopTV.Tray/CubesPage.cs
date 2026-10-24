// SysopTV, Copyright (C) 2026 Christoph Scheel
// Licensed under the GNU General Public License v3.0, see LICENSE.
//
// Page "Würfel": the list of cubes on the left, the selected cube on the
// right in two parts. "Verbindung und Funktion" is stored in the program and
// saved as soon as a field is left. "Einstellungen im Würfel" is stored in the
// cube: loaded on its own when an admin password is set, and written only with
// "Auf dem Würfel speichern", which stays locked until a load worked.

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
        private readonly ListBox list = new ListBox { Width = 180, Height = 150, IntegralHeight = false };
        private readonly Button searchButton = new Button { Text = "Suchen" };
        private readonly Button addButton = new Button { Text = "Von Hand hinzufügen" };
        private readonly Button removeButton = new Button { Text = "Entfernen" };
        private readonly ListBox found = new ListBox { Width = 180, Height = 80, IntegralHeight = false, Visible = false };
        private readonly Label listStatus = new Label { AutoSize = true, MaximumSize = new Size(180, 0), ForeColor = SystemColors.GrayText };

        // Verbindung und Funktion (stored in the program)
        private readonly TableLayoutPanel connGrid = Grid();
        private readonly TextBox address = new TextBox { Width = 240 };
        private readonly ComboBox function = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 240 };
        private readonly ComboBox design = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
        private readonly RadioButton showUsed = new RadioButton { Text = "verbraucht", AutoSize = true };
        private readonly RadioButton showLeft = new RadioButton { Text = "übrig", AutoSize = true };
        private readonly ComboBox cycle = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
        private readonly TextBox apiKey = new TextBox { Width = 240, UseSystemPasswordChar = true };
        private readonly TextBox adminPassword = new TextBox { Width = 240, UseSystemPasswordChar = true };
        private readonly Button testButton = new Button { Text = "Verbindung testen", AutoSize = true };
        private readonly Label cubeStatus = new Label { AutoSize = true, MaximumSize = new Size(190, 0), ForeColor = SystemColors.GrayText, Margin = new Padding(6, 3, 0, 0) };
        private Control designRow, modeRow, cycleRow;

        // Einstellungen im Würfel (stored in the cube)
        private readonly TableLayoutPanel cubeGrid = Grid();
        private readonly Panel browserPanel = new Panel { AutoSize = true, Visible = false };
        private readonly ComboBox fwDesign = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
        private readonly ComboBox fwCycle = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
        private readonly RadioButton fwUsed = new RadioButton { Text = "verbraucht", AutoSize = true };
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
        private Control fwDesignRow, fwCycleRow, fwModeRow;

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
            fwCycle.Items.AddRange(CycleLabels);

            var left = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, Margin = new Padding(0, 0, 16, 0) };
            left.Controls.Add(list);
            foreach (Button b in new[] { searchButton, addButton, removeButton })
            {
                b.Width = 180;
                b.Height = 28;
                b.Margin = new Padding(0, 4, 0, 0);
                left.Controls.Add(b);
            }
            left.Controls.Add(found);
            left.Controls.Add(listStatus);

            AddRow(connGrid, "Adresse (IP oder Name)", address);
            AddRow(connGrid, "Funktion", function);
            designRow = design;
            AddRow(connGrid, "Design", designRow);
            modeRow = Row(showUsed, showLeft);
            AddRow(connGrid, "Prozent zeigen", modeRow);
            cycleRow = cycle;
            AddRow(connGrid, "Automatisch wechseln", cycleRow);
            AddRow(connGrid, "API-Schlüssel (optional)", apiKey);
            AddRow(connGrid, "Admin-Passwort", adminPassword);
            AddRow(connGrid, null, Row(testButton, cubeStatus));

            fwDesignRow = fwDesign;
            AddRow(cubeGrid, "Design", fwDesignRow);
            fwCycleRow = fwCycle;
            AddRow(cubeGrid, "Automatisch wechseln", fwCycleRow);
            fwModeRow = Row(fwUsed, fwLeft);
            AddRow(cubeGrid, "Prozent zeigen", fwModeRow);
            AddRow(cubeGrid, "Helligkeit", Row(brightness, Caption("%")));
            AddRow(cubeGrid, "Nachtmodus", Row(night, Caption("von"), nightFrom, Caption("bis"), nightTo));
            AddRow(cubeGrid, "Helligkeit nachts", Row(nightBrightness, Caption("%")));
            AddRow(cubeGrid, "Gerätename", deviceName);

            var info = Note("Dieser Würfel hat noch keine Schnittstelle für Einstellungen (ab Firmware 0.4.0). Die Seite im Browser kann alles einstellen.");
            var open = new Button { Text = "Einstellungen am Würfel öffnen", AutoSize = true, Top = 40 };
            open.Click += (s, e) => OpenBrowser("settings");
            browserPanel.Controls.Add(info);
            browserPanel.Controls.Add(open);

            // The buttons sit in the header of their part, so they never slide out of view.
            var cubeHeader = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Margin = new Padding(0, 10, 0, 0) };
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
            design.SelectedIndexChanged += (s, e) => Commit();
            showUsed.CheckedChanged += (s, e) => Commit();
            cycle.SelectedIndexChanged += (s, e) => Commit();

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
            return new Label { Height = 1, Width = 440, BackColor = SystemColors.ControlDark, Margin = new Padding(0, 4, 0, 0) };
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
            var _ = LoadCubeSettings();
            if (info == null && selected.Address.Length > 0)
            {
                var __ = TestCube();
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

        private static void ShowRow(TableLayoutPanel grid, Control c, bool visible)
        {
            c.Visible = visible;
            Control caption = grid.GetControlFromPosition(0, grid.GetRow(c));
            if (caption != null)
            {
                caption.Visible = visible;
            }
        }

        // Only the fields that count for the function. For pictures the program
        // decides design, mode and change; for "usage" the cube does.
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
            SuspendLayout();
            ShowRow(connGrid, designRow, picture);
            ShowRow(connGrid, modeRow, claude);
            ShowRow(connGrid, cycleRow, picture);
            ShowRow(cubeGrid, fwDesignRow, !picture);
            ShowRow(cubeGrid, fwCycleRow, !picture);
            ShowRow(cubeGrid, fwModeRow, !picture);
            ResumeLayout();
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
            return ClaudeDesigns.All.Select(d => d.Label).ToArray();
        }

        private void ClearCubeFields()
        {
            fwDesign.Items.Clear();
            fwDesign.Items.AddRange(DesignNames(null));
            fwDesign.SelectedIndex = 0;
            fwCycle.SelectedIndex = 0;
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
            fwCycle.SelectedIndex = Math.Max(0, Array.IndexOf(CubeEntry.CycleChoices, s.CycleSeconds));
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
            bool usage = c.Function == CubeFunction.Usage;
            var s = new CubeSettings
            {
                // For picture cubes the design settings of the cube stay as they were.
                Design = usage ? fwDesign.SelectedIndex : loaded.Design,
                CycleSeconds = usage ? CubeEntry.CycleChoices[Math.Max(0, fwCycle.SelectedIndex)] : loaded.CycleSeconds,
                ShowUsed = usage ? fwUsed.Checked : loaded.ShowUsed,
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
