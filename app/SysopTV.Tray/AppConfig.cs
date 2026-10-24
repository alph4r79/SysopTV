// SysopTV, Copyright (C) 2026 Christoph Scheel
// Licensed under the GNU General Public License v3.0, see LICENSE.
//
// Settings in %APPDATA%\SysopTV\config.json. Secrets (API keys, admin
// passwords, Shopify token and secret) are stored only encrypted with DPAPI
// for the current Windows user, never as plain text.
//
// Each cube has one of three functions: "claude" (Claude Limits), "shopify"
// or "woocommerce". Older files are taken over without losing anything:
// - version 0.1.0 had one cube (deviceAddress, apiKeyProtected), it becomes
//   a cube with "claude";
// - "usage" (the cube drew itself) becomes "claude"; design, used or
//   remaining and the change are taken from the cube on the first contact
//   (adoptCubeDesign), until then DOS;
// - "shop" becomes "shopify" or "woocommerce" after its shopSource;
// - shop cubes of versions before 0.5.0 keep their design under "Details".
//
// A cube can show a second function in turn with the first ("second",
// "alternate", "alternateSeconds"). The second function is stored as a
// CubeEntry of its own, only its view fields count.
//
// Before every save the old config.json is copied to config.bak.json. If
// config.json cannot be read at the start, the program takes the backup; if
// that is broken as well, it starts with defaults and keeps the broken file
// as config.broken.json.

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;

namespace SysopTV.Tray
{
    internal static class Secret
    {
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("SysopTV.Tray");

        internal static string Protect(string plain)
        {
            if (string.IsNullOrEmpty(plain))
            {
                return "";
            }
            return Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), Entropy, DataProtectionScope.CurrentUser));
        }

        internal static string Unprotect(string cipher)
        {
            if (string.IsNullOrEmpty(cipher))
            {
                return "";
            }
            try
            {
                return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(cipher), Entropy, DataProtectionScope.CurrentUser));
            }
            catch (CryptographicException)
            {
                return "";
            }
            catch (FormatException)
            {
                return "";
            }
        }
    }

    internal static class CubeFunction
    {
        internal const string Claude = "claude";
        internal const string Shopify = ShopSources.Shopify;
        internal const string WooCommerce = ShopSources.WooCommerce;

        // Only read from older files.
        internal const string LegacyUsage = "usage";
        internal const string LegacyShop = "shop";

        internal static readonly DesignInfo[] All =
        {
            new DesignInfo(Claude, "Claude Limits"),
            new DesignInfo(Shopify, "Shopify"),
            new DesignInfo(WooCommerce, "WooCommerce")
        };

        internal static string Label(string f)
        {
            DesignInfo d = Array.Find(All, x => x.Id == f);
            return d != null ? d.Label : All[0].Label;
        }

        internal static bool IsShop(string f)
        {
            return f == Shopify || f == WooCommerce;
        }
    }

    [DataContract]
    internal sealed class CubeEntry
    {
        [DataMember(Name = "address")] public string Address = "";
        [DataMember(Name = "name")] public string Name = "";
        [DataMember(Name = "function")] public string Function = CubeFunction.Claude;
        [DataMember(Name = "apiKeyProtected")] public string ApiKeyProtected = "";
        [DataMember(Name = "adminPasswordProtected")] public string AdminPasswordProtected = "";

        // The picture, chosen in the program. Older files have none of these,
        // OnDeserializing sets the defaults.
        [DataMember(Name = "design")] public string Design = "";
        [DataMember(Name = "showUsed")] public bool ShowUsed = true;
        [DataMember(Name = "cycleSeconds")] public int CycleSeconds;

        // Shop: view "big" (Großanzeige) or "details" (the four detail designs).
        [DataMember(Name = "shopView")] public string ShopView = ShopBig.ViewBig;
        [DataMember(Name = "bigPeriod")] public string BigPeriod = ShopBig.DefaultPeriod;
        [DataMember(Name = "bigContent")] public string BigContent = ShopBig.DefaultContent;
        [DataMember(Name = "bigStyle")] public string BigStyle = ShopBig.DefaultStyle;
        [DataMember(Name = "periodCycleSeconds")] public int PeriodCycleSeconds;

        // Flip animation of the style Fallblatt, for Claude and the shop.
        [DataMember(Name = "animate")] public bool Animate = true;

        // Up to 0.6.0 the source of a "shop" cube, only read.
        [DataMember(Name = "shopSource", EmitDefaultValue = false)] public string LegacyShopSource;

        // Taken over from "usage": design, used or remaining and change come
        // from /api/info of the cube on the first contact.
        [DataMember(Name = "adoptCubeDesign", EmitDefaultValue = false)] public bool AdoptCubeDesign;

        // "Im Wechsel zeigen": a second function with its own view, in turn
        // with the first every AlternateSeconds.
        [DataMember(Name = "alternate")] public bool Alternate;
        [DataMember(Name = "alternateSeconds")] public int AlternateSeconds = DefaultAlternateSeconds;
        [DataMember(Name = "second", EmitDefaultValue = false)] public CubeEntry Second;

        internal static readonly int[] CycleChoices = { 0, 30, 60 };
        internal const int DefaultAlternateSeconds = 20;
        internal const int MinAlternateSeconds = 10;
        internal const int MaxAlternateSeconds = 120;

        internal string ApiKey
        {
            get { return Secret.Unprotect(ApiKeyProtected); }
            set { ApiKeyProtected = Secret.Protect(value); }
        }

        internal string AdminPassword
        {
            get { return Secret.Unprotect(AdminPasswordProtected); }
            set { AdminPasswordProtected = Secret.Protect(value); }
        }

        internal string Title
        {
            get { return string.IsNullOrEmpty(Name) ? Address : Name + " (" + Address + ")"; }
        }

        [OnDeserializing]
        private void OnDeserializing(StreamingContext context)
        {
            Design = "";
            ShowUsed = true;
            CycleSeconds = 0;
            ShopView = "";
            BigPeriod = ShopBig.DefaultPeriod;
            BigContent = ShopBig.DefaultContent;
            BigStyle = ShopBig.DefaultStyle;
            PeriodCycleSeconds = 0;
            Animate = true;
            LegacyShopSource = null;
            AdoptCubeDesign = false;
            Alternate = false;
            AlternateSeconds = DefaultAlternateSeconds;
            Second = null;
        }

        // Where the shop numbers come from, given by the function.
        internal string ShopSource
        {
            get { return Function == CubeFunction.WooCommerce ? ShopSources.WooCommerce : ShopSources.Shopify; }
        }

        internal bool IsShop
        {
            get { return CubeFunction.IsShop(Function); }
        }

        // What the picture shows now: the style of the big display, or the design.
        internal string PictureAt(DateTime now)
        {
            return ShopBigView ? (ShopBig.Known(ShopBig.Styles, BigStyle) ? BigStyle : ShopBig.DefaultStyle) : DesignAt(now);
        }

        // Name of a picture for overview and the cube: "Großanzeige Fallblatt", "ANSI".
        internal string PictureLabel(string picture)
        {
            if (ShopBigView)
            {
                return "Großanzeige " + ShopBig.Label(ShopBig.Styles, picture);
            }
            DesignInfo d = Array.Find(Designs ?? new DesignInfo[0], x => x.Id == picture);
            return d != null ? d.Label : picture;
        }

        internal bool ShopBigView
        {
            get { return IsShop && ShopView == ShopBig.ViewBig; }
        }

        // The period to show now: with change Heute, Woche, Monat, Jahr one after the
        // other, starting with the chosen one.
        internal string PeriodAt(DateTime now)
        {
            string chosen = ShopBig.Known(ShopBig.Periods, BigPeriod) ? BigPeriod : ShopBig.DefaultPeriod;
            if (PeriodCycleSeconds <= 0)
            {
                return chosen;
            }
            int start = Array.FindIndex(ShopBig.Periods, d => d.Id == chosen);
            long step = new DateTimeOffset(now).ToUnixTimeSeconds() / PeriodCycleSeconds;
            return ShopBig.Periods[(int)((start + step) % ShopBig.Periods.Length)].Id;
        }

        // The designs to choose from for the function of this cube.
        internal DesignInfo[] Designs
        {
            get { return IsShop ? ShopDesigns.All : ClaudeDesigns.All; }
        }

        // The chosen design, or the default if it does not fit the function.
        internal string DesignOrDefault
        {
            get
            {
                if (IsShop)
                {
                    return ShopDesigns.Known(Design) ? Design : ShopDesigns.Default;
                }
                return ClaudeDesigns.Known(Design) ? Design : ClaudeDesigns.Default;
            }
        }

        // The design to show now: with automatic change one after the other,
        // starting with the chosen one, the same on every PC clock tick.
        internal string DesignAt(DateTime now)
        {
            DesignInfo[] list = Designs;
            string chosen = DesignOrDefault;
            if (CycleSeconds <= 0)
            {
                return chosen;
            }
            int start = Array.FindIndex(list, d => d.Id == chosen);
            long step = new DateTimeOffset(now).ToUnixTimeSeconds() / CycleSeconds;
            return list[(int)((start + step) % list.Length)].Id;
        }

        [OnDeserialized]
        private void OnDeserialized(StreamingContext context)
        {
            Design = Design ?? "";
            // No view stored: a file from before 0.5.0, the shop design goes on as a detail design.
            if (ShopView != ShopBig.ViewBig && ShopView != ShopBig.ViewDetails)
            {
                ShopView = ShopBig.ViewDetails;
            }
            if (!ShopBig.Known(ShopBig.Periods, BigPeriod))
            {
                BigPeriod = ShopBig.DefaultPeriod;
            }
            if (!ShopBig.Known(ShopBig.Contents, BigContent))
            {
                BigContent = ShopBig.DefaultContent;
            }
            if (!ShopBig.Known(ShopBig.Styles, BigStyle))
            {
                BigStyle = ShopBig.DefaultStyle;
            }
            if (Array.IndexOf(CycleChoices, PeriodCycleSeconds) < 0)
            {
                PeriodCycleSeconds = 0;
            }
            if (Array.IndexOf(CycleChoices, CycleSeconds) < 0)
            {
                CycleSeconds = 0;
            }
            Address = (Address ?? "").Trim();
            Name = Name ?? "";
            switch (Function)
            {
                case CubeFunction.Claude:
                case CubeFunction.Shopify:
                case CubeFunction.WooCommerce:
                    break;
                case CubeFunction.LegacyShop:
                    Function = LegacyShopSource == ShopSources.WooCommerce ? CubeFunction.WooCommerce : CubeFunction.Shopify;
                    break;
                default:
                    // "usage" or nothing: Claude Limits with the design of the cube, DOS until it is known.
                    Function = CubeFunction.Claude;
                    Design = ClaudeDesigns.Default;
                    AdoptCubeDesign = true;
                    break;
            }
            LegacyShopSource = null;
            ApiKeyProtected = ApiKeyProtected ?? "";
            AdminPasswordProtected = AdminPasswordProtected ?? "";
            if (AlternateSeconds < MinAlternateSeconds || AlternateSeconds > MaxAlternateSeconds)
            {
                AlternateSeconds = AlternateSeconds <= 0 ? DefaultAlternateSeconds : Math.Max(MinAlternateSeconds, Math.Min(MaxAlternateSeconds, AlternateSeconds));
            }
            if (Second != null)
            {
                Second.Second = null;
                Second.Alternate = false;
                if (Second.Function == Function)
                {
                    Second.Function = OtherFunction(Function);
                    Second.Design = "";
                }
            }
            else if (Alternate)
            {
                Alternate = false;
            }
        }

        // The first function that is not f, for a new second function.
        internal static string OtherFunction(string f)
        {
            DesignInfo d = Array.Find(CubeFunction.All, x => x.Id != f);
            return d.Id;
        }

        // Makes sure there is a second function that differs from the first.
        internal CubeEntry EnsureSecond()
        {
            if (Second == null)
            {
                Second = new CubeEntry { Function = OtherFunction(Function) };
            }
            else if (Second.Function == Function)
            {
                Second.Function = OtherFunction(Function);
            }
            return Second;
        }

        internal bool Alternates
        {
            get { return Alternate && Second != null && Second.Function != Function; }
        }

        // The functions shown, one or two.
        internal IEnumerable<CubeEntry> Views
        {
            get
            {
                yield return this;
                if (Alternates)
                {
                    yield return Second;
                }
            }
        }

        // Which view to show now. In turn every AlternateSeconds after the PC
        // clock; if one of the two has no data, only the other one, until data
        // comes back.
        internal CubeEntry ViewAt(DateTime now, Func<CubeEntry, bool> hasData)
        {
            if (!Alternates)
            {
                return this;
            }
            bool first = hasData(this);
            bool second = hasData(Second);
            if (first != second)
            {
                return first ? this : Second;
            }
            long slot = new DateTimeOffset(now).ToUnixTimeSeconds() / Math.Max(MinAlternateSeconds, AlternateSeconds);
            return slot % 2 == 0 ? this : Second;
        }

        internal CubeEntry Copy()
        {
            var c = (CubeEntry)MemberwiseClone();
            if (Second != null)
            {
                c.Second = Second.Copy();
            }
            return c;
        }
    }

    // The shop sources, the same ids as the shop functions.
    internal static class ShopSources
    {
        internal const string Shopify = "shopify";
        internal const string WooCommerce = "woocommerce";
        internal static readonly DesignInfo[] All =
        {
            new DesignInfo(Shopify, "Shopify"),
            new DesignInfo(WooCommerce, "WooCommerce")
        };
    }

    // Access to a WooCommerce shop with the plugin "SysopTV for WooCommerce".
    [DataContract]
    internal sealed class WooConfig
    {
        [DataMember(Name = "url")] public string Url = "";
        [DataMember(Name = "displayName")] public string DisplayName = "";
        [DataMember(Name = "keyProtected")] public string KeyProtected = "";

        internal string Key
        {
            get { return Secret.Unprotect(KeyProtected); }
            set { KeyProtected = Secret.Protect(value); }
        }

        internal bool Configured
        {
            get { return !string.IsNullOrWhiteSpace(Url) && !string.IsNullOrEmpty(KeyProtected); }
        }

        [OnDeserialized]
        private void OnDeserialized(StreamingContext context)
        {
            Url = Url ?? "";
            DisplayName = DisplayName ?? "";
            KeyProtected = KeyProtected ?? "";
        }

        internal WooConfig Copy()
        {
            return (WooConfig)MemberwiseClone();
        }
    }

    [DataContract]
    internal sealed class ShopConfig
    {
        [DataMember(Name = "domain")] public string Domain = "";
        [DataMember(Name = "displayName")] public string DisplayName = "";
        [DataMember(Name = "accessTokenProtected")] public string AccessTokenProtected = "";
        [DataMember(Name = "clientId")] public string ClientId = "";
        [DataMember(Name = "clientSecretProtected")] public string ClientSecretProtected = "";

        internal string AccessToken
        {
            get { return Secret.Unprotect(AccessTokenProtected); }
            set { AccessTokenProtected = Secret.Protect(value); }
        }

        internal string ClientSecret
        {
            get { return Secret.Unprotect(ClientSecretProtected); }
            set { ClientSecretProtected = Secret.Protect(value); }
        }

        internal bool Configured
        {
            get
            {
                return !string.IsNullOrWhiteSpace(Domain) &&
                       (!string.IsNullOrEmpty(AccessTokenProtected) ||
                        (!string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrEmpty(ClientSecretProtected)));
            }
        }

        [OnDeserialized]
        private void OnDeserialized(StreamingContext context)
        {
            Domain = Domain ?? "";
            DisplayName = DisplayName ?? "";
            AccessTokenProtected = AccessTokenProtected ?? "";
            ClientId = ClientId ?? "";
            ClientSecretProtected = ClientSecretProtected ?? "";
        }

        internal ShopConfig Copy()
        {
            return (ShopConfig)MemberwiseClone();
        }
    }

    // "Benachrichtigen, wenn das Limit knapp wird": one Windows message per
    // threshold and period (session until its reset, week until its reset).
    [DataContract]
    internal sealed class NotifyConfig
    {
        internal static readonly int[] Thresholds = { 70, 80, 90, 95 };
        internal const int DefaultThreshold = 90;

        [DataMember(Name = "enabled")] public bool Enabled;
        [DataMember(Name = "session")] public int Session = DefaultThreshold;  // percent used, 0 is off
        [DataMember(Name = "week")] public int Week = DefaultThreshold;

        [OnDeserializing]
        private void OnDeserializing(StreamingContext context)
        {
            Enabled = false;
            Session = DefaultThreshold;
            Week = DefaultThreshold;
        }

        [OnDeserialized]
        private void OnDeserialized(StreamingContext context)
        {
            Session = Clean(Session);
            Week = Clean(Week);
        }

        private static int Clean(int v)
        {
            return v == 0 || Array.IndexOf(Thresholds, v) >= 0 ? v : DefaultThreshold;
        }

        internal NotifyConfig Copy()
        {
            return (NotifyConfig)MemberwiseClone();
        }
    }

    [DataContract]
    internal sealed class AppConfig
    {
        internal const int MinInterval = 120;

        [DataMember(Name = "cubes")] public List<CubeEntry> Cubes;
        [DataMember(Name = "intervalSeconds")] public int IntervalSeconds;
        [DataMember(Name = "shop")] public ShopConfig Shop;
        [DataMember(Name = "woocommerce")] public WooConfig Woo;
        [DataMember(Name = "notify")] public NotifyConfig Notify;
        // "Einmal täglich nach neuen Versionen suchen", off by default.
        [DataMember(Name = "updateCheck")] public bool UpdateCheck;

        // Set by Load when config.json was broken and the backup was taken.
        internal bool RestoredFromBackup;

        // Only read, from version 0.1.0.
        [DataMember(Name = "deviceAddress", EmitDefaultValue = false)] public string LegacyAddress;
        [DataMember(Name = "apiKeyProtected", EmitDefaultValue = false)] public string LegacyApiKey;

        internal AppConfig()
        {
            SetDefaults();
        }

        [OnDeserializing]
        private void OnDeserializing(StreamingContext context)
        {
            SetDefaults();
        }

        private void SetDefaults()
        {
            Cubes = new List<CubeEntry>();
            IntervalSeconds = MinInterval;
            Shop = new ShopConfig();
            Woo = new WooConfig();
            Notify = new NotifyConfig();
            UpdateCheck = false;
            LegacyAddress = null;
            LegacyApiKey = null;
        }

        internal bool SourceConfigured(string source)
        {
            return source == ShopSources.WooCommerce ? Woo.Configured : Shop.Configured;
        }

        internal string SourceName(string source)
        {
            return source == ShopSources.WooCommerce ? Woo.DisplayName : Shop.DisplayName;
        }

        internal int Interval
        {
            get { return Math.Max(MinInterval, IntervalSeconds); }
        }

        internal AppConfig Clone()
        {
            var c = new AppConfig { IntervalSeconds = IntervalSeconds, Shop = Shop.Copy(), Woo = Woo.Copy(), Notify = Notify.Copy(), UpdateCheck = UpdateCheck };
            foreach (CubeEntry e in Cubes)
            {
                c.Cubes.Add(e.Copy());
            }
            return c;
        }

        internal static AppConfig Load()
        {
            return Load(AppPaths.ConfigFile);
        }

        internal static string BackupPath(string path)
        {
            return Path.Combine(Path.GetDirectoryName(path), "config.bak.json");
        }

        internal static string BrokenPath(string path)
        {
            return Path.Combine(Path.GetDirectoryName(path), "config.broken.json");
        }

        // Reads one file. Null if it is missing, empty or not valid.
        private static AppConfig Read(string path, out string error)
        {
            error = null;
            try
            {
                if (!File.Exists(path))
                {
                    return null;
                }
                using (FileStream fs = File.OpenRead(path))
                {
                    var cfg = (AppConfig)new DataContractJsonSerializer(typeof(AppConfig)).ReadObject(fs);
                    if (cfg == null)
                    {
                        error = "leer";
                    }
                    return cfg;
                }
            }
            catch (Exception ex)
            {
                error = ex.GetType().Name;
                return null;
            }
        }

        // With a path for the offline tests, which must not touch the real file.
        internal static AppConfig Load(string path)
        {
            string error;
            AppConfig cfg = Read(path, out error);
            bool restored = false;
            if (cfg == null && error != null)
            {
                string backup = BackupPath(path);
                string backupError;
                cfg = Read(backup, out backupError);
                if (cfg != null)
                {
                    Log.Write("Einstellungen nicht lesbar (" + error + "), aus config.bak.json wiederhergestellt");
                    try
                    {
                        File.Copy(path, BrokenPath(path), true);
                        File.Copy(backup, path, true);
                    }
                    catch (Exception ex)
                    {
                        Log.Write("Sicherung nicht zurückkopiert: " + ex.GetType().Name);
                    }
                    restored = true;
                }
                else
                {
                    Log.Write("Einstellungen nicht lesbar (" + error + "), Sicherung " + (backupError != null ? "ebenfalls nicht lesbar" : "fehlt") +
                              ", nehme Standardwerte, die kaputte Datei bleibt als config.broken.json");
                    try
                    {
                        File.Copy(path, BrokenPath(path), true);
                        File.Delete(path);
                    }
                    catch (Exception ex)
                    {
                        Log.Write("config.broken.json nicht angelegt: " + ex.GetType().Name);
                    }
                }
            }
            if (cfg == null)
            {
                cfg = new AppConfig();
            }
            if (cfg.Cubes == null)
            {
                cfg.Cubes = new List<CubeEntry>();
            }
            if (cfg.Shop == null)
            {
                cfg.Shop = new ShopConfig();
            }
            if (cfg.Woo == null)
            {
                cfg.Woo = new WooConfig();
            }
            if (cfg.Notify == null)
            {
                cfg.Notify = new NotifyConfig();
            }
            cfg.RestoredFromBackup = restored;
            if (cfg.Cubes.Count == 0 && !string.IsNullOrWhiteSpace(cfg.LegacyAddress))
            {
                cfg.Cubes.Add(new CubeEntry
                {
                    Address = cfg.LegacyAddress.Trim(),
                    Function = CubeFunction.Claude,
                    Design = ClaudeDesigns.Default,
                    AdoptCubeDesign = true,
                    ApiKeyProtected = cfg.LegacyApiKey ?? ""
                });
                Log.Write("Einstellungen von Version 0.1.0 übernommen: ein Würfel " + cfg.LegacyAddress.Trim());
            }
            cfg.LegacyAddress = null;
            cfg.LegacyApiKey = null;
            if (cfg.Cubes.Count == 0)
            {
                cfg.Cubes.Add(new CubeEntry { Address = "sysoptv.local" });
            }
            return cfg;
        }

        internal void Save()
        {
            Save(AppPaths.ConfigFile);
        }

        // Writes a new file next to it first, then swaps. The old config.json
        // becomes config.bak.json, but only if it can be read: a broken file
        // never replaces a good backup.
        internal void Save(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string tmp = path + ".tmp";
            using (FileStream fs = File.Create(tmp))
            {
                new DataContractJsonSerializer(typeof(AppConfig)).WriteObject(fs, this);
            }
            if (File.Exists(path))
            {
                string error;
                bool oldReadable = Read(path, out error) != null;
                File.Replace(tmp, path, oldReadable ? BackupPath(path) : null);
            }
            else
            {
                File.Move(tmp, path);
            }
        }
    }
}
