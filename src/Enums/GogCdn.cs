using CommonPlugin;
using System;
using System.Collections.Generic;

namespace GogOssLibraryNS.Enums
{
    public enum GogCdn
    {
        Akamai,
        Fastly,
        Auto
    }

    public class PreferredCdn
    {
        public static Dictionary<GogCdn, string> GetCdnDict()
        {
            var preferredCdnActions = new Dictionary<GogCdn, string>
            {
                { GogCdn.Akamai, "Akamai" },
                { GogCdn.Fastly, "Fastly" },
                { GogCdn.Auto, LocalizationManager.Instance.GetString(LOC.ThirdPartyPlayniteAutomatic) },
            };
            return preferredCdnActions;
        }
    }
}
