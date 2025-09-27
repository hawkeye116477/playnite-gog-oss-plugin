using SharpCompress.Compressors;
using SharpCompress.Compressors.Deflate;
using System;
using System.IO;

namespace GogOssLibraryNS
{
    public class Helpers
    {
        public static string GetMD5(byte[] bytes)
        {
            using (var md5 = System.Security.Cryptography.MD5.Create())
            {
                return BitConverter.ToString(md5.ComputeHash(bytes)).Replace("-", "");
            }
        }

        /// <summary>
        /// Found at https://stackoverflow.com/a/2132004
        /// </summary>
        public static string[] SplitArguments(string commandLine)
        {
            var parmChars = commandLine.ToCharArray();
            var inSingleQuote = false;
            var inDoubleQuote = false;
            for (var index = 0; index < parmChars.Length; index++)
            {
                if (parmChars[index] == '"' && !inSingleQuote)
                {
                    inDoubleQuote = !inDoubleQuote;
                    parmChars[index] = '\n';
                }
                if (parmChars[index] == '\'' && !inDoubleQuote)
                {
                    inSingleQuote = !inSingleQuote;
                    parmChars[index] = '\n';
                }
                if (!inSingleQuote && !inDoubleQuote && parmChars[index] == ' ')
                    parmChars[index] = '\n';
            }
            return new string(parmChars).Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
        }

        public static string DecompressZlib(Stream content)
        {
            using var zlibStream = new ZlibStream(content, CompressionMode.Decompress);
            using var streamReader = new StreamReader(zlibStream);
            var result = streamReader.ReadToEnd();
            return result;
        }
    }
}
