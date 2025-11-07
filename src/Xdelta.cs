using Playnite.Common;
using Playnite.SDK;
using System;
using System.IO;
using System.Linq;

namespace GogOssLibraryNS
{
    public class Xdelta
    {
        public static string InstallationPath
        {
            get
            {
                string[] xdeltaExes = { "xdelta3_x86.exe", "xdelta3_x64.exe", "xdelta3.exe" };
                string envPath = Environment.GetEnvironmentVariable("PATH")
                                            .Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                                            .SelectMany(pathEntry => xdeltaExes.Select(xdeltaExe => Path.Combine(pathEntry.Trim(), xdeltaExe)))
                                            .FirstOrDefault(File.Exists);

                var launcherPath = "";
                if (string.IsNullOrWhiteSpace(envPath) == false)
                {
                    launcherPath = envPath;
                }
                else
                {
                    var pf64 = Environment.GetEnvironmentVariable("ProgramW6432");
                    if (string.IsNullOrEmpty(pf64))
                    {
                        pf64 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                    }
                    launcherPath = Path.Combine(pf64, "GOG OSS", "xdelta3.exe");
                    if (!File.Exists(launcherPath))
                    {
                        var playniteAPI = API.Instance;
                        if (playniteAPI.ApplicationInfo.IsPortable)
                        {
                            launcherPath = Path.Combine(playniteAPI.Paths.ApplicationPath, "GOG OSS", "xdelta3.exe");
                        }
                    }
                }
                var savedSettings = GogOssLibrary.GetSettings();
                if (savedSettings != null)
                {
                    var savedLauncherPath = savedSettings.SelectedCometPath;
                    var playniteDirectoryVariable = ExpandableVariables.PlayniteDirectory.ToString();
                    if (savedLauncherPath != "")
                    {
                        if (savedLauncherPath.Contains(playniteDirectoryVariable))
                        {
                            var playniteAPI = API.Instance;
                            savedLauncherPath = savedLauncherPath.Replace(playniteDirectoryVariable, playniteAPI.Paths.ApplicationPath);
                        }
                        launcherPath = savedLauncherPath;
                    }
                }
                if (!File.Exists(launcherPath))
                {
                    launcherPath = "";
                }
                return launcherPath;
            }
        }

        public static void StartClient()
        {
            if (!InstallationPath.IsNullOrEmpty())
            {
                ProcessStarter.StartProcess("cmd", $"/K \"{InstallationPath}\" --h", Path.GetDirectoryName(InstallationPath));
            }
        }

        public static bool IsInstalled
        {
            get
            {
                if (string.IsNullOrEmpty(InstallationPath) || !File.Exists(InstallationPath))
                {
                    return false;
                }
                else
                {
                    return true;
                }
            }
        }
    }
}
