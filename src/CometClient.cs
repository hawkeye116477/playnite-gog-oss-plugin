using Playnite.Common;
using Playnite.SDK;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace GogOssLibraryNS
{
    public class CometClient : LibraryClient
    {
        private static readonly ILogger logger = LogManager.GetLogger();

        public override bool IsInstalled => Comet.IsInstalled;

        public override void Open()
        {
            Comet.StartClient();
        }

        public override void Shutdown()
        {
        }
    }
}
