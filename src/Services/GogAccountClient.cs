using GogOssLibraryNS.Models;
using Playnite.Common;
using Playnite.SDK;
using Playnite.SDK.Data;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web;
using static GogOssLibraryNS.Models.TokenResponse;

namespace GogOssLibraryNS.Services
{
    public class GogAccountClient
    {
        private ILogger logger = LogManager.GetLogger();
        private IWebView webView;
        private readonly string clientId = "46899977096215655";
        private string clientSecret = "9d85c43b1482497dbbce61f6e4aa173a433796eeae2ca8c5f6129f2dc4de46d9";
        private readonly string tokenUrl = "https://auth.gog.com/token?";

        public GogAccountClient()
        {
        }

        public GogAccountClient(IWebView webView)
        {
            this.webView = webView;
        }

        public async Task<bool> GetIsUserLoggedIn()
        {
            var account = await GetAccountInfo();
            return account?.isLoggedIn ?? false;
        }

        public async Task Login()
        {
            var loggedIn = false;

            var loginUrlParams = new Dictionary<string, string>
            {
                { "client_id", clientId },
                { "layout", "galaxy" },
                { "redirect_uri", "https://embed.gog.com/on_login_success?origin=client" },
                { "response_type", "code" }
            };
            var loginUrl = FormatUrl(loginUrlParams, "https://auth.gog.com/auth?");
            var code = "";

            webView.LoadingChanged += (s, e) =>
            {
                var address = webView.GetCurrentAddress();
                if (address.StartsWith(loginUrlParams["redirect_uri"]))
                {
                    var redirectUri = new Uri(address);
                    code = HttpUtility.ParseQueryString(redirectUri.Query).Get("code");
                    if (code.IsNullOrEmpty())
                    {
                        logger.Error("Can't get auth code from GOG");
                        return;
                    }
                    loggedIn = true;
                    webView.Close();
                }
            };
            webView.DeleteDomainCookies(".gog.com");
            webView.Navigate(loginUrl);
            webView.OpenDialog();

            if (!loggedIn)
            {
                return;
            }
            else
            {
                using (var httpClient = new HttpClient())
                {
                    var urlParams = new Dictionary<string, string>
                    {
                        { "client_id", clientId },
                        { "client_secret", clientSecret },
                        { "grant_type", "authorization_code" },
                        { "redirect_uri", "https://embed.gog.com/on_login_success?origin=client" },
                        { "code", code }
                    };
                    var newTokenUrl = FormatUrl(urlParams, tokenUrl);
                    var response = await httpClient.GetAsync(newTokenUrl);
                    if (response.IsSuccessStatusCode)
                    {
                        var responseContent = await response.Content.ReadAsStringAsync();
                        var responseJson = Serialization.FromJson<TokenResponsePart>(responseContent);
                        DateTimeOffset now = DateTime.UtcNow;
                        responseJson.loginTime = now.ToUnixTimeSeconds();
                        var tokenResponse = new TokenResponse();
                        tokenResponse.client_id = new Dictionary<string, TokenResponsePart>();
                        tokenResponse.client_id.Add(clientId, responseJson);
                        var strConf = Serialization.ToJson(tokenResponse.client_id, false);
                        FileSystem.CreateDirectory(Path.GetDirectoryName(GogOss.TokensPath));
                        File.WriteAllText(GogOss.TokensPath, strConf);
                    }
                    else
                    {
                        logger.Error($"Failed to authenticate with GOG. Error: {response.ReasonPhrase}");
                    }
                }
            }
        }

        public static string FormatUrl(IDictionary<string, string> dict, string url)
        {
            var uriBuilder = new UriBuilder(url);
            var query = HttpUtility.ParseQueryString(uriBuilder.Query);
            foreach (var item in dict)
            {
                query.Add(item.Key, item.Value);
            }
            uriBuilder.Query = query.ToString();
            return uriBuilder.Uri.AbsoluteUri;
        }

        public async Task<bool> RenewTokens(string refreshToken)
        {
            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Clear();

            var urlParams = new Dictionary<string, string>
            {
                { "client_id", clientId },
                { "client_secret", clientSecret },
                { "grant_type", "refresh_token" },
                { "refresh_token", refreshToken }
            };
            var newTokenUrl = FormatUrl(urlParams, tokenUrl);
            var response = await httpClient.GetAsync(newTokenUrl);
            if (response.IsSuccessStatusCode)
            {
                var responseContent = await response.Content.ReadAsStringAsync();
                var responseJson = Serialization.FromJson<TokenResponsePart>(responseContent);
                DateTimeOffset now = DateTime.UtcNow;
                responseJson.loginTime = now.ToUnixTimeSeconds();
                var tokenResponse = new TokenResponse();
                tokenResponse.client_id = new Dictionary<string, TokenResponsePart>();
                tokenResponse.client_id.Add(clientId, responseJson);
                var strConf = Serialization.ToJson(tokenResponse.client_id, false);
                FileSystem.CreateDirectory(Path.GetDirectoryName(GogOss.TokensPath));
                File.WriteAllText(GogOss.TokensPath, strConf);
                return true;
            }
            else
            {
                logger.Error("Failed to renew tokens.");
                return false;
            }
        }

        public async Task<AccountBasicResponse> GetAccountInfo()
        {
            var tokens = LoadTokens();
            if (tokens == null)
            {
                return new AccountBasicResponse();
            }

            var tokenLastUpdateTime = File.GetLastWriteTime(GogOss.TokensPath);
            var tokenExpirySeconds = tokens.expires_in;
            DateTime tokenExpiryTime = tokenLastUpdateTime.AddSeconds(tokenExpirySeconds);

            if (DateTime.Now > tokenExpiryTime)
            {
                var renewSuccess = await RenewTokens(tokens.refresh_token);
                if (renewSuccess)
                {
                    tokens = LoadTokens();
                }
            }

            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Add("Authorization", "Bearer " + tokens.access_token);
            var response = await httpClient.GetAsync(@"https://menu.gog.com/v1/account/basic");
            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    var renewSuccess = await RenewTokens(tokens.refresh_token);
                    if (renewSuccess)
                    {
                        await GetAccountInfo();
                    }
                    else
                    {
                        logger.Debug("Can't get GOG account info");
                        return new AccountBasicResponse();
                    }
                }
                else
                {
                    logger.Debug("Can't get GOG account info");
                    return new AccountBasicResponse();
                }
            }
            var stringInfo = await response.Content.ReadAsStringAsync();
            var accountInfo = Serialization.FromJson<AccountBasicResponse>(stringInfo);
            return accountInfo;
        }

        public TokenResponsePart LoadTokens()
        {
            if (File.Exists(GogOss.TokensPath))
            {
                try
                {
                    var jsonResponse = Serialization.FromJson<Dictionary<string, TokenResponsePart>>(File.ReadAllText(GogOss.TokensPath));
                    var firstKey = jsonResponse.First().Value;
                    return firstKey;
                }
                catch (Exception e)
                {
                    logger.Error(e, "Failed to load saved tokens.");
                }

            }
            return null;
        }

        public List<LibraryGameResponse> GetOwnedGames(AccountBasicResponse account)
        {
            var baseUrl = @"https://www.gog.com/u/{0}/games/stats?sort=recent_playtime&order=desc&page={1}";
            var stringLibContent = string.Empty;
            var games = new List<LibraryGameResponse>();

            try
            {
                var url = string.Format(baseUrl, account.username, 1);
                webView.NavigateAndWait(url);
                stringLibContent = webView.GetPageText();
                var libraryData = Serialization.FromJson<PagedResponse<LibraryGameResponse>>(stringLibContent);
                if (libraryData == null)
                {
                    logger.Error("GOG library content is empty.");
                    return null;
                }

                games.AddRange(libraryData._embedded.items);
                if (libraryData.pages > 1)
                {
                    for (int i = 2; i <= libraryData.pages; i++)
                    {
                        webView.NavigateAndWait(string.Format(baseUrl, account.username, i));
                        stringLibContent = webView.GetPageText();
                        var pageData = Serialization.FromJson<PagedResponse<LibraryGameResponse>>(stringLibContent);
                        games.AddRange(pageData._embedded.items);
                    }
                }

                return games;
            }
            catch (Exception e)
            {
                logger.Error(e, $"Failed to library from new API for account {account.username}, falling back to legacy.");
                logger.Debug(stringLibContent);
                return GetOwnedGames();
            }
        }

        public List<LibraryGameResponse> GetOwnedGames()
        {
            var games = new List<LibraryGameResponse>();
            var baseUrl = @"https://www.gog.com/account/getFilteredProducts?hiddenFlag=0&mediaType=1&page={0}&sortBy=title";
            webView.NavigateAndWait(string.Format(baseUrl, 1));
            var gamesList = webView.GetPageText();

            var libraryData = Serialization.FromJson<GetOwnedGamesResult>(gamesList);
            if (libraryData == null)
            {
                logger.Error("GOG library content is empty.");
                return null;
            }

            games.AddRange(libraryData.products.Select(a => new LibraryGameResponse()
            {
                game = new LibraryGameResponse.Game()
                {
                    id = a.id.ToString(),
                    title = a.title,
                    url = a.url,
                    image = a.image
                }
            }));

            if (libraryData.totalPages > 1)
            {
                for (int i = 2; i <= libraryData.totalPages; i++)
                {
                    webView.NavigateAndWait(string.Format(baseUrl, i));
                    gamesList = webView.GetPageText();
                    var pageData = libraryData = Serialization.FromJson<GetOwnedGamesResult>(gamesList);
                    games.AddRange(pageData.products.Select(a => new LibraryGameResponse()
                    {
                        game = new LibraryGameResponse.Game()
                        {
                            id = a.id.ToString(),
                            title = a.title,
                            url = a.url,
                            image = a.image
                        }
                    }));
                }
            }

            return games;
        }
    }
}
