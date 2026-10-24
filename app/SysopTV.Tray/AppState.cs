// SysopTV, Copyright (C) 2026 Christoph Scheel
// Licensed under the GNU General Public License v3.0, see LICENSE.
//
// %LOCALAPPDATA%\SysopTV\state.json: time of the last query at Anthropic, an
// active 429 block and the last valid values with their time. After a restart
// the program waits for the interval and the block instead of asking at once.
// Never contains the login token or anything from .credentials.json.

using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace SysopTV.Tray
{
    [DataContract]
    internal sealed class StoredLimit
    {
        [DataMember(Name = "used")] public int Used;
        [DataMember(Name = "resetsAt", EmitDefaultValue = false)] public long? ResetsAt;

        internal static StoredLimit From(UsageLimit l)
        {
            return l == null ? null : new StoredLimit { Used = l.Used, ResetsAt = l.ResetsAt };
        }

        internal UsageLimit ToLimit()
        {
            return new UsageLimit { Used = Math.Max(0, Math.Min(100, Used)), ResetsAt = ResetsAt };
        }
    }

    [DataContract]
    internal sealed class AppState
    {
        // All times in Unix seconds (UTC), 0 means none.
        [DataMember(Name = "lastQuery")] public long LastQuery;
        [DataMember(Name = "blockedUntil")] public long BlockedUntil;
        [DataMember(Name = "lastOkAt")] public long LastOkAt;
        [DataMember(Name = "session", EmitDefaultValue = false)] public StoredLimit Session;
        [DataMember(Name = "week", EmitDefaultValue = false)] public StoredLimit Week;

        // Last valid shop numbers per source with their time, no customer data.
        [DataMember(Name = "shopify", EmitDefaultValue = false)] public ShopData ShopifyShop;
        [DataMember(Name = "shopifyOkAt")] public long ShopifyOkAt;
        [DataMember(Name = "woocommerce", EmitDefaultValue = false)] public ShopData WooShop;
        [DataMember(Name = "woocommerceOkAt")] public long WooOkAt;

        internal ShopData Shop(string source)
        {
            return source == ShopSources.WooCommerce ? WooShop : ShopifyShop;
        }

        internal long ShopOkAt(string source)
        {
            return source == ShopSources.WooCommerce ? WooOkAt : ShopifyOkAt;
        }

        internal void SetShop(string source, ShopData data, DateTime when)
        {
            if (source == ShopSources.WooCommerce)
            {
                WooShop = data;
                WooOkAt = ToUnix(when);
            }
            else
            {
                ShopifyShop = data;
                ShopifyOkAt = ToUnix(when);
            }
        }

        internal static string DefaultPath
        {
            get { return Path.Combine(AppPaths.LogDir, "state.json"); }
        }

        internal static DateTime? ToLocal(long unix)
        {
            return unix > 0 ? DateTimeOffset.FromUnixTimeSeconds(unix).LocalDateTime : (DateTime?)null;
        }

        internal static long ToUnix(DateTime? local)
        {
            return local.HasValue && local.Value > DateTime.MinValue ? new DateTimeOffset(local.Value).ToUnixTimeSeconds() : 0;
        }

        // The stored values as "ok" data, or null if there are none.
        internal UsageData LastOk()
        {
            if (LastOkAt <= 0 || (Session == null && Week == null))
            {
                return null;
            }
            return new UsageData
            {
                State = "ok",
                Session = Session != null ? Session.ToLimit() : null,
                Week = Week != null ? Week.ToLimit() : null
            };
        }

        internal void SetLastOk(UsageData data, DateTime when)
        {
            Session = StoredLimit.From(data.Session);
            Week = StoredLimit.From(data.Week);
            LastOkAt = ToUnix(when);
        }

        internal static AppState Load(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    using (FileStream fs = File.OpenRead(path))
                    {
                        var s = (AppState)new DataContractJsonSerializer(typeof(AppState)).ReadObject(fs);
                        if (s != null)
                        {
                            return s;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Write("state.json nicht lesbar, beginne ohne gespeicherte Werte: " + ex.GetType().Name);
            }
            return new AppState();
        }

        internal void Save(string path)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                string tmp = path + ".tmp";
                using (FileStream fs = File.Create(tmp))
                {
                    new DataContractJsonSerializer(typeof(AppState)).WriteObject(fs, this);
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
            catch (Exception ex)
            {
                Log.Write("state.json nicht geschrieben: " + ex.GetType().Name);
            }
        }
    }
}
