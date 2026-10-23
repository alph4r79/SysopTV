// SysopTV, Copyright (C) 2026 Christoph Scheel
// Licensed under the GNU General Public License v3.0, see LICENSE.
//
// Log file %LOCALAPPDATA%\SysopTV\app.log, at most 1 MB, then one backup
// app.old.log. Never write tokens, keys or passwords here.

using System;
using System.IO;
using System.Text;

namespace SysopTV.Tray
{
    internal static class Log
    {
        private const long Limit = 1024 * 1024;
        private static readonly object Gate = new object();

        // In --once mode every line also goes to the console.
        internal static bool Echo;

        internal static void Write(string text)
        {
            string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + text;
            lock (Gate)
            {
                try
                {
                    Directory.CreateDirectory(AppPaths.LogDir);
                    var info = new FileInfo(AppPaths.LogFile);
                    if (info.Exists && info.Length > Limit)
                    {
                        if (File.Exists(AppPaths.OldLogFile))
                        {
                            File.Delete(AppPaths.OldLogFile);
                        }
                        File.Move(AppPaths.LogFile, AppPaths.OldLogFile);
                    }
                    File.AppendAllText(AppPaths.LogFile, line + Environment.NewLine, new UTF8Encoding(false));
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
            if (Echo)
            {
                Console.WriteLine(line);
            }
        }
    }
}
