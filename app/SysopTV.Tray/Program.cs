// SysopTV, Copyright (C) 2026 Christoph Scheel
// Licensed under the GNU General Public License v3.0, see LICENSE.

using System;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

namespace SysopTV.Tray
{
    internal static class Program
    {
        // "Local\" keeps both per Windows session, like the mutex of the old script.
        internal const string MutexName = "Local\\SysopTV-App";
        private const string ShowEventName = "Local\\SysopTV-App-Show";

        internal static string Version
        {
            get
            {
                Version v = Assembly.GetExecutingAssembly().GetName().Version;
                return v.Major + "." + v.Minor + "." + v.Build;
            }
        }

        [STAThread]
        private static int Main(string[] args)
        {
            Net.Setup();

            if (HasArg(args, "--once"))
            {
                return OnceMode.Run(args);
            }
            string preview = ArgValue(args, "--preview");
            if (preview != null)
            {
                return PreviewMode.Run(preview);
            }

            bool created;
            using (var mutex = new Mutex(true, MutexName, out created))
            {
                if (!created)
                {
                    // Second start: ask the running instance to open its settings.
                    try
                    {
                        using (EventWaitHandle ev = EventWaitHandle.OpenExisting(ShowEventName))
                        {
                            ev.Set();
                        }
                    }
                    catch (WaitHandleCannotBeOpenedException)
                    {
                    }
                    return 0;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                using (var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName))
                {
                    Application.Run(new TrayApp(showEvent));
                }
                GC.KeepAlive(mutex);
            }
            return 0;
        }

        internal static bool HasArg(string[] args, string name)
        {
            foreach (string a in args)
            {
                if (string.Equals(a, name, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        internal static string ArgValue(string[] args, string name)
        {
            for (int i = 0; i + 1 < args.Length; i++)
            {
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                {
                    return args[i + 1];
                }
            }
            return null;
        }
    }
}
