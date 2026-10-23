// SysopTV, Copyright (C) 2026 Christoph Scheel
// Licensed under the GNU General Public License v3.0, see LICENSE.
//
// Settings window: live status, list of cubes with their function, display
// settings of the selected cube (via /api/settings if the firmware can do it,
// otherwise the settings page in the browser), shop and program.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SysopTV.Tray
{
    internal sealed class SettingsForm : Form
    {
        private readonly AppConfig work;
        private readonly SenderService service;
        private readonly Label status = new Label { AutoSize = true, MaximumSize = new Size(560, 0), Font = new Font(SystemFonts.MessageBoxFont, FontStyle.Bold) };

        // Cubes
        private readonly ListView cubeList = new ListView { View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = false, Width = 560, Height = 96, HeaderStyle = ColumnHeaderStyle.Nonclickable };
        private readonly Button searchButton = new Button { Text = "Suchen", AutoSize = true };
        private readonly Button removeButton = new Button { Text = "Entfernen", AutoSize = true };
        private readonly Button addButton = new Button { Text = "Von Hand hinzufügen", AutoSize = true };
        private readonly Button testButton = new Button { Text = "Verbindung testen", AutoSize = true };
        private readonly ListBox found = new ListBox { Width = 420, Height = 52, Visible = false };
        private readonly Label cubeStatus = new Label { AutoSize = true, MaximumSize = new Size(440, 0), ForeColor = SystemColors.GrayText };
        private readonly TextBox address = new TextBox { Width = 200 };
        private readonly ComboBox function = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 240 };
        private readonly TextBox apiKey = new TextBox { Width = 240, UseSystemPasswordChar = true };
        private readonly TextBox adminPassword = new TextBox { Width = 240, UseSystemPasswordChar = true };

        // Display settings of the selected cube
        private readonly Panel displayPanel = new Panel { AutoSize = true };
        private readonly Panel browserPanel = new Panel { AutoSize = true };
        private readonly ComboBox design = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 140 };
        private readonly ComboBox cycle = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 140 };
        private readonly RadioButton showUsed = new RadioButton { Text = "verbraucht", AutoSize = true };
        private readonly RadioButton showLeft = new RadioButton { Text = "übrig", AutoSize = true };
        private readonly NumericUpDown brightness = new NumericUpDown { Minimum = 10, Maximum = 100, Increment = 5, Width = 60 };
        private readonly CheckBox night = new CheckBox { Text = "Nachtmodus", AutoSize = true };
        private readonly TextBox nightFrom = new TextBox { Width = 50 };
        private readonly TextBox nightTo = new TextBox { Width = 50 };
        private readonly NumericUpDown nightBrightness = new NumericUpDown { Minimum = 0, Maximum = 100, Increment = 5, Width = 60 };
        private readonly TextBox deviceName = new TextBox { Width = 160, MaxLength = 24 };
        private readonly Label displayStatus = new Label { AutoSize = true, MaximumSize = new Size(540, 0), ForeColor = SystemColors.GrayText };
        private readonly Button loadSettings = new Button { Text = "Vom Würfel laden", AutoSize = true };
        private readonly Button saveSettings = new Button { Text = "Auf dem Würfel speichern", AutoSize = true };

        // Shop
        private readonly TextBox shopDomain = new TextBox { Width = 240 };
        private readonly TextBox shopName = new TextBox { Width = 240 };
        private readonly TextBox shopToken = new TextBox { Width = 240, UseSystemPasswordChar = true };
        private readonly TextBox shopClientId = new TextBox { Width = 240 };
        private readonly TextBox shopSecret = new TextBox { Width = 240, UseSystemPasswordChar = true };
        private readonly Button shopTest = new Button { Text = "Shop testen", AutoSize = true };
        private readonly Label shopStatus = new Label { AutoSize = true, MaximumSize = new Size(420, 0), ForeColor = SystemColors.GrayText };

        // Program
        private readonly NumericUpDown interval = new NumericUpDown { Minimum = AppConfig.MinInterval, Maximum = 3600, Width = 80 };
        private readonly CheckBox autostart = new CheckBox { Text = "Mit Windows starten", AutoSize = true };

        private CubeEntry selected;
        private readonly Dictionary<string, CubeInfo> infos = new Dictionary<string, CubeInfo>();
        private CancellationTokenSource searchCancel;

        internal event Action<AppConfig> Saved;

        internal SettingsForm(AppConfig config, SenderService service)
        {
            work = config.Clone();
            this.service = service;

            Text = "SysopTV " + Program.Version + ": Einstellungen";
            Icon = TrayIcons.AppIcon();
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.Font;
            Font = SystemFonts.MessageBoxFont;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Padding = new Padding(10);

            cubeList.Columns.Add("Name", 150);
            cubeList.Columns.Add("Adresse", 140);
            cubeList.Columns.Add("Funktion", 240);
            cycle.Items.AddRange(new object[] { "aus", "alle 30 Sekunden", "alle 60 Sekunden" });

            shopDomain.Text = work.Shop.Domain;
            shopName.Text = work.Shop.DisplayName;
            shopToken.Text = work.Shop.AccessToken;
            shopClientId.Text = work.Shop.ClientId;
            shopSecret.Text = work.Shop.ClientSecret;
            interval.Value = work.Interval;
            try
            {
                autostart.Checked = Autostart.IsEnabled();
            }
            catch (Exception)
            {
                autostart.Checked = false;
            }

            var main = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, Dock = DockStyle.Fill };
            main.Controls.Add(status);
            main.Controls.Add(CubeGroup());
            main.Controls.Add(DisplayGroup());
            main.Controls.Add(ShopGroup());
            main.Controls.Add(ProgramGroup());
            main.Controls.Add(ButtonRow());
            Controls.Add(main);

            FillCubeList();
            ShowStatus();
            service.StatusChanged += OnStatusChanged;
            FormClosed += (s, e) =>
            {
                service.StatusChanged -= OnStatusChanged;
                if (searchCancel != null)
                {
                    searchCancel.Cancel();
                }
            };

            cubeList.SelectedIndexChanged += (s, e) => SelectCube();
            searchButton.Click += async (s, e) => await Search();
            testButton.Click += async (s, e) => await TestCube();
            addButton.Click += (s, e) => AddCube(new CubeEntry { Address = "" }, true);
            removeButton.Click += (s, e) => RemoveCube();
            found.SelectedIndexChanged += (s, e) =>
            {
                var info = found.SelectedItem as CubeInfo;
                if (info != null)
                {
                    AddFound(info);
                }
            };
            address.Leave += (s, e) => { CommitCube(); RefreshRow(); };
            function.SelectedIndexChanged += (s, e) => { CommitCube(); RefreshRow(); };
            loadSettings.Click += async (s, e) => await LoadDisplaySettings();
            saveSettings.Click += async (s, e) => await SaveDisplaySettings();
            shopTest.Click += async (s, e) => await TestShop();
            if (cubeList.Items.Count > 0)
            {
                cubeList.Items[0].Selected = true;
            }
        }

        // ------------------------------------------------------------------
        // Layout
        // ------------------------------------------------------------------

        private static GroupBox Group(string title, Control content)
        {
            var g = new GroupBox { Text = title, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(8), Margin = new Padding(0, 6, 0, 0), Dock = DockStyle.Fill };
            content.Dock = DockStyle.Fill;
            g.Controls.Add(content);
            return g;
        }

        private static TableLayoutPanel Grid()
        {
            var t = new TableLayoutPanel { ColumnCount = 2, AutoSize = true };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            return t;
        }

        private static Label Caption(string text)
        {
            return new Label { Text = text, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 8, 0) };
        }

        private static FlowLayoutPanel Row(params Control[] controls)
        {
            var row = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0), WrapContents = false };
            row.Controls.AddRange(controls);
            return row;
        }

        private GroupBox CubeGroup()
        {
            var t = new TableLayoutPanel { ColumnCount = 1, AutoSize = true };
            t.Controls.Add(cubeList);
            t.Controls.Add(Row(searchButton, addButton, removeButton, testButton));
            t.Controls.Add(found);
            t.Controls.Add(cubeStatus);
            TableLayoutPanel g = Grid();
            g.Controls.Add(Caption("Adresse (IP oder Name)"), 0, 0);
            g.Controls.Add(address, 1, 0);
            g.Controls.Add(Caption("Funktion"), 0, 1);
            g.Controls.Add(function, 1, 1);
            g.Controls.Add(Caption("API-Schlüssel (optional)"), 0, 2);
            g.Controls.Add(apiKey, 1, 2);
            g.Controls.Add(Caption("Admin-Passwort (optional)"), 0, 3);
            g.Controls.Add(adminPassword, 1, 3);
            t.Controls.Add(g);
            return Group("Würfel", t);
        }

        private GroupBox DisplayGroup()
        {
            TableLayoutPanel g = Grid();
            g.Controls.Add(Caption("Design"), 0, 0);
            g.Controls.Add(Row(design, Caption("Automatisch wechseln"), cycle), 1, 0);
            g.Controls.Add(Caption("Prozent zeigen"), 0, 1);
            g.Controls.Add(Row(showUsed, showLeft), 1, 1);
            g.Controls.Add(Caption("Helligkeit"), 0, 2);
            g.Controls.Add(Row(brightness, night, Caption("von"), nightFrom, Caption("bis"), nightTo, Caption("nachts"), nightBrightness), 1, 2);
            g.Controls.Add(Caption("Gerätename"), 0, 3);
            g.Controls.Add(deviceName, 1, 3);
            g.Controls.Add(Row(loadSettings, saveSettings), 1, 4);
            g.Controls.Add(displayStatus, 1, 5);
            displayPanel.Controls.Add(g);
            g.Dock = DockStyle.Fill;

            var info = new Label { Text = "Dieser Würfel hat noch keine Schnittstelle für Einstellungen (ab Firmware 0.4.0). Die Seite im Browser kann alles einstellen.", AutoSize = true, MaximumSize = new Size(540, 0) };
            var open = new Button { Text = "Einstellungen am Würfel öffnen", AutoSize = true, Top = 36 };
            open.Click += (s, e) => OpenBrowser("settings");
            browserPanel.Controls.Add(info);
            browserPanel.Controls.Add(open);
            browserPanel.Visible = false;

            var t = new TableLayoutPanel { ColumnCount = 1, AutoSize = true };
            t.Controls.Add(displayPanel);
            t.Controls.Add(browserPanel);
            return Group("Anzeige auf dem Würfel", t);
        }

        private GroupBox ShopGroup()
        {
            TableLayoutPanel g = Grid();
            g.Controls.Add(Caption("Shop-Adresse"), 0, 0);
            g.Controls.Add(Row(shopDomain, new Label { Text = "z. B. meinshop.myshopify.com", AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(6, 6, 0, 0) }), 1, 0);
            g.Controls.Add(Caption("Name für die Anzeige"), 0, 1);
            g.Controls.Add(shopName, 1, 1);
            g.Controls.Add(Caption("Admin-API-Zugriffstoken"), 0, 2);
            g.Controls.Add(shopToken, 1, 2);
            g.Controls.Add(Caption("oder Client-ID"), 0, 3);
            g.Controls.Add(shopClientId, 1, 3);
            g.Controls.Add(Caption("und Client-Secret"), 0, 4);
            g.Controls.Add(shopSecret, 1, 4);
            g.Controls.Add(Row(shopTest, shopStatus), 1, 5);
            return Group("Shop (Shopify)", g);
        }

        private GroupBox ProgramGroup()
        {
            TableLayoutPanel t = Grid();
            t.Controls.Add(Caption("Claude abfragen alle"), 0, 0);
            t.Controls.Add(Row(interval, new Label { Text = "Sekunden, mindestens 120, sonst sperrt der Server (Fehler 429)", AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(6, 6, 0, 0) }), 1, 0);
            var logButton = new Button { Text = "Log-Ordner öffnen", AutoSize = true };
            logButton.Click += (s, e) =>
            {
                try
                {
                    Directory.CreateDirectory(AppPaths.LogDir);
                    Process.Start(AppPaths.LogDir);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Der Ordner ließ sich nicht öffnen: " + ex.Message, "SysopTV");
                }
            };
            t.Controls.Add(Row(autostart, logButton), 1, 1);
            return Group("Programm", t);
        }

        private Control ButtonRow()
        {
            var row = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 8, 0, 0) };
            var cancel = new Button { Text = "Abbrechen", DialogResult = DialogResult.Cancel, AutoSize = true };
            var save = new Button { Text = "Speichern", AutoSize = true };
            cancel.Click += (s, e) => Close();
            save.Click += (s, e) => SaveAndClose();
            row.Controls.Add(cancel);
            row.Controls.Add(save);
            CancelButton = cancel;
            return row;
        }

        // ------------------------------------------------------------------
        // Status line
        // ------------------------------------------------------------------

        private void OnStatusChanged()
        {
            if (IsHandleCreated && !IsDisposed)
            {
                BeginInvoke((Action)ShowStatus);
            }
        }

        private void ShowStatus()
        {
            SenderStatus st = service.Status;
            status.Text = st.Line;
            status.ForeColor = st.Kind == StatusKind.Error ? Color.Firebrick
                             : st.Kind == StatusKind.Warn ? Color.DarkOrange
                             : st.Kind == StatusKind.Ok ? Color.ForestGreen : SystemColors.GrayText;
        }

        // ------------------------------------------------------------------
        // Cubes
        // ------------------------------------------------------------------

        private void FillCubeList()
        {
            cubeList.Items.Clear();
            foreach (CubeEntry c in work.Cubes)
            {
                cubeList.Items.Add(new ListViewItem(new[] { c.Name, c.Address, CubeFunction.Label(c.Function) }) { Tag = c });
            }
        }

        private void RefreshRow()
        {
            foreach (ListViewItem item in cubeList.Items)
            {
                var c = (CubeEntry)item.Tag;
                item.SubItems[0].Text = c.Name;
                item.SubItems[1].Text = c.Address;
                item.SubItems[2].Text = CubeFunction.Label(c.Function);
            }
        }

        // Writes the fields of the selected cube back into the working copy.
        private void CommitCube()
        {
            if (selected == null)
            {
                return;
            }
            selected.Address = address.Text.Trim();
            var f = function.SelectedItem as FunctionItem;
            if (f != null)
            {
                selected.Function = f.Value;
            }
            selected.ApiKey = apiKey.Text.Trim();
            selected.AdminPassword = adminPassword.Text;
        }

        private sealed class FunctionItem
        {
            internal string Value;
            public override string ToString() { return CubeFunction.Label(Value); }
        }

        private void SelectCube()
        {
            CommitCube();
            selected = cubeList.SelectedItems.Count > 0 ? (CubeEntry)cubeList.SelectedItems[0].Tag : null;
            bool on = selected != null;
            address.Enabled = function.Enabled = apiKey.Enabled = adminPassword.Enabled = removeButton.Enabled = testButton.Enabled = on;
            if (!on)
            {
                return;
            }
            address.Text = selected.Address;
            apiKey.Text = selected.ApiKey;
            adminPassword.Text = selected.AdminPassword;
            CubeInfo info;
            infos.TryGetValue(selected.Address, out info);
            FillFunctions(info);
            ShowDisplayArea(info);
            if (info == null && selected.Address.Length > 0)
            {
                var _ = TestCube();
            }
        }

        // Picture functions only for cubes that report "frame".
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
                function.Items.Add(new FunctionItem { Value = v });
            }
            function.SelectedIndex = Math.Max(0, values.IndexOf(selected.Function));
        }

        private void ShowDisplayArea(CubeInfo info)
        {
            bool api = info != null && info.Can("settings");
            displayPanel.Visible = api;
            browserPanel.Visible = !api;
            displayStatus.Text = api ? "Mit „Vom Würfel laden“ die aktuellen Werte holen." : "";
        }

        private void AddCube(CubeEntry entry, bool focusAddress)
        {
            CommitCube();
            work.Cubes.Add(entry);
            FillCubeList();
            cubeList.Items[cubeList.Items.Count - 1].Selected = true;
            if (focusAddress)
            {
                address.Focus();
            }
        }

        private void AddFound(CubeInfo info)
        {
            infos[info.Ip] = info;
            CubeEntry existing = work.Cubes.FirstOrDefault(c => c.Address == info.Ip);
            if (existing != null)
            {
                existing.Name = info.DeviceName;
                RefreshRow();
                cubeStatus.Text = "Der Würfel " + info.Ip + " ist schon in der Liste.";
                return;
            }
            AddCube(new CubeEntry { Address = info.Ip, Name = info.DeviceName, Function = CubeFunction.Usage }, false);
            cubeStatus.Text = "Hinzugefügt: " + info;
        }

        private void RemoveCube()
        {
            if (selected == null)
            {
                return;
            }
            work.Cubes.Remove(selected);
            selected = null;
            FillCubeList();
            if (cubeList.Items.Count > 0)
            {
                cubeList.Items[0].Selected = true;
            }
        }

        private async Task Search()
        {
            searchButton.Enabled = false;
            found.Items.Clear();
            found.Visible = false;
            searchCancel = new CancellationTokenSource();
            var progress = new Progress<string>(text => cubeStatus.Text = text);
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
                cubeStatus.Text = result.Count == 0
                    ? "Kein Würfel gefunden. Ist er eingeschaltet und im selben Netz?"
                    : result.Count + " Würfel gefunden, zum Hinzufügen anklicken.";
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
            CommitCube();
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
            if (info == null)
            {
                cubeStatus.Text = "Unter " + c.Address + " antwortet kein SysopTV.";
                return;
            }
            infos[c.Address] = info;
            if (!string.IsNullOrEmpty(info.DeviceName))
            {
                c.Name = info.DeviceName;
                RefreshRow();
            }
            cubeStatus.Text = "Verbunden: SysopTV " + info.Version + ", Laufzeit " + info.UptimeText + (info.Rescue ? ", Rettungsmodus" : "");
            if (c == selected)
            {
                FillFunctions(info);
                ShowDisplayArea(info);
            }
        }

        private void OpenBrowser(string path)
        {
            CommitCube();
            string addr = selected != null && selected.Address.Length > 0 ? selected.Address : "sysoptv.local";
            try
            {
                Process.Start(Net.BaseUrl(addr) + path);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Der Browser ließ sich nicht öffnen: " + ex.Message, "SysopTV");
            }
        }

        // ------------------------------------------------------------------
        // Display settings over /api/settings
        // ------------------------------------------------------------------

        private async Task LoadDisplaySettings()
        {
            CommitCube();
            CubeEntry c = selected;
            if (c == null)
            {
                return;
            }
            displayStatus.Text = "Lade ...";
            try
            {
                CubeSettings s = await Task.Run(() => Cube.GetSettings(c.Address, c.AdminPassword));
                design.Items.Clear();
                foreach (string n in s.DesignNames)
                {
                    design.Items.Add(n);
                }
                design.SelectedIndex = Math.Min(Math.Max(0, s.Design), design.Items.Count - 1);
                cycle.SelectedIndex = s.CycleSeconds == 60 ? 2 : s.CycleSeconds == 30 ? 1 : 0;
                showUsed.Checked = s.ShowUsed;
                showLeft.Checked = !s.ShowUsed;
                brightness.Value = Math.Min(100, Math.Max(10, s.Brightness));
                night.Checked = s.NightEnabled;
                nightFrom.Text = s.NightFrom;
                nightTo.Text = s.NightTo;
                nightBrightness.Value = Math.Min(100, Math.Max(0, s.NightBrightness));
                deviceName.Text = s.DeviceName;
                displayStatus.Text = "Geladen.";
            }
            catch (InvalidOperationException ex)
            {
                displayStatus.Text = ex.Message;
            }
        }

        private async Task SaveDisplaySettings()
        {
            CommitCube();
            CubeEntry c = selected;
            if (c == null || design.Items.Count == 0)
            {
                displayStatus.Text = "Bitte zuerst vom Würfel laden.";
                return;
            }
            var s = new CubeSettings
            {
                Design = design.SelectedIndex,
                CycleSeconds = cycle.SelectedIndex == 2 ? 60 : cycle.SelectedIndex == 1 ? 30 : 0,
                ShowUsed = showUsed.Checked,
                Brightness = (int)brightness.Value,
                NightEnabled = night.Checked,
                NightFrom = nightFrom.Text.Trim(),
                NightTo = nightTo.Text.Trim(),
                NightBrightness = (int)nightBrightness.Value,
                DeviceName = deviceName.Text.Trim()
            };
            displayStatus.Text = "Speichere ...";
            try
            {
                CubeSettings saved = await Task.Run(() => Cube.SaveSettings(c.Address, c.AdminPassword, s));
                c.Name = saved.DeviceName;
                RefreshRow();
                displayStatus.Text = "Auf dem Würfel gespeichert.";
            }
            catch (InvalidOperationException ex)
            {
                displayStatus.Text = ex.Message;
            }
        }

        // ------------------------------------------------------------------
        // Shop
        // ------------------------------------------------------------------

        private ShopConfig ShopFromFields()
        {
            ShopConfig s = work.Shop.Copy();
            s.Domain = ShopifyClient.NormalizeDomain(shopDomain.Text);
            s.DisplayName = shopName.Text.Trim();
            s.AccessToken = shopToken.Text.Trim();
            s.ClientId = shopClientId.Text.Trim();
            s.ClientSecret = shopSecret.Text.Trim();
            return s;
        }

        private async Task TestShop()
        {
            ShopConfig cfg = ShopFromFields();
            if (!cfg.Configured)
            {
                shopStatus.Text = "Bitte Shop-Adresse und Token oder Client-ID mit Secret eintragen.";
                return;
            }
            shopTest.Enabled = false;
            shopStatus.Text = "Frage Shopify ab ...";
            ShopData d = await Task.Run(() => new ShopifyClient().Fetch(cfg));
            if (IsDisposed)
            {
                return;
            }
            shopTest.Enabled = true;
            shopStatus.Text = d.Error != null
                ? "Fehler: " + d.Error
                : "Heute " + d.Today.Orders + " Bestellungen, " + ShopRenderer.Money(d.Today.Revenue) + ", offen " + d.Unshipped;
        }

        // ------------------------------------------------------------------
        // Save
        // ------------------------------------------------------------------

        private void SaveAndClose()
        {
            CommitCube();
            if (work.Cubes.Any(c => c.Address.Length == 0))
            {
                MessageBox.Show(this, "Jeder Würfel braucht eine Adresse.", "SysopTV");
                return;
            }
            work.IntervalSeconds = (int)interval.Value;
            try
            {
                work.Shop = ShopFromFields();
                work.Save();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Die Einstellungen ließen sich nicht speichern: " + ex.Message, "SysopTV");
                return;
            }
            try
            {
                Autostart.Set(autostart.Checked);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Der Autostart ließ sich nicht ändern: " + ex.Message, "SysopTV");
            }
            Action<AppConfig> handler = Saved;
            if (handler != null)
            {
                handler(work.Clone());
            }
            Close();
        }
    }
}
