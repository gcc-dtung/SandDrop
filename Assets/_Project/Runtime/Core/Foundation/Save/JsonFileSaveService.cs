using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GameKit.Core.Contracts;
using UnityEngine;
using GameKit.Core.Foundation.Logging;

namespace GameKit.Core.Foundation.Save
{
    /// <summary>
    /// <see cref="ISaveService"/> stored as one JSON file.
    /// Writing: <c>save.json.tmp</c> → current file kept as <c>save.json.bak</c> → tmp renamed to <c>save.json</c>.
    /// Reading tries <c>save.json</c>, then <c>.tmp</c>, then <c>.bak</c>, so a crash in the middle of a write never loses the save.
    /// Pass <c>null</c> as directory for a memory-only instance (tests, or Edit mode tools that must not touch the real save).
    /// </summary>
    public sealed class JsonFileSaveService : ISaveService
    {
        public const string FileName = "save.json";

        [Serializable]
        private class Envelope
        {
            public int version;
            public List<Entry> entries = new List<Entry>();
        }

        [Serializable]
        private class Entry
        {
            public string key;
            public string json;
        }

        private readonly Dictionary<string, string> entries = new Dictionary<string, string>();
        private readonly string mainPath;
        private readonly string tmpPath;
        private readonly string bakPath;
        private readonly int currentVersion;

        /// <summary>Save format version after loading and migrating.</summary>
        public int Version { get; private set; }

        public bool IsDirty { get; private set; }

        private bool IsMemoryOnly => mainPath == null;

        /// <param name="directory">Folder for the save file, e.g. <c>Application.persistentDataPath/Save</c>; null = memory only.</param>
        /// <param name="currentVersion">Save format version the code expects.</param>
        /// <param name="migrations">One migration per version step below <paramref name="currentVersion"/>.</param>
        public JsonFileSaveService(string directory, int currentVersion = 1, IEnumerable<ISaveMigration> migrations = null)
        {
            this.currentVersion = currentVersion;

            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
                mainPath = Path.Combine(directory, FileName);
                tmpPath = mainPath + ".tmp";
                bakPath = mainPath + ".bak";
            }

            ReadFromDisk();
            RunMigrations(migrations);
        }

        public bool Has(string key) => entries.ContainsKey(key);

        public bool TryLoad<T>(string key, out T data)
        {
            if (entries.TryGetValue(key, out var json))
            {
                try
                {
                    data = JsonUtility.FromJson<T>(json);
                    if (data != null)
                    {
                        return true;
                    }
                }
                catch (Exception e)
                {
                    Log.Error("Save", $"Could not parse '{key}' as {typeof(T).Name}: {e.Message}");
                }
            }

            data = default;
            return false;
        }

        public T Load<T>(string key) where T : new()
        {
            return TryLoad(key, out T data) ? data : new T();
        }

        public void Save<T>(string key, T data)
        {
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data), $"Use Delete(\"{key}\") to remove a save entry.");
            }

            entries[key] = JsonUtility.ToJson(data);
            IsDirty = true;
        }

        public void Delete(string key)
        {
            if (entries.Remove(key))
            {
                IsDirty = true;
            }
        }

        public void DeleteAll()
        {
            entries.Clear();
            IsDirty = true;
        }

        public void Flush()
        {
            if (!IsDirty)
            {
                return;
            }

            if (IsMemoryOnly)
            {
                IsDirty = false;
                return;
            }

            var envelope = new Envelope { version = Version };
            foreach (var pair in entries.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                envelope.entries.Add(new Entry { key = pair.Key, json = pair.Value });
            }

            File.WriteAllText(tmpPath, JsonUtility.ToJson(envelope, true));
            if (File.Exists(mainPath))
            {
                if (File.Exists(bakPath))
                {
                    File.Delete(bakPath);
                }
                File.Move(mainPath, bakPath);
            }
            File.Move(tmpPath, mainPath);

            IsDirty = false;
        }

        private void ReadFromDisk()
        {
            Version = currentVersion;
            if (IsMemoryOnly)
            {
                return;
            }

            foreach (var path in new[] { mainPath, tmpPath, bakPath })
            {
                if (!File.Exists(path))
                {
                    continue;
                }

                try
                {
                    var envelope = JsonUtility.FromJson<Envelope>(File.ReadAllText(path));
                    if (envelope?.entries == null)
                    {
                        Log.Warning("Save", $"Could not read {Path.GetFileName(path)}: empty file.");
                        continue;
                    }

                    entries.Clear();
                    foreach (var entry in envelope.entries)
                    {
                        if (!string.IsNullOrEmpty(entry.key))
                        {
                            entries[entry.key] = entry.json;
                        }
                    }

                    Version = envelope.version;
                    if (path != mainPath)
                    {
                        Log.Warning("Save", $"Recovered save from {Path.GetFileName(path)}.");
                        IsDirty = true; // rewrite save.json on the next Flush
                    }
                    return;
                }
                catch (Exception e)
                {
                    Log.Warning("Save", $"Could not read {Path.GetFileName(path)}: {e.Message}");
                }
            }
        }

        private void RunMigrations(IEnumerable<ISaveMigration> migrations)
        {
            if (Version > currentVersion)
            {
                Log.Warning("Save", $"Save version {Version} is newer than the game ({currentVersion}); loading it as is.");
                return;
            }

            if (Version == currentVersion)
            {
                return;
            }

            var byVersion = new Dictionary<int, ISaveMigration>();
            if (migrations != null)
            {
                foreach (var migration in migrations)
                {
                    byVersion[migration.FromVersion] = migration;
                }
            }

            while (Version < currentVersion)
            {
                if (!byVersion.TryGetValue(Version, out var migration))
                {
                    Log.Error("Save", $"No migration from version {Version}; save stays at version {Version}.");
                    return;
                }

                migration.Migrate(entries);
                Version++;
                IsDirty = true;
            }
        }
    }
}
