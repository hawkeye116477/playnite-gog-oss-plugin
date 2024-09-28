using System.Collections.Generic;

namespace CometLibraryNS.Models
{
    public class TokenResponse
    {
        public Dictionary<string, TokenResponsePart> client_id { get; set; }

        public class TokenResponsePart
        {
            public string access_token;
            public int expires_in;
            public string token_type;
            public string scope;
            public string session_id;
            public string refresh_token;
            public string user_id;
            public long loginTime;
        }
    }

}
