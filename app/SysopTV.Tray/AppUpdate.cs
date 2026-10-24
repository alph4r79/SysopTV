// SysopTV, Copyright (C) 2026 Christoph Scheel
// Licensed under the GNU General Public License v3.0, see LICENSE.
//
// Program update from GitHub, only for the installed program (the portable
// one only shows the hint and the link). The setup is downloaded into a temp
// folder and checked against SHA256SUMS.txt. After the confirmation the cubes
// get NO CARRIER, then a second, short-lived SysopTV.exe (--run-setup) waits
// until this one has ended, checks the file once more and starts the setup in
// the silent mode of Inno Setup. The setup starts the program again
// (/STARTAPP=1). It needs no administrator rights (PrivilegesRequired=lowest,
// installation under %LOCALAPPDATA%\Programs), so Windows asks nothing.
// config.json is never touched by the setup.

using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace SysopTV.Tray
{
    internal static class AppUpdate
    {
        // Silent mode of Inno Setup; /STARTAPP=1 starts the program afterwards (see sysoptv.iss).
        internal const string SetupArguments = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /STARTAPP=1";

        // Starts a process. The offline tests put a fake here and check the call.
        internal static Func<ProcessStartInfo, bool> StartProcess = psi =>
        {
            using (Process.Start(psi))
            {
                return true;
            }
        };

        // Installed by the setup: its uninstaller lies next to the program.
        internal static bool IsInstalled(string folder)
        {
            return File.Exists(Path.Combine(folder, "unins000.exe"));
        }

        internal static bool Installed
        {
            get { return IsInstalled(AppDomain.CurrentDomain.BaseDirectory); }
        }

        // Downloads the setup of the release into a fresh temp folder and checks
        // it. Returns its path, or null with a German error text.
        internal static string DownloadSetup(ReleaseInfo r, out string sha256, out string error)
        {
            sha256 = null;
            if (r == null || r.SetupUrl == null)
            {
                error = "Im Release gibt es kein Setup.";
                return null;
            }
            byte[] data = UpdateCheck.DownloadVerified(r, r.SetupUrl, r.SetupName, out error);
            if (data == null)
            {
                return null;
            }
            string dir = Path.Combine(Path.GetTempPath(), "sysoptv-update-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, r.SetupName);
            File.WriteAllBytes(path, data);
            sha256 = UpdateCheck.Sha256Hex(data);
            return path;
        }

        // Removes a downloaded setup and its temp folder.
        internal static void Discard(string setupPath)
        {
            try
            {
                if (setupPath != null && File.Exists(setupPath))
                {
                    File.Delete(setupPath);
                    Directory.Delete(Path.GetDirectoryName(setupPath));
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        // The second SysopTV.exe that starts the setup once this one has ended.
        internal static ProcessStartInfo HelperStart(string exe, string setupPath, string sha256, int pid)
        {
            return new ProcessStartInfo(exe, "--run-setup \"" + setupPath + "\" --sha256 " + sha256 + " --wait-pid " + pid.ToString(CultureInfo.InvariantCulture))
            {
                UseShellExecute = false
            };
        }

        internal static ProcessStartInfo SetupStart(string setupPath)
        {
            return new ProcessStartInfo(setupPath, SetupArguments)
            {
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(setupPath)
            };
        }

        // SysopTV.exe --run-setup <setup> --sha256 <hex> --wait-pid <pid>
        internal static int RunSetup(string[] args)
        {
            string setup = Program.ArgValue(args, "--run-setup");
            string sha = Program.ArgValue(args, "--sha256");
            int pid;
            int.TryParse(Program.ArgValue(args, "--wait-pid") ?? "", NumberStyles.Integer, CultureInfo.InvariantCulture, out pid);
            if (pid > 0)
            {
                try
                {
                    using (Process p = Process.GetProcessById(pid))
                    {
                        p.WaitForExit(30000);
                    }
                }
                catch (ArgumentException)
                {
                    // already ended
                }
                catch (InvalidOperationException)
                {
                }
            }
            // The file lay in a temp folder in the meantime: check it once more.
            if (setup == null || sha == null || !File.Exists(setup) ||
                UpdateCheck.Sha256Hex(File.ReadAllBytes(setup)) != sha.ToLowerInvariant())
            {
                Log.Write("Update: Setup fehlt oder wurde verändert, nicht gestartet");
                Discard(setup);
                return 1;
            }
            Log.Write("Update: starte " + Path.GetFileName(setup) + " " + SetupArguments);
            return StartProcess(SetupStart(setup)) ? 0 : 1;
        }
    }
}
