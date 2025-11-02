using System.Collections.Generic;

namespace GogOssLibraryNS.Enums
{
    public enum GogCdn
    {
        Akamai,
        Fastly
    }

    public class PreferredCdn
    {
        public static Dictionary<GogCdn, string> GetCdnDict()
        {
            var preferredCdnActions = new Dictionary<GogCdn, string>
            {
                { GogCdn.Akamai, "Akamai" },
                { GogCdn.Fastly, "Fastly" },
            };
            return preferredCdnActions;
        }
    }
}
