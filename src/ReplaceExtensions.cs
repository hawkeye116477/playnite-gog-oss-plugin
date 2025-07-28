using System;

namespace GogOssLibraryNS
{
    public static class ReplaceExtensions
    {
        public static string ReplaceFirst(this string source, string oldValue, string newValue, StringComparison comparisonType = StringComparison.Ordinal)
        {
            if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(oldValue))
            {
                return source;
            }

            int startIndex = source.IndexOf(oldValue, comparisonType);
            if (startIndex < 0)
            {
                return source;
            }

            return source.Substring(0, startIndex) + newValue + source.Substring(startIndex + oldValue.Length);
        }
    }
}
