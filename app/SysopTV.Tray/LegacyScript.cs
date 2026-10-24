// SysopTV, Copyright (C) 2026 Christoph Scheel
// Licensed under the GNU General Public License v3.0, see LICENSE.
//
// Finds the old PowerShell sender: shortcuts in the startup folder that
// start sysoptv-sender.ps1, and running processes with it on the command
// line. Running processes are never stopped by the program.

using System;
using System.Collections.Generic;
using System.IO;
using System.Management;
using System.Text;

namespace SysopTV.Tray
{
    internal static class LegacyScript
    {
        private const string ScriptName = "sysoptv-sender.ps1";

        internal static List<string> FindShortcuts()
        {
            var result = new List<string>();
            try
            {
                string startup = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
                foreach (string lnk in Directory.GetFiles(startup, "*.lnk"))
                {
                    // A .lnk file holds the target and arguments as ANSI or UTF-16 text.
                    byte[] bytes = File.ReadAllBytes(lnk);
                    string ansi = Encoding.Default.GetString(bytes);
                    string wide = Encoding.Unicode.GetString(bytes);
                    if (ansi.IndexOf(ScriptName, StringComparison.OrdinalIgnoreCase) >= 0 ||
                        wide.IndexOf(ScriptName, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        result.Add(lnk);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Write("Autostart-Ordner nicht lesbar: " + ex.GetType().Name);
            }
            return result;
        }

        internal static int CountRunning()
        {
            int count = 0;
            try
            {
                // Any process with the script in its command line, not only powershell.exe.
                using (var searcher = new ManagementObjectSearcher(
                    "SELECT CommandLine FROM Win32_Process WHERE CommandLine LIKE '%" + ScriptName + "%'"))
                {
                    foreach (ManagementBaseObject p in searcher.Get())
                    {
                        string cmd = p["CommandLine"] as string;
                        if (cmd != null && cmd.IndexOf(ScriptName, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            count++;
                        }
                        p.Dispose();
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Write("Prozesse nicht prüfbar: " + ex.GetType().Name);
            }
            return count;
        }

        // Moves the shortcuts to %APPDATA%\SysopTV\alt\, nothing is deleted.
        internal static void MoveShortcuts(IEnumerable<string> shortcuts)
        {
            Directory.CreateDirectory(AppPaths.LegacyDir);
            foreach (string lnk in shortcuts)
            {
                string target = Path.Combine(AppPaths.LegacyDir, Path.GetFileName(lnk));
                if (File.Exists(target))
                {
                    target = Path.Combine(AppPaths.LegacyDir,
                        Path.GetFileNameWithoutExtension(lnk) + " " + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".lnk");
                }
                File.Move(lnk, target);
                Log.Write("Alte Verknüpfung verschoben nach " + target);
            }
        }
    }
}
