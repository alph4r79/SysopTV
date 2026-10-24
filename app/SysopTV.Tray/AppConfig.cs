// SysopTV, Copyright (C) 2026 Christoph Scheel
// Licensed under the GNU General Public License v3.0, see LICENSE.
//
// Settings in %APPDATA%\SysopTV\config.json. Secrets (API keys, admin
// passwords, Shopify token and secret) are stored only encrypted with DPAPI
// for the current Windows user, never as plain text.
//
// Version 0.1.0 had one cube (deviceAddress, apiKeyProtected). Such a file is
// taken over as one cube with the function "usage".
// Files of version 0.2.0 are read as they are; design, showUsed and
// cycleSeconds of each cube then get their defaults. Shop cubes of versions
// before 0.5.0 keep their design under the view "Details".

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
        internal const string Usage = "usage";         // Claude, the cube draws itself
        internal const string ClaudeFrame = "claude";  // Claude as picture
        internal const string ShopFrame = "shop";      // Shop as picture

        internal static string Label(string f)
        {
            switch (f)
            {
                case ClaudeFrame: return "Claude als Bild";
                case ShopFrame: return "Shop als Bild";
                default: return "Claude, Würfel zeichnet selbst";
            }
        }
    }

    [DataContract]
    internal sealed class CubeEntry
    {
        [DataMember(Name = "address")] public string Address = "";
        [DataMember(Name = "name")] public string Name = "";
        [DataMember(Name = "function")] public string Function = CubeFunction.Usage;
        [DataMember(Name = "apiKeyProtected")] public string ApiKeyProtected = "";
        [DataMember(Name = "adminPasswordProtected")] public string AdminPasswordProtected = "";

        // For the pictures only (functions "claude" and "shop"), chosen in the program.
        // Older files have none of these, OnDeserializing sets the defaults.
        [DataMember(Name = "design")] public string Design = "";
        [DataMember(Name = "showUsed")] public bool ShowUsed = true;
        [DataMember(Name = "cycleSeconds")] public int CycleSeconds;

        // Shop as picture: view "big" (Großanzeige) or "details" (the four detail designs).
        [DataMember(Name = "shopView")] public string ShopView = ShopBig.ViewBig;
        [DataMember(Name = "bigPeriod")] public string BigPeriod = ShopBig.DefaultPeriod;
        [DataMember(Name = "bigContent")] public string BigContent = ShopBig.DefaultContent;
        [DataMember(Name = "bigStyle")] public string BigStyle = ShopBig.DefaultStyle;
        [DataMember(Name = "periodCycleSeconds")] public int PeriodCycleSeconds;

        // Flip animation of the style Fallblatt, for Claude and the shop.
        [DataMember(Name = "animate")] public bool Animate = true;

        internal static readonly int[] CycleChoices = { 0, 30, 60 };

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
            get { return Function == CubeFunction.ShopFrame && ShopView == ShopBig.ViewBig; }
        }

        // The period to show now: with change Heute, Woche, Monat one after the
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

        // The designs to choose from for the function of this cube, null for "usage".
        internal DesignInfo[] Designs
        {
            get
            {
                if (Function == CubeFunction.ClaudeFrame)
                {
                    return ClaudeDesigns.All;
                }
                return Function == CubeFunction.ShopFrame ? ShopDesigns.All : null;
            }
        }

        // The chosen design, or the default if it does not fit the function.
        internal string DesignOrDefault
        {
            get
            {
                if (Function == CubeFunction.ClaudeFrame)
                {
                    return ClaudeDesigns.Known(Design) ? Design : ClaudeDesigns.Default;
                }
                return ShopDesigns.Known(Design) ? Design : ShopDesigns.Default;
            }
        }

        // The design to show now: with automatic change one after the other,
        // starting with the chosen one, the same on every PC clock tick.
        internal string DesignAt(DateTime now)
        {
            DesignInfo[] list = Designs;
            string chosen = DesignOrDefault;
            if (list == null || CycleSeconds <= 0)
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
            if (Function != CubeFunction.ClaudeFrame && Function != CubeFunction.ShopFrame)
            {
                Function = CubeFunction.Usage;
            }
            ApiKeyProtected = ApiKeyProtected ?? "";
            AdminPasswordProtected = AdminPasswordProtected ?? "";
        }

        internal CubeEntry Copy()
        {
            return (CubeEntry)MemberwiseClone();
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

    [DataContract]
    internal sealed class AppConfig
    {
        internal const int MinInterval = 120;

        [DataMember(Name = "cubes")] public List<CubeEntry> Cubes;
        [DataMember(Name = "intervalSeconds")] public int IntervalSeconds;
        [DataMember(Name = "shop")] public ShopConfig Shop;

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
            LegacyAddress = null;
            LegacyApiKey = null;
        }

        internal int Interval
        {
            get { return Math.Max(MinInterval, IntervalSeconds); }
        }

        internal AppConfig Clone()
        {
            var c = new AppConfig { IntervalSeconds = IntervalSeconds, Shop = Shop.Copy() };
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

        // With a path for the offline tests, which must not touch the real file.
        internal static AppConfig Load(string path)
        {
            AppConfig cfg = null;
            try
            {
                if (File.Exists(path))
                {
                    using (FileStream fs = File.OpenRead(path))
                    {
                        cfg = (AppConfig)new DataContractJsonSerializer(typeof(AppConfig)).ReadObject(fs);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Write("Einstellungen nicht lesbar, nehme Standardwerte: " + ex.GetType().Name);
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
            if (cfg.Cubes.Count == 0 && !string.IsNullOrWhiteSpace(cfg.LegacyAddress))
            {
                cfg.Cubes.Add(new CubeEntry
                {
                    Address = cfg.LegacyAddress.Trim(),
                    Function = CubeFunction.Usage,
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
                File.Replace(tmp, path, null);
            }
            else
            {
                File.Move(tmp, path);
            }
        }
    }
}
