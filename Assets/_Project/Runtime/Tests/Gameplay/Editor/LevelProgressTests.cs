using GameKit.Core.Foundation.Save;
using GameKit.Gameplay.Level;
using NUnit.Framework;

namespace GameKit.Tests.Gameplay
{
    public class LevelProgressTests
    {
        [Test]
        public void Load_NoSave_StartsAtFirstLevel()
        {
            var save = new JsonFileSaveService(null);

            Assert.AreEqual(0, save.Load<LevelProgressData>(LevelProgressData.Key).levelIndex);
        }

        [Test]
        public void Save_ThenLoad_KeepsLevelIndex()
        {
            var save = new JsonFileSaveService(null);

            save.Save(LevelProgressData.Key, new LevelProgressData { levelIndex = 4 });

            Assert.AreEqual(4, save.Load<LevelProgressData>(LevelProgressData.Key).levelIndex);
        }

        [Test]
        public void LevelDatabase_Get_WrapsAround()
        {
            var db = UnityEngine.ScriptableObject.CreateInstance<LevelDatabase>();
            try
            {
                var a = UnityEngine.ScriptableObject.CreateInstance<SandLevelData>();
                var b = UnityEngine.ScriptableObject.CreateInstance<SandLevelData>();
                db.levels.Add(a);
                db.levels.Add(b);

                Assert.AreSame(a, db.Get(2));
                Assert.AreSame(b, db.Get(-1));
                UnityEngine.Object.DestroyImmediate(a);
                UnityEngine.Object.DestroyImmediate(b);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(db);
            }
        }
    }
}
