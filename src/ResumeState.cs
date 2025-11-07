using Playnite.SDK;
using Playnite.SDK.Data;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GogOssLibraryNS
{
    public class ResumeState
    {
        public class MainObjects
        {
            public ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> Files { get; set; } =
                new ConcurrentDictionary<string, ConcurrentDictionary<string, byte>>(StringComparer.OrdinalIgnoreCase);

            public DateTime LastUpdatedUtc { get; set; } = DateTime.UtcNow;
        }

        private readonly ILogger logger = LogManager.GetLogger();

        public MainObjects State { get; private set; } = new MainObjects();

        public void MarkCompleted(string filePath, string chunkId)
        {
            if (chunkId == null) chunkId = "";

            var set = State.Files.GetOrAdd(filePath, _ =>
                new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase));

            set.TryAdd(chunkId, 0);
        }

        public bool IsCompleted(string filePath, string chunkId)
        {
            if (string.IsNullOrEmpty(chunkId))
            {
                return false;
            }

            if (State.Files.TryGetValue(filePath, out var set))
            {
                return set.ContainsKey(chunkId);
            }

            return false;
        }

        public void Save(string resumeStatePath)
        {
            try
            {
                State.LastUpdatedUtc = DateTime.UtcNow;
                var json = Serialization.ToJson(State);

                var tmp = resumeStatePath + ".tmp";
                File.WriteAllText(tmp, json);
                try
                {
                    if (File.Exists(resumeStatePath))
                    {
                        File.Replace(tmp, resumeStatePath, null);
                    }
                    else
                    {
                        File.Move(tmp, resumeStatePath);
                    }
                }
                catch
                {
                    File.Copy(tmp, resumeStatePath, true);
                    try
                    {
                        File.Delete(tmp);
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                logger.Warn($"Failed to persist resume state: {ex.Message}");
            }
        }

        public void Load(string resumeStatePath)
        {
            try
            {
                if (!File.Exists(resumeStatePath))
                {
                    return;
                }

                var json = File.ReadAllText(resumeStatePath);

                var loaded = Serialization.FromJson<MainObjects>(json);

                if (loaded == null || loaded.Files == null || loaded.Files.Count == 0)
                {
                    return;
                }

                State = loaded;
            }
            catch (Exception ex)
            {
                logger.Warn($"Failed to read resume state: {ex.Message}");
            }
        }
    }
}
