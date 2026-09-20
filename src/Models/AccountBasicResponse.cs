namespace GogOssLibraryNS.Models
{
    public class AccountBasicResponse
    {
        public string accessToken;
        public int accessTokenExpires;
        public string avatar;
        public int cacheExpires;
        public string clientId;
        public bool isLoggedIn = false;
        public string userId;
        public string username;
    }
}