// SysopTV, Copyright (C) 2026 Christoph Scheel
// Licensed under the GNU General Public License v3.0, see LICENSE.
//
// Every file of the program is found here. Normally config.json lies in
// %APPDATA%\SysopTV, state.json and app.log in %LOCALAPPDATA%\SysopTV. The
// environment variable SYSOPTV_DATA_DIR or the option --data-dir <folder>
// puts all three into that one folder instead, for tests and trials that
// must never touch the real settings.

using System;
using System.IO;

namespace SysopTV.Tray
{
    internal static class AppPaths
    {
        internal const string DataDirVariable = "SYSOPTV_DATA_DIR";

        // Set by --data-dir, wins over the environment variable.
        internal static string DataDirOption;

        // The other folder, or null for the normal folders of the user.
        internal static string DataDir
        {
            get
            {
                string dir = DataDirOption;
                if (string.IsNullOrWhiteSpace(dir))
                {
                    dir = Environment.GetEnvironmentVariable(DataDirVariable);
                }
                return string.IsNullOrWhiteSpace(dir) ? null : Path.GetFullPath(dir.Trim());
            }
        }

        // The normal folders, whatever DataDir says.
        internal static string UserConfigDir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SysopTV"); }
        }

        internal static string UserLogDir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SysopTV"); }
        }

        // config.json and the folder "alt" for old shortcuts.
        internal static string ConfigDir
        {
            get { return DataDir ?? UserConfigDir; }
        }

        internal static string ConfigFile
        {
            get { return Path.Combine(ConfigDir, "config.json"); }
        }

        internal static string LegacyDir
        {
            get { return Path.Combine(ConfigDir, "alt"); }
        }

        // state.json and app.log (the old script writes sender.log to the normal folder).
        internal static string LogDir
        {
            get { return DataDir ?? UserLogDir; }
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
