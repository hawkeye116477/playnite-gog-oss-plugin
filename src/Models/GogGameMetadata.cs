using Playnite.SDK.Models;

namespace GogOssLibraryNS.Models
{
    public class GogGameMetadata : GameMetadata
    {
        public ProductApiDetail GameDetails { get; set; }
        public StorePageResult.ProductDetails StoreDetails { get; set; }
    }
}
