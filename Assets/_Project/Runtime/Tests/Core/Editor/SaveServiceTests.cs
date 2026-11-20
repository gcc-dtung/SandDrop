using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using GameKit.Core.Contracts;
using GameKit.Core.Foundation.Save;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace GameKit.Tests.Core
{
    public class SaveServiceTests
    {
        [Serializable]
        private class Progress
        {
            public const string Key = "progress";
            public int level = 1;
            public int coins;
        }

        private string directory;

        [SetUp]
        public void CreateTempDirectory()
        {
            // Never the real persistentDataPath: tests must not touch the player's save.
            directory = Path.Combine(Path.GetTempPath(), "GameKitSaveTests_" + Guid.NewGuid().ToString("N"));
        }

        [TearDown]
        public void DeleteTempDirectory()
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }

        private string MainFile => Path.Combine(directory, JsonFileSaveService.FileName);

        [Test]
        public void MemoryOnly_RoundTrip_AndNeverTouchesDisk()
        {
            var save = new JsonFileSaveService(null);
            save.Save(Progress.Key, new Progress { level = 4, coins = 20 });
            save.Flush();

            Assert.IsTrue(save.TryLoad<Progress>(Progress.Key, out var loaded));
            Assert.AreEqual(4, loaded.level);
            Assert.AreEqual(20, loaded.coins);
            Assert.IsFalse(save.IsDirty);
            Assert.IsFalse(Directory.Exists(directory));
        }

        [Test]
        public void Load_MissingKey_ReturnsNewInstanceWithDefaults()
        {
            var save = new JsonFileSaveService(null);

            var loaded = save.Load<Progress>(Progress.Key);

            Assert.AreEqual(1, loaded.level);
            Assert.IsFalse(save.Has(Progress.Key));
        }

        [Test]
        public void Delete_RemovesKey_AndMarksDirty()
        {
            var save = new JsonFileSaveService(null);
            save.Save(Progress.Key, new Progress());
            save.Flush();

            save.Delete(Progress.Key);

            Assert.IsFalse(save.Has(Progress.Key));
            Assert.IsTrue(save.IsDirty);
        }

        [Test]
        public void Flush_ThenNewInstance_ReadsSameData()
        {
            var first = new JsonFileSaveService(directory);
            first.Save(Progress.Key, new Progress { level = 9, coins = 300 });
            first.Flush();

            var second = new JsonFileSaveService(directory);

            Assert.IsTrue(File.Exists(MainFile));
            Assert.AreEqual(9, second.Load<Progress>(Progress.Key).level);
            Assert.AreEqual(300, second.Load<Progress>(Progress.Key).coins);
            Assert.IsFalse(second.IsDirty);
        }

        [Test]
        public void Flush_WhenNothingChanged_DoesNotWrite()
        {
            var save = new JsonFileSaveService(directory);
            save.Flush();

            Assert.IsFalse(File.Exists(MainFile));
        }

        [Test]
        public void CorruptMainFile_RecoversFromBackup()
        {
            var save = new JsonFileSaveService(directory);
            save.Save(Progress.Key, new Progress { level = 2 });
            save.Flush();                                   // save.json = level 2
            save.Save(Progress.Key, new Progress { level = 3 });
            save.Flush();                                   // save.json = level 3, .bak = level 2

            File.WriteAllText(MainFile, "{ this is not json");

            LogAssert.Expect(LogType.Warning, new Regex(@"Could not read save\.json"));
            LogAssert.Expect(LogType.Warning, new Regex(@"Recovered save from save\.json\.bak"));
            var recovered = new JsonFileSaveService(directory);

            Assert.AreEqual(2, recovered.Load<Progress>(Progress.Key).level);
            Assert.IsTrue(recovered.IsDirty, "Recovered data should be written back to save.json on the next Flush.");
        }

        [Test]
        public void LeftoverTmpFile_IsUsedWhenMainIsMissing()
        {
            var save = new JsonFileSaveService(directory);
            save.Save(Progress.Key, new Progress { level = 5 });
            save.Flush();
            File.Move(MainFile, MainFile + ".tmp");         // simulate a crash between writing tmp and renaming it

            LogAssert.Expect(LogType.Warning, new Regex(@"Recovered save from save\.json\.tmp"));
            var recovered = new JsonFileSaveService(directory);

            Assert.AreEqual(5, recovered.Load<Progress>(Progress.Key).level);
        }

        private class RenameKeyMigration : ISaveMigration
        {
            public int FromVersion => 1;

            public void Migrate(IDictionary<string, string> entries)
            {
                if (entries.TryGetValue("old_progress", out var json))
                {
                    entries.Remove("old_progress");
                    entries[Progress.Key] = json;
                }
            }
        }

        [Test]
        public void Migration_UpgradesOldSave_AndBumpsVersion()
        {
            var v1 = new JsonFileSaveService(directory, currentVersion: 1);
            v1.Save("old_progress", new Progress { level = 7 });
            v1.Flush();

            var v2 = new JsonFileSaveService(directory, currentVersion: 2, migrations: new ISaveMigration[] { new RenameKeyMigration() });

            Assert.AreEqual(2, v2.Version);
            Assert.IsFalse(v2.Has("old_progress"));
            Assert.AreEqual(7, v2.Load<Progress>(Progress.Key).level);
            Assert.IsTrue(v2.IsDirty);

            v2.Flush();
            Assert.AreEqual(2, new JsonFileSaveService(directory, currentVersion: 2).Version);
        }

        [Test]
        public void MissingMigrationStep_KeepsDataAndOldVersion()
        {
            var v1 = new JsonFileSaveService(directory, currentVersion: 1);
            v1.Save(Progress.Key, new Progress { level = 6 });
            v1.Flush();

            LogAssert.Expect(LogType.Error, new Regex("No migration from version 1"));
            var v3 = new JsonFileSaveService(directory, currentVersion: 3);

            Assert.AreEqual(1, v3.Version);
            Assert.AreEqual(6, v3.Load<Progress>(Progress.Key).level);
        }
    }
}
