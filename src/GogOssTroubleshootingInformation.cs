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
                System.Reflection.Assembly assembly = System.Reflection.Assembly.GetExecutingAssembly();
                System.Diagnostics.FileVersionInfo fvi = System.Diagnostics.FileVersionInfo.GetVersionInfo(assembly.Location);
                return fvi.FileVersion;
            }
        }
        public string CometVersion { get; set; } = "";
        public string GogdlVersion { get; set; } = "";
        public string CometBinary => Comet.ClientExecPath;
        public string GamesInstallationPath => GogOss.GamesInstallationPath;
        public string XdeltaBinary => Xdelta.InstallationPath;
    }
}
