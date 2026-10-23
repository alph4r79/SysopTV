// SysopTV, Copyright (C) 2026 Christoph Scheel
// Licensed under the GNU General Public License v3.0, see LICENSE.
//
// SysopTV.exe --once [--device <address>]: query once, send to the cube,
// print the result and exit. Never prints the token.

using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace SysopTV.Tray
{
    internal static class OnceMode
    {
        [DllImport("kernel32.dll")]
        private static extern bool AttachConsole(int processId);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetStdHandle(int handle);

        internal static int Run(string[] args)
        {
            // A WinExe has no console. Use the redirected output if there is one,
            // otherwise the console of the calling window.
            IntPtr stdout = GetStdHandle(-11);
            if (stdout == IntPtr.Zero || stdout == new IntPtr(-1))
            {
                AttachConsole(-1);
            }
            else if (Console.IsOutputRedirected)
            {
                // Into a file or pipe as UTF-8, so the umlauts survive.
                var writer = new System.IO.StreamWriter(Console.OpenStandardOutput(), new System.Text.UTF8Encoding(false));
                writer.AutoFlush = true;
                Console.SetOut(writer);
            }
            Log.Echo = true;
            Console.WriteLine();

            Mutex running;
            if (Mutex.TryOpenExisting(Program.MutexName, out running))
            {
                running.Dispose();
                Console.WriteLine("SysopTV läuft bereits im Hintergrund. Ein zweiter Lauf würde Anthropic doppelt abfragen.");
                return 3;
            }

            AppConfig cfg = AppConfig.Load();
            CubeEntry cube = cfg.Cubes.Find(c => c.Function == CubeFunction.Usage) ?? cfg.Cubes[0];
            string device = Program.ArgValue(args, "--device") ?? cube.Address;

            var claude = new ClaudeClient();
            UsageData data = new FailurePolicy().Resolve(claude.Query(), DateTime.Now);
            Log.Write("Testlauf, Würfel " + device);
            Log.Write("Daten: " + data.Summary());
            if (data.State == "ok")
            {
                Console.WriteLine("Session: " + Describe(data.Session));
                Console.WriteLine("Woche:   " + Describe(data.Week));
            }

            SendResult send = Cube.SendUsage(device, data.ToJson(DateTimeOffset.Now), cube.ApiKey);
            if (send.Ok)
            {
                Log.Write("Würfel hat die Daten angenommen: " + send.Message);
            }
            else
            {
                Log.Write("Würfel nicht erreicht: " + send.Message);
            }
            Console.WriteLine("HTTP-Status des Würfels: " + (send.Status > 0 ? send.Status.ToString() : "keine Antwort"));
            return send.Ok ? 0 : 1;
        }

        private static string Describe(UsageLimit l)
        {
            if (l == null)
            {
                return "keine Daten";
            }
            string reset = "keine Reset-Zeit";
            if (l.ResetsAt.HasValue)
            {
                reset = "Reset " + DateTimeOffset.FromUnixTimeSeconds(l.ResetsAt.Value).ToLocalTime().ToString("ddd dd.MM. HH:mm:ss");
            }
            return l.Used + " % verbraucht, " + reset;
        }
    }
}
