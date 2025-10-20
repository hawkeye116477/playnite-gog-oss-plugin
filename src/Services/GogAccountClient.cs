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

namespace GogOssLibraryNS.Services
{
    public class GogAccountClient
    {
        private ILogger logger = LogManager.GetLogger();
        private IWebView webView;
        private readonly string clientId = "46899977096215655";
        private string clientSecret = "9d85c43b1482497dbbce61f6e4aa173a433796eeae2ca8c5f6129f2dc4de46d9";
        private readonly string tokenUrl = "https://auth.gog.com/token?";
        private readonly HttpClient httpClient = new HttpClient();

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
                    var responseJson = Serialization.FromJson<TokenResponse.TokenResponsePart>(responseContent);
                    DateTimeOffset now = DateTime.UtcNow;
                    responseJson.loginTime = now.ToUnixTimeSeconds();
                    var tokenResponse = new TokenResponse
                    {
                        client_id = new Dictionary<string, TokenResponse.TokenResponsePart>()
                    };
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
                var responseJson = Serialization.FromJson<TokenResponse.TokenResponsePart>(responseContent);
                DateTimeOffset now = DateTime.UtcNow;
                responseJson.loginTime = now.ToUnixTimeSeconds();
                var tokenResponse = new TokenResponse
                {
                    client_id = new Dictionary<string, TokenResponse.TokenResponsePart>()
                };
                tokenResponse.client_id.Add(clientId, responseJson);
                var strConf = Serialization.ToJson(tokenResponse.client_id, false);
                var tokenFullPath = Path.GetDirectoryName(GogOss.TokensPath);
                if (!Directory.Exists(tokenFullPath))
                {
                    FileSystem.CreateDirectory(tokenFullPath);
                }
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
            var accountInfo = new AccountBasicResponse();
            var tokens = LoadTokens();
            if (tokens == null)
            {
                return accountInfo;
            }

            var tokenLastUpdateTime = File.GetLastWriteTimeUtc(GogOss.TokensPath);
            var tokenExpirySeconds = tokens.expires_in;
            DateTime tokenExpiryTime = tokenLastUpdateTime.AddSeconds(tokenExpirySeconds);
            if (DateTime.UtcNow > tokenExpiryTime)
            {
                var renewSuccess = await RenewTokens(tokens.refresh_token);
                if (renewSuccess)
                {
                    tokens = LoadTokens();
                }
                else
                {
                    return accountInfo;
                }
            }

            httpClient.DefaultRequestHeaders.Clear();
            httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {tokens.access_token}");
            try
            {
                var response = await httpClient.GetAsync(@"https://menu.gog.com/v1/account/basic");
                if (!response.IsSuccessStatusCode)
                {
                    if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                    {
                        var renewSuccess = await RenewTokens(tokens.refresh_token);
                        if (renewSuccess)
                        {
                            return await GetAccountInfo();
                        }
                    }
                }
                var stringInfo = await response.Content.ReadAsStringAsync();
                accountInfo = Serialization.FromJson<AccountBasicResponse>(stringInfo);
            }
            catch (Exception ex)
            {
                logger.Debug("Can't get GOG account info");
                logger.Debug(ex.Message);
            }
            return accountInfo;
        }

        public TokenResponse.TokenResponsePart LoadTokens()
        {
            if (File.Exists(GogOss.TokensPath))
            {
                try
                {
                    var jsonResponse = Serialization.FromJson<Dictionary<string, TokenResponse.TokenResponsePart>>(File.ReadAllText(GogOss.TokensPath));
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

        public async Task<List<LibraryGameResponse>> GetOwnedGames(AccountBasicResponse account)
        {
            var baseUrl = @"https://www.gog.com/u/{0}/games/stats?sort=recent_playtime&order=desc&page={1}";
            var stringLibContent = string.Empty;
            var games = new List<LibraryGameResponse>();

            var tokens = LoadTokens();
            httpClient.DefaultRequestHeaders.Clear();
            httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {tokens.access_token}");
            try
            {
                var url = string.Format(baseUrl, account.username, 1);
                var response = await httpClient.GetAsync(url);
                if (response.IsSuccessStatusCode)
                {
                    stringLibContent = await response.Content.ReadAsStringAsync();
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
                            response = await httpClient.GetAsync(url);
                            if (response.IsSuccessStatusCode)
                            {
                                stringLibContent = await response.Content.ReadAsStringAsync();
                                var pageData = Serialization.FromJson<PagedResponse<LibraryGameResponse>>(stringLibContent);
                                games.AddRange(pageData._embedded.items);
                            }
                        }
                    }
                }
                return games;
            }
            catch (Exception e)
            {
                logger.Error(e, $"Failed to library from new API for account {account.username}, falling back to legacy.");
                logger.Debug(stringLibContent);
                return await GetOwnedGames();
            }
        }

        public async Task<List<LibraryGameResponse>> GetOwnedGames()
        {
            var games = new List<LibraryGameResponse>();
            var baseUrl = @"https://www.gog.com/account/getFilteredProducts?hiddenFlag=0&mediaType=1&page={0}&sortBy=title";

            var tokens = LoadTokens();
            httpClient.DefaultRequestHeaders.Clear();
            httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {tokens.access_token}");
            var response = await httpClient.GetAsync(string.Format(baseUrl, 1));

            if (response.IsSuccessStatusCode)
            {
                var gamesList = await response.Content.ReadAsStringAsync();
                var libraryData = new GetOwnedGamesResult();
                if (!gamesList.IsNullOrWhiteSpace() && Serialization.TryFromJson(gamesList, out GetOwnedGamesResult newLibraryData))
                {
                    libraryData = newLibraryData;
                }
                else
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
                        response = await httpClient.GetAsync(string.Format(baseUrl, i));
                        if (response.IsSuccessStatusCode)
                        {
                            gamesList = await response.Content.ReadAsStringAsync();
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
                }
            }
            return games;
        }

        public async Task<List<int>> GetOwnedIds()
        {
            var ownedItems = new GogOwned();
            var ownedList = ownedItems.owned;
            if (await GetIsUserLoggedIn())
            {
                var tokens = LoadTokens();
                GogDownloadApi.Client.DefaultRequestHeaders.Clear();
                GogDownloadApi.Client.DefaultRequestHeaders.Add("Authorization", "Bearer " + tokens.access_token);
                GogDownloadApi.Client.DefaultRequestHeaders.Add("User-Agent", GogDownloadApi.UserAgent);
                var response = await GogDownloadApi.Client.GetAsync(@"https://embed.gog.com/user/data/games");
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    if (!string.IsNullOrWhiteSpace(content))
                    {
                        var responseJson = Serialization.FromJson<GogOwned>(content);
                        if (responseJson.owned != null && responseJson.owned.Count > 0)
                        {
                            ownedList = responseJson.owned;
                        }
                    }
                }
            }
            return ownedList;
        }
    }
}
