using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CometLibrary.Models
{
    public class TokenResponse
    {
        public string access_token;
        public int expires_in;
        public string token_type;
        public string scope;
        public string session_id;
        public string refresh_token;
        public string user_id;
    }
}
