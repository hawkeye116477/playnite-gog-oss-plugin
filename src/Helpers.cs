using ByteSizeLib;
using Playnite.SDK;
using SharpCompress.Compressors;
using SharpCompress.Compressors.Deflate;
using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace GogOssLibraryNS
{
    public class Helpers
    {
        public static string GetMD5(byte[] bytes)
        {
            using (var md5 = MD5.Create())
            {
                return BitConverter.ToString(md5.ComputeHash(bytes)).Replace("-", "");
            }
        }

        public static string GetMD5(string filePath, IProgress<int> progress = null)
        {
            using (var stream = new FileStream(filePath,
                                               FileMode.Open,
                                               FileAccess.Read,
                                               FileShare.Read,
                                               bufferSize: 1 * 1024 * 1024,
                                               options: FileOptions.SequentialScan))
            using (var progressStream = new ProgressStream.ProgressStream(stream, progress))
            using (var md5 = MD5.Create())
            {
                var hash = md5.ComputeHash(progressStream);
                return BitConverter.ToString(hash).Replace("-", "");
            }
        }

        public static string GetSHA256(string filePath, IProgress<int> progress = null)
        {
            using (var stream = new FileStream(filePath,
                                               FileMode.Open,
                                               FileAccess.Read,
                                               FileShare.Read,
                                               bufferSize: 1 * 1024 * 1024,
                                               options: FileOptions.SequentialScan))
            using (var progressStream = new ProgressStream.ProgressStream(stream, progress))
            using (var sha256 = SHA256.Create())
            {
                var hash = sha256.ComputeHash(progressStream);
                return BitConverter.ToString(hash).Replace("-", "");
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

        public static async Task<string> DecompressZlib(Stream content)
        {
            var logger = LogManager.GetLogger();
            try
            {
                using var zlibStream = new ZlibStream(content, CompressionMode.Decompress);
                using var streamReader = new StreamReader(zlibStream);
                var result = await streamReader.ReadToEndAsync();
                return result;
            }
            catch (Exception ex)
            {
                logger.Error($"[GOG OSS] An error occurred while decompressing data: {ex}.");
                return string.Empty;
            }
        }

        public static double StringSizeToBytes(string size)
        {
            return ByteSize.Parse($"{size}").Bytes;
        }
    }
}
