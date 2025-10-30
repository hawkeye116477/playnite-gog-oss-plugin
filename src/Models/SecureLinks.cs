using System.Collections.Generic;

namespace GogOssLibraryNS.Models
{
    public class SecureLinks
    {
        public MainSecureLinks mainSecureLinks = new();
        public InGameDependsSecureLinks inGameDependsSecureLinks = new();
        public class MainSecureLinks
        {
            public List<string> secureLinks = new();
        }
        public class InGameDependsSecureLinks
        {
            public List<string> secureLinks = new();
        }
    }
}
