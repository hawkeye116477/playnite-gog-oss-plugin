using ByteSizeLib;
using Playnite.SDK;
using SharpCompress.Compressors;
using SharpCompress.Compressors.Deflate;
using System;
using System.Buffers;
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
            var bufferSize = 512 * 1024;
            using var stream = new FileStream(filePath,
                                               FileMode.Open,
                                               FileAccess.Read,
                                               FileShare.Read,
                                               bufferSize: bufferSize,
                                               options: FileOptions.SequentialScan);
            using var md5 = MD5.Create();
            byte[] buffer = ArrayPool<byte>.Shared.Rent(bufferSize);
            try
            {
                int read;
                long total = 0;

                while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    md5.TransformBlock(buffer, 0, read, null, 0);
                    total += read;
                    progress?.Report(read);
                }
                md5.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                return BitConverter.ToString(md5.Hash).Replace("-", "");
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        public static string GetSHA256(string filePath, IProgress<int> progress = null)
        {
            var bufferSize = 512 * 1024;
            using var stream = new FileStream(filePath,
                                               FileMode.Open,
                                               FileAccess.Read,
                                               FileShare.Read,
                                               bufferSize: bufferSize,
                                               options: FileOptions.SequentialScan);
            using var sha256 = SHA256.Create();
            byte[] buffer = ArrayPool<byte>.Shared.Rent(bufferSize);
            try
            {
                int read;
                long total = 0;

                while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    sha256.TransformBlock(buffer, 0, read, null, 0);
                    total += read;
                    progress?.Report(read);
                }
                sha256.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                return BitConverter.ToString(sha256.Hash).Replace("-", "");
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
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
