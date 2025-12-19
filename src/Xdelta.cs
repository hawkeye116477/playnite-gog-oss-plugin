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

                var xdeltaPath = "";
                if (string.IsNullOrWhiteSpace(envPath) == false)
                {
                    xdeltaPath = envPath;
                }
                else
                {
                    var pf64 = Environment.GetEnvironmentVariable("ProgramW6432");
                    if (string.IsNullOrEmpty(pf64))
                    {
                        pf64 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                    }
                    xdeltaPath = Path.Combine(pf64, "GOG OSS", "xdelta3.exe");
                    if (!File.Exists(xdeltaPath))
                    {
                        var playniteAPI = API.Instance;
                        if (playniteAPI.ApplicationInfo.IsPortable)
                        {
                            xdeltaPath = Path.Combine(playniteAPI.Paths.ApplicationPath, "GOG OSS", "xdelta3.exe");
                        }
                    }
                }
                var savedSettings = GogOssLibrary.GetSettings();
                if (savedSettings != null)
                {
                    var savedXdeltaPath = savedSettings.SelectedXdeltaPath;
                    var playniteDirectoryVariable = ExpandableVariables.PlayniteDirectory.ToString();
                    if (savedXdeltaPath != "")
                    {
                        if (savedXdeltaPath.Contains(playniteDirectoryVariable))
                        {
                            var playniteAPI = API.Instance;
                            savedXdeltaPath = savedXdeltaPath.Replace(playniteDirectoryVariable, playniteAPI.Paths.ApplicationPath);
                        }
                        xdeltaPath = savedXdeltaPath;
                    }
                }
                if (!File.Exists(xdeltaPath))
                {
                    xdeltaPath = "";
                }
                return xdeltaPath;
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
