using CliWrap;
using GogOssLibraryNS.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;

namespace GogOssLibraryNS
{
    public class GogOss
    {
        public const string EnStoreLocaleString = "US_USD_en-US";
        public static string TokensPath = Path.Combine(GogOssLibrary.Instance.GetPluginUserDataPath(), "tokens.json");
        public static string Icon => Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), @"Resources\gogicon.png");
        public static string UserAgent => @"Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/128.0.0.0 Safari/537.36";

        public static Installed GetInstalledInfo(string gameId)
        {
            var installedAppList = GogOssLibrary.GetInstalledAppList();
            var installedInfo = new Installed();
            if (installedAppList.ContainsKey(gameId))
            {
                installedInfo = installedAppList[gameId];
            }
            return installedInfo;
        }

        public static string IsiInstallPath
        {
            get
            {
                var isiInstallPath = "";
                var isiInstalledInfo = GetInstalledInfo("ISI");
                if (isiInstalledInfo.install_path != "")
                {
                    isiInstallPath = Path.Combine(isiInstalledInfo.install_path);
                }
                return isiInstallPath;
            }
        }

        public static async Task LaunchIsi(Installed installedGameInfo, string gameId)
        {
            var isiInstallPath = IsiInstallPath;
            if (isiInstallPath != "" && Directory.Exists(isiInstallPath))
            {
                var metaManifest = Gogdl.GetGameMetaManifest(gameId);
                var shortLang = installedGameInfo.language.Split('-')[0];
                var langInEnglish = "";
                if (!shortLang.IsNullOrEmpty())
                {
                    langInEnglish = new CultureInfo(shortLang).EnglishName;
                }
                foreach (var product in metaManifest.products)
                {
                    var args = new List<string>
                    {
                        "/VERYSILENT",
                        $"/DIR={installedGameInfo.install_path}",
                        $"/ProductId={product.productId}",
                        "/galaxyclient",
                        $"/buildId={installedGameInfo.build_id}",
                        $"/versionName={installedGameInfo.version}",
                        "/nodesktopshortcut",
                        "/nodesktopshorctut", // Yes, they made a typo
                    };
                    if (!langInEnglish.IsNullOrEmpty())
                    {
                        args.AddRange(new[] {
                                    $"/Language={langInEnglish}",
                                    $"/LANG={langInEnglish}",
                                    $"/lang-code={installedGameInfo.language}" });
                    }
                    var isiExe = Path.Combine(isiInstallPath, "scriptinterpreter.exe");
                    if (File.Exists(isiExe))
                    {
                        await Cli.Wrap(isiExe)
                                 .WithArguments(args)
                                 .WithWorkingDirectory(isiInstallPath)
                                 .AddCommandToLog()
                                 .ExecuteAsync();
                    }
                }

            }
        }
    }
}
