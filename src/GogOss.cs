using CliWrap;
using GogOssLibraryNS.Models;
using Playnite.SDK;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
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

        public static async Task LaunchIsi(Installed installedGameInfo, string gameId)
        {
            var isiInstallPath = Path.Combine(Gogdl.DependenciesInstallationPath, "_redist", "ISI");
            if (isiInstallPath != "" && Directory.Exists(isiInstallPath))
            {
                var metaManifest = Gogdl.GetGameMetaManifest(gameId);
                var shortLang = installedGameInfo.language.Split('-')[0];
                var langInEnglish = "";
                if (!shortLang.IsNullOrEmpty())
                {
                    langInEnglish = new CultureInfo(shortLang).EnglishName;
                }
                else
                {
                    langInEnglish = "English";
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

        public static async Task CompleteInstallation(string gameId)
        {
            var installedInfo = GetInstalledInfo(gameId);
            var metaManifest = Gogdl.GetGameMetaManifest(gameId);
            if (metaManifest.version == 1)
            {
                if (metaManifest.product.support_commands.Count > 0)
                {
                    foreach (var support_command in metaManifest.product.support_commands)
                    {
                        if (!support_command.executable.IsNullOrEmpty())
                        {
                            var playniteAPI = API.Instance;
                            var supportPath = Path.Combine(installedInfo.install_path, "gog-support", support_command.gameID);
                            var supportExe = Path.GetFullPath(Path.Combine(supportPath, support_command.executable.TrimStart(["/"]));
                            var supportArgs = new List<string>
                            {
                                "/VERYSILENT",
                                $"/DIR={installedInfo.install_path}",
                                $"/ProductId={gameId}",
                                "/galaxyclient",
                                $"/buildId={metaManifest.product.timestamp}",
                                $"/versionName={installedInfo.version}",
                                "/nodesktopshortcut",
                                "/nodesktopshorctut", // Yes, they made a typo
                            };
                            var shortLang = installedInfo.language.Split('-')[0];
                            var langInEnglish = "";
                            if (!shortLang.IsNullOrEmpty())
                            {
                                langInEnglish = new CultureInfo(shortLang).EnglishName;
                            }
                            else
                            {
                                langInEnglish = "English";
                            }
                            if (!langInEnglish.IsNullOrEmpty())
                            {
                                supportArgs.AddRange(new[] {
                                            $"/Language={langInEnglish}",
                                            $"/LANG={langInEnglish}",
                                            $"/lang-code={installedInfo.language}" });
                            }
                            if (File.Exists(supportExe))
                            {
                                await Cli.Wrap(supportExe)
                                         .WithArguments(supportArgs)
                                         .WithWorkingDirectory(supportPath)
                                         .AddCommandToLog()
                                         .ExecuteAsync();
                            }
                        }
                    }
                }
            }
            else if (metaManifest.scriptInterpreter)
            {
                await LaunchIsi(installedInfo, gameId);
            }
            else
            {
                var product = metaManifest.products.FirstOrDefault(i => i.productId == gameId);
                if (product != null && !product.temp_executable.IsNullOrEmpty())
                {
                    var supportPath = Path.Combine(installedInfo.install_path, "gog-support", gameId);
                    var tempExe = Path.GetFullPath(Path.Combine(supportPath, product.temp_executable.TrimStart(["/"]));
                    var tempArgs = new List<string>
                    {
                        "/VERYSILENT",
                        $"/DIR={installedInfo.install_path}",
                        $"/ProductId={gameId}",
                        "/galaxyclient",
                        $"/buildId={installedInfo.build_id}",
                        $"/versionName={installedInfo.version}",
                        "/nodesktopshortcut",
                        "/nodesktopshorctut", // Yes, they made a typo
                   };
                    var shortLang = installedInfo.language.Split('-')[0];
                    var langInEnglish = "";
                    if (!shortLang.IsNullOrEmpty())
                    {
                        langInEnglish = new CultureInfo(shortLang).EnglishName;
                    }
                    else
                    {
                        langInEnglish = "English";
                    }
                    if (!langInEnglish.IsNullOrEmpty())
                    {
                        tempArgs.AddRange(new[] {
                                            $"/Language={langInEnglish}",
                                            $"/LANG={langInEnglish}",
                                            $"/lang-code={installedInfo.language}" });
                    }
                    if (File.Exists(tempExe))
                    {
                        await Cli.Wrap(tempExe)
                                 .WithArguments(tempArgs)
                                 .WithWorkingDirectory(supportPath)
                                 .AddCommandToLog()
                                 .ExecuteAsync();
                    }
                }
            }
        }
    }
}
