// SysopTV, Copyright (C) 2026 Christoph Scheel
// Licensed under the GNU General Public License v3.0, see LICENSE.
//
// Settings in %APPDATA%\SysopTV\config.json. Secrets (API keys, admin
// passwords, Shopify token and secret) are stored only encrypted with DPAPI
// for the current Windows user, never as plain text.
//
// Version 0.1.0 had one cube (deviceAddress, apiKeyProtected). Such a file is
// taken over as one cube with the function "usage".

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

        [OnDeserialized]
        private void OnDeserialized(StreamingContext context)
        {
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
            AppConfig cfg = null;
            try
            {
                if (File.Exists(AppPaths.ConfigFile))
                {
                    using (FileStream fs = File.OpenRead(AppPaths.ConfigFile))
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
            Directory.CreateDirectory(AppPaths.ConfigDir);
            string tmp = AppPaths.ConfigFile + ".tmp";
            using (FileStream fs = File.Create(tmp))
            {
                new DataContractJsonSerializer(typeof(AppConfig)).WriteObject(fs, this);
            }
            if (File.Exists(AppPaths.ConfigFile))
            {
                File.Replace(tmp, AppPaths.ConfigFile, null);
            }
            else
            {
                File.Move(tmp, AppPaths.ConfigFile);
            }
        }
    }
}
