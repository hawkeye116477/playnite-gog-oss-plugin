using Playnite.SDK;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;

namespace CometLibrary
{
    public class Comet
    {
        public const string EnStoreLocaleString = "US_USD_en-US";

        public static string ClientExecPath
        {
            get
            {
                var path = InstallationPath;
                return string.IsNullOrEmpty(path) ? string.Empty : path;
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

        public static string InstallationPath
        {
            get
            {
                var launcherPath = "";
                var heroicCometBinary = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                           @"Programs\heroic\resources\app.asar.unpacked\build\bin\x64\win32\comet.exe");
                if(File.Exists(heroicCometBinary))
                {
                    launcherPath = heroicCometBinary;
                }
                else
                {
                    var pf64 = Environment.GetEnvironmentVariable("ProgramW6432");
                    if (string.IsNullOrEmpty(pf64))
                    {
                        pf64 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                    }
                    launcherPath = Path.Combine(pf64, "Comet", "comet-x86_64-pc-windows-msvc.exe");
                    if (!File.Exists(launcherPath))
                    {
                        var playniteAPI = API.Instance;
                        if (playniteAPI.ApplicationInfo.IsPortable)
                        {
                            launcherPath = Path.Combine(playniteAPI.Paths.ApplicationPath, "Comet", "comet-x86_64-pc-windows-msvc.exe");
                        }
                    }
                }
                var savedSettings = CometLibrary.GetSettings();
                if (savedSettings != null)
                {
                    var savedLauncherPath = savedSettings.SelectedLauncherPath;
                    var playniteDirectoryVariable = ExpandableVariables.PlayniteDirectory.ToString();
                    if (savedLauncherPath != "")
                    {
                        if (savedLauncherPath.Contains(playniteDirectoryVariable))
                        {
                            var playniteAPI = API.Instance;
                            savedLauncherPath = savedLauncherPath.Replace(playniteDirectoryVariable, playniteAPI.Paths.ApplicationPath);
                        }
                        if (Directory.Exists(savedLauncherPath))
                        {
                            launcherPath = savedLauncherPath;
                        }
                    }
                }
                if (!File.Exists(launcherPath))
                {
                    launcherPath = "";
                }
                return launcherPath;
            }
        }

        public static bool IsRunning
        {
            get
            {
                // The Notifications Renderer process is used because other Galaxy related process can
                // be running in the background without the client itself being open for the user
                return Process.GetProcessesByName("GOG Galaxy Notifications Renderer")?.Any() == true;
            }
        }

        public static string ClientInstallationPath => InstallationPath;
        public static string Icon => Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), @"Resources\gogicon.png");
    }
}
