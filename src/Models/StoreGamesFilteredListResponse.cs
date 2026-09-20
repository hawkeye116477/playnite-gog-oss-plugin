using System.Collections.Generic;

namespace GogOssLibraryNS.Models
{
    public class StoreGamesFilteredListResponse
    {
        public class Product
        {
            public string title;
            public string image;
            public string url;
            public string supportUrl;
            public string forumUrl;
            public bool isGame;
            public string slug;
            public uint id;
        }

        public List<Product> products;
    }
}