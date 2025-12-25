using System.Collections.Generic;

namespace GogOssLibraryNS.Models
{
    public class PagedResponse<T>
    {
        public class Embedded
        {
            public List<T> items;
        }

        public int page;
        public int pages;
        public int total;
        public int limit;
        public Embedded _embedded;
    }
}
