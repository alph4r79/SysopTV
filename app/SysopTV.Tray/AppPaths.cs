// SysopTV, Copyright (C) 2026 Christoph Scheel
// Licensed under the GNU General Public License v3.0, see LICENSE.

using System;
using System.IO;

namespace SysopTV.Tray
{
    internal static class AppPaths
    {
        // %APPDATA%\SysopTV: config.json and the folder "alt" for old shortcuts.
        internal static string ConfigDir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SysopTV"); }
        }

        internal static string ConfigFile
        {
            get { return Path.Combine(ConfigDir, "config.json"); }
        }

        internal static string LegacyDir
        {
            get { return Path.Combine(ConfigDir, "alt"); }
        }

        // %LOCALAPPDATA%\SysopTV: app.log (the old script writes sender.log there as well).
        internal static string LogDir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SysopTV"); }
        }

        internal static string LogFile
        {
            get { return Path.Combine(LogDir, "app.log"); }
        }

        internal static string OldLogFile
        {
            get { return Path.Combine(LogDir, "app.old.log"); }
        }

        internal static string CredentialsFile
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", ".credentials.json"); }
        }
    }
}
