using System.Diagnostics;
using System.Reflection;
using Playnite.SDK;

namespace GogOssLibraryNS
{
    public class GogOssTroubleshootingInformation
    {
        public static string PlayniteVersion
        {
            get
            {
                var playniteAPI = API.Instance;
                return playniteAPI.ApplicationInfo.ApplicationVersion.ToString();
            }
        }

        public string PluginVersion
        {
            get
            {
                Assembly assembly = Assembly.GetExecutingAssembly();
                FileVersionInfo fvi = FileVersionInfo.GetVersionInfo(assembly.Location);
                return fvi.FileVersion;
            }
        }

        public string CometVersion { get; set; } = "";
        public string GogdlVersion { get; set; } = "";
        public string CometBinary => Comet.ClientExecPath;
        public string GamesInstallationPath => GogOss.GamesInstallationPath;
    }
}