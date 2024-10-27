﻿using ByteSizeLib;
using Playnite.SDK;
using Playnite.SDK.Data;
using SharpCompress.Compressors;
using SharpCompress.Compressors.Deflate;
using SharpCompress.IO;
using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace GogOssLibraryNS
{
    public class Helpers
    {
        public static string FormatSize(double size, string unit = "B", bool toBits = false)
        {
            if (toBits)
            {
                size *= 8;
            }
            var finalSize = ByteSize.Parse($"{size} {unit}").ToBinaryString();
            if (toBits)
            {
                finalSize = finalSize.Replace("B", "b");
            }
            return finalSize.Replace("i", "");
        }

        public static double ToBytes(double size, string unit)
        {
            return ByteSize.Parse($"{size} {unit}").Bytes;
        }

        public static int CpuThreadsNumber
        {
            get
            {
                return Environment.ProcessorCount;
            }
        }

        public static void SaveJsonSettingsToFile(object jsonSettings, string fileName, string subDir = "")
        {
            var strConf = Serialization.ToJson(jsonSettings, true);
            if (!strConf.IsNullOrEmpty())
            {
                var dataDir = GogOssLibrary.Instance.GetPluginUserDataPath();
                if (!subDir.IsNullOrEmpty())
                {
                    dataDir = Path.Combine(dataDir, subDir);
                }
                if (!Directory.Exists(dataDir))
                {
                    Directory.CreateDirectory(dataDir);
                }
                var dataFile = Path.Combine(dataDir, $"{fileName}.json");
                File.WriteAllText(dataFile, strConf);
            }
        }

        public static double GetDouble(string value)
        {
            double result;

            // Try parsing in the current culture
            if (!double.TryParse(value, NumberStyles.Any, CultureInfo.CurrentCulture, out result) &&
                // Then try in US english
                !double.TryParse(value, NumberStyles.Any, CultureInfo.GetCultureInfo("en-US"), out result) &&
                // Then in neutral language
                !double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out result))
            {
                result = 0;
            }

            return result;
        }

        public static bool IsFileLocked(string filePath)
        {
            try
            {
                using FileStream inputStream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.None);
                inputStream.Close();
            }
            catch (Exception)
            {
                return true;
            }
            return false;
        }

        public static bool IsDirectoryLocked(string path)
        {
            bool locked = false;
            foreach (var file in Directory.GetFiles(path, "*.*", SearchOption.AllDirectories))
            {
                if (IsFileLocked(file))
                {
                    locked = true;
                }
            }
            return locked;
        }

        public static bool IsDirectoryWritable(string folderPath)
        {
            try
            {
                using (FileStream fs = File.Create(Path.Combine(folderPath, Path.GetRandomFileName()),
                                                   1,
                                                   FileOptions.DeleteOnClose)
                )
                { }
                return true;
            }
            catch (UnauthorizedAccessException)
            {
                var playniteAPI = API.Instance;
                playniteAPI.Dialogs.ShowErrorMessage(LOC.GogOssPermissionError);
                return false;
            }
            catch (Exception ex)
            {
                var logger = LogManager.GetLogger();
                logger.Error($"An error occured during checking if directory {folderPath} is writable: {ex.Message}");
                return true;
            }
        }

        public static string GetMD5(byte[] bytes)
        {
            using (var md5 = System.Security.Cryptography.MD5.Create())
            {
                return BitConverter.ToString(md5.ComputeHash(bytes)).Replace("-", "");
            }
        }

        /// <summary>
        /// Returns a relative path string from a full path based on a base path
        /// provided.
        /// Found on https://weblog.west-wind.com/posts/2010/Dec/20/Finding-a-Relative-Path-in-NET and improved a little
        /// </summary>
        /// <param name="basePath">The base path on which relative processing is based. Should be a directory.</param>
        /// <param name="fullPath">The path to convert. Can be either a file or a directory</param>
        /// <returns>
        /// String of the relative path.
        /// 
        /// Examples of returned values:
        ///  test.txt, ..\test.txt, ..\..\..\test.txt, ., .., subdir\test.txt
        /// </returns>
        public static string GetRelativePath(string basePath, string fullPath)
        {
            // Require trailing backslash for path
            if (!basePath.EndsWith("\\"))
                basePath += "\\";

            Uri baseUri = new Uri(basePath);
            Uri fullUri = new Uri(fullPath);

            Uri relativeUri = baseUri.MakeRelativeUri(fullUri);

            // Uri's use forward slashes so convert back to backward slashes
            // Uri's also escape some chars, co convert back to unescaped format
            return Uri.UnescapeDataString(relativeUri.ToString().Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar));
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


        public static async Task<Stream> DecompressZlibToStream(Stream content)
        {
            using var outputStream = new MemoryStream();
            using (var zlibStream = new ZlibStream(new NonDisposingStream(content), CompressionMode.Decompress))
            {
                zlibStream.Position = 0;
                await zlibStream.CopyToAsync(outputStream);
                //zlibStream.Dispose();
            }
            return outputStream;
        }

        public static byte[] DecompressZlibToByte(byte[] bytes)
        {
            using (var memoryStream = new MemoryStream(bytes))
            {
                using (var outputStream = new MemoryStream())
                {
                    using (var zlibStream = new ZlibStream(new NonDisposingStream(memoryStream), CompressionMode.Decompress))
                    {
                        zlibStream.CopyTo(outputStream);
                    }
                    return outputStream.ToArray();
                }
            }

        }

        public static string DecompressZlib(Stream content)
        {
            string result;
            using (var zlibStream = new ZlibStream(new NonDisposingStream(content), CompressionMode.Decompress))
            using (var streamReader = new StreamReader(zlibStream))
            {
                result = streamReader.ReadToEnd();
            }
            return result;
        }
    }
}
