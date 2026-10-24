// SysopTV, Copyright (C) 2026 Christoph Scheel
// Licensed under the GNU General Public License v3.0, see LICENSE.
//
// SysopTV.exe --once [--device <address>]: query once, send one full picture
// of the Claude design to the cube, print the result and exit. Never prints
// the token.

using System;
using System.Drawing;
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
            CubeEntry cube = cfg.Cubes.Find(c => c.Function == CubeFunction.Claude) ?? cfg.Cubes[0];
            string device = Program.ArgValue(args, "--device") ?? cube.Address;

            // Same rules as the background program: no query inside the interval
            // after the last one and none during a 429 block, state.json is updated.
            AppState state = AppState.Load(AppState.DefaultPath);
            var claude = new ClaudeClient { BlockedUntil = AppState.ToLocal(state.BlockedUntil) ?? DateTime.MinValue };
            var policy = new FailurePolicy();
            UsageData stored = state.LastOk();
            if (stored != null)
            {
                policy.Seed(stored, AppState.ToLocal(state.LastOkAt).Value);
            }
            DateTime last = AppState.ToLocal(state.LastQuery) ?? DateTime.MinValue;
            UsageData data;
            if (!SenderService.QueryDue(DateTime.Now, last, AppConfig.MinInterval, DateTime.MinValue))
            {
                Console.WriteLine("Letzte Abfrage um " + last.ToString("HH:mm:ss") + ", Anthropic wird frühestens 120 Sekunden danach wieder gefragt.");
                data = policy.Resolve(new UsageData { State = "error", Reason = "gespeicherte Werte" }, DateTime.Now);
            }
            else
            {
                UsageData raw = claude.Query();
                if (raw.State != "error" || raw.Attempted)
                {
                    state.LastQuery = AppState.ToUnix(DateTime.Now);
                }
                data = policy.Resolve(raw, DateTime.Now);
                if (data.State == "ok" && !data.Held)
                {
                    state.SetLastOk(data, DateTime.Now);
                }
                state.BlockedUntil = claude.BlockedUntil > DateTime.Now ? AppState.ToUnix(claude.BlockedUntil) : 0;
                state.Save(AppState.DefaultPath);
            }
            Log.Write("Testlauf, Würfel " + device);
            Log.Write("Daten: " + data.Summary());
            if (data.State == "ok")
            {
                Console.WriteLine("Session: " + Describe(data.Session));
                Console.WriteLine("Woche:   " + Describe(data.Week));
            }

            // The cube draws nothing itself: the numbers go as a picture in the design of the cube.
            CubeEntry view = cube.Function == CubeFunction.Claude ? cube : new CubeEntry { Function = CubeFunction.Claude };
            string design = view.DesignOrDefault;
            Canvas picture;
            if (data.State == "ok" || data.State == "login")
            {
                UsageData values = data.State == "ok" ? data : stored;
                picture = ClaudeDesigns.Render(design, UsageView.Build(values, data.State == "login", null, view.ShowUsed, DateTimeOffset.Now));
            }
            else
            {
                picture = Notice.Render(design, "CLAUDE", "Keine Daten von Anthropic", null, DateTime.Now);
            }
            SendResult send = Cube.SendFrame(device, FrameCodec.Encode(picture, new Rectangle(0, 0, Canvas.Size, Canvas.Size)), cube.ApiKey,
                                             SenderService.SourceText(view, design));
            if (send.Ok)
            {
                Log.Write("Würfel hat das Bild angenommen: " + send.Message);
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
