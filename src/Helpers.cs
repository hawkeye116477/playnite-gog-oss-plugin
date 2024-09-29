using ByteSizeLib;
using GogOssLibraryNS.Models;
using Playnite.SDK;
using Playnite.SDK.Data;
using System;
using System.Globalization;
using System.IO;

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
    }
}
