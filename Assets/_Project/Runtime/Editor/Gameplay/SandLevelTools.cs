using System.Collections.Generic;
using System.IO;
using GameKit.App;
using GameKit.Gameplay.Level;
using GameKit.Gameplay.Voxel;
using UnityEditor;
using UnityEngine;

namespace GameKit.EditorTools.Gameplay
{
    /// <summary>Editor helpers for sand levels: sample levels, grain material, validation.</summary>
    public static class SandLevelTools
    {
        private const string LevelsFolder = "Assets/_Project/Data/Levels";
        private const string DatabasePath = LevelsFolder + "/LevelDatabase.asset";
        private const string ConfigPath = "Assets/_Project/Data/Config/GameConfig.asset";
        private const string MaterialFolder = "Assets/_Project/Source/Effect/Materials";
        private const string MaterialPath = MaterialFolder + "/SandGrain.mat";

        private const int Pink = 0, Cream = 1, Mint = 2, Orange = 3, Sky = 4;
        private const int Size = 44;
        private const float Grain = 0.065f;
        private const float Mid = Size * 0.5f;

        // Bright toy colours (names kept from the first palette so level code stays readable).
        private static readonly Color[] Palette =
        {
            new Color(1.00f, 0.30f, 0.42f), // pink → strawberry red-pink
            new Color(1.00f, 0.80f, 0.20f), // cream → sunny yellow
            new Color(0.22f, 0.82f, 0.45f), // mint → fresh green
            new Color(1.00f, 0.52f, 0.12f), // orange
            new Color(0.20f, 0.58f, 1.00f)  // sky → bright blue
        };

        private const string SurfaceMaterialPath = MaterialFolder + "/SandSurface.mat";

        [MenuItem("Tools/GameKit/Sand/Create Surface Material")]
        public static Material CreateSurfaceMaterial()
        {
            var existing = AssetDatabase.LoadAssetAtPath<Material>(SurfaceMaterialPath);
            if (existing != null)
            {
                return existing;
            }

            Shader shader = Shader.Find("GameKit/SandSurface");
            if (shader == null)
            {
                Debug.LogError("[SandLevelTools] Shader GameKit/SandSurface not found.");
                return null;
            }

            Directory.CreateDirectory(MaterialFolder);
            var material = new Material(shader) { name = "SandSurface" };
            AssetDatabase.CreateAsset(material, SurfaceMaterialPath);
            AssetDatabase.SaveAssets();
            return material;
        }

        [MenuItem("Tools/GameKit/Sand/Create Grain Material")]
        public static Material CreateGrainMaterial()
        {
            var existing = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (existing != null)
            {
                return existing;
            }

            Shader shader = Shader.Find("GameKit/SandGrain");
            if (shader == null)
            {
                Debug.LogError("[SandLevelTools] Shader GameKit/SandGrain not found.");
                return null;
            }

            Directory.CreateDirectory(MaterialFolder);
            var material = new Material(shader) { name = "SandGrain", enableInstancing = true };
            AssetDatabase.CreateAsset(material, MaterialPath);
            AssetDatabase.SaveAssets();
            return material;
        }

        [MenuItem("Tools/GameKit/Sand/Create Sample Levels")]
        public static void CreateSampleLevels()
        {
            Directory.CreateDirectory(LevelsFolder);
            var levels = new List<SandLevelData>
            {
                SaveLevel("Level_01", BuildCake()),
                SaveLevel("Level_02", BuildMushroom()),
                SaveLevel("Level_03", BuildIceCream()),
                SaveLevel("Level_04", BuildSnowmanFromMeshes()),
                SaveLevel("Level_05", BuildOnionBalls()),
                SaveLevel("Level_06", BuildGiftTower()),
                SaveLevel("Level_07", BuildCookieStack())
            };

            var database = AssetDatabase.LoadAssetAtPath<LevelDatabase>(DatabasePath);
            if (database == null)
            {
                database = ScriptableObject.CreateInstance<LevelDatabase>();
                AssetDatabase.CreateAsset(database, DatabasePath);
            }
            // Keep the order the designer set; only append sample levels that are not listed yet.
            database.levels.RemoveAll(l => l == null);
            foreach (SandLevelData level in levels)
            {
                if (!database.levels.Contains(level))
                {
                    database.levels.Add(level);
                }
            }
            EditorUtility.SetDirty(database);

            var config = AssetDatabase.LoadAssetAtPath<GameConfig>(ConfigPath);
            if (config != null && config.levels == null)
            {
                config.levels = database;
                EditorUtility.SetDirty(config);
            }

            AssetDatabase.SaveAssets();
            ValidateAll();
        }

        [MenuItem("Tools/GameKit/Sand/Validate Levels")]
        public static void ValidateAll()
        {
            var database = AssetDatabase.LoadAssetAtPath<LevelDatabase>(DatabasePath);
            if (database == null)
            {
                Debug.LogWarning("[SandLevelTools] No LevelDatabase at " + DatabasePath);
                return;
            }

            foreach (SandLevelData level in database.levels)
            {
                Validate(level);
            }
        }

        public static bool Validate(SandLevelData level)
        {
            List<string> errors = LevelValidator.Validate(level);
            if (errors.Count > 0)
            {
                Debug.LogError($"[SandLevelTools] {level.name}: {errors.Count} error(s)\n" + string.Join("\n", errors), level);
                return false;
            }

            var result = LevelSolver.Solve(level.ToBoardBlocks(), level.trayColorQueue, out List<int> order);
            int grains = 0;
            foreach (var cells in level.BuildGrid().CellsByBlock(level.blocks.Count))
            {
                grains += cells.Count;
            }

            string message = $"[SandLevelTools] {level.name}: {level.blocks.Count} blocks, {grains} grains, solver: {result}" +
                             (result == LevelSolver.Result.Solvable ? $" (order {string.Join(",", order)})" : "");
            if (result == LevelSolver.Result.Unsolvable)
            {
                Debug.LogError(message, level);
                return false;
            }
            Debug.Log(message, level);
            return true;
        }

        private static SandLevelData SaveLevel(string name, SandLevelData data)
        {
            string path = $"{LevelsFolder}/{name}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<SandLevelData>(path);
            if (existing != null)
            {
                EditorUtility.CopySerialized(data, existing);
                existing.name = name;
                Object.DestroyImmediate(data);
                EditorUtility.SetDirty(existing);
                return existing;
            }

            data.name = name;
            AssetDatabase.CreateAsset(data, path);
            return data;
        }

        private static SandLevelData NewLevel(int size, float grain)
        {
            var level = ScriptableObject.CreateInstance<SandLevelData>();
            level.gridSize = new Vector3Int(size, size, size);
            level.grainSize = grain;
            level.palette = (Color[])Palette.Clone();
            level.modelRotation = new Vector3(15f, 30f, 0f);
            return level;
        }

        private static int Add(SandLevelData level, string name, int color, int parent, params SandShape[] shapes)
        {
            level.blocks.Add(new SandBlockDef { name = name, colorIndex = color, parent = parent, shapes = new List<SandShape>(shapes) });
            return level.blocks.Count - 1;
        }

        private static SandShape Box(float x, float y, float z, float sx, float sy, float sz) =>
            new SandShape(SandShapeKind.Box, new Vector3(x, y, z), new Vector3(sx, sy, sz));

        private static SandShape Egg(float x, float y, float z, float sx, float sy, float sz) =>
            new SandShape(SandShapeKind.Ellipsoid, new Vector3(x, y, z), new Vector3(sx, sy, sz));

        private static SandShape Pillar(float x, float y, float z, float d, float h) =>
            new SandShape(SandShapeKind.Cylinder, new Vector3(x, y, z), new Vector3(d, h, d));

        private static SandShape Ball(float x, float y, float z, float d) => Egg(x, y, z, d, d, d);

        private static SandShape Shape(SandShapeKind kind, float x, float y, float z, float sx, float sy, float sz) =>
            new SandShape(kind, new Vector3(x, y, z), new Vector3(sx, sy, sz));

        /// <summary>
        /// Level 4: snowman made of Unity primitive meshes and baked through <see cref="SandModelImporter"/>,
        /// the same path a real FBX / OBJ model takes. The cores inside the body balls become nested blocks.
        /// </summary>
        private static SandLevelData BuildSnowmanFromMeshes()
        {
            var root = new GameObject("Snowman");
            var created = new List<Object>();
            try
            {
                void Part(string name, PrimitiveType type, Vector3 position, Vector3 scale, int color, Vector3 euler = default)
                {
                    GameObject go = GameObject.CreatePrimitive(type);
                    go.name = name;
                    go.transform.SetParent(root.transform, false);
                    go.transform.localPosition = position;
                    go.transform.localEulerAngles = euler;
                    go.transform.localScale = scale;
                    var material = new Material(Shader.Find("Standard")) { color = Palette[color] };
                    created.Add(material);
                    go.GetComponent<MeshRenderer>().sharedMaterial = material;
                }

                Part("Body_Low", PrimitiveType.Sphere, new Vector3(0f, 0.5f, 0f), Vector3.one * 1.0f, Cream);
                Part("Body_Mid", PrimitiveType.Sphere, new Vector3(0f, 1.28f, 0f), Vector3.one * 0.75f, Cream);
                Part("Head", PrimitiveType.Sphere, new Vector3(0f, 1.9f, 0f), Vector3.one * 0.55f, Cream);
                Part("Core_Low", PrimitiveType.Sphere, new Vector3(0f, 0.5f, 0f), Vector3.one * 0.55f, Pink);
                Part("Core_Mid", PrimitiveType.Sphere, new Vector3(0f, 1.28f, 0f), Vector3.one * 0.4f, Pink);
                Part("Core_Head", PrimitiveType.Sphere, new Vector3(0f, 1.9f, 0f), Vector3.one * 0.28f, Pink);
                Part("Button_A", PrimitiveType.Sphere, new Vector3(0f, 1.42f, -0.33f), Vector3.one * 0.13f, Sky);
                Part("Button_B", PrimitiveType.Sphere, new Vector3(0f, 1.24f, -0.36f), Vector3.one * 0.13f, Sky);
                Part("Button_C", PrimitiveType.Sphere, new Vector3(0f, 0.86f, -0.4f), Vector3.one * 0.13f, Sky);
                Part("Hat_Brim", PrimitiveType.Cylinder, new Vector3(0f, 2.14f, 0f), new Vector3(0.62f, 0.05f, 0.62f), Orange);
                Part("Hat_Top", PrimitiveType.Cylinder, new Vector3(0f, 2.34f, 0f), new Vector3(0.38f, 0.17f, 0.38f), Orange);
                Part("Nose", PrimitiveType.Capsule, new Vector3(0f, 1.9f, -0.3f), new Vector3(0.09f, 0.12f, 0.09f), Orange, new Vector3(90f, 0f, 0f));

                var level = ScriptableObject.CreateInstance<SandLevelData>();
                SandModelImporter.Bake(root, level, Size);
                return level;
            }
            finally
            {
                Object.DestroyImmediate(root);
                foreach (Object o in created)
                {
                    Object.DestroyImmediate(o);
                }
            }
        }

        /// <summary>
        /// Adds an object made of 3 layers: outer → middle → core, each one only tappable once the one around it
        /// is gone. <paramref name="scales"/> are the middle and core sizes relative to the outer one.
        /// </summary>
        private static void AddLayered(SandLevelData level, string name, SandShapeKind kind, Vector3 center, Vector3 size,
            int outerColor, int middleColor, int coreColor, float middleScale = 0.7f, float coreScale = 0.42f)
        {
            int outer = Add(level, name + "_Outer", outerColor, -1, new SandShape(kind, center, size));
            int middle = Add(level, name + "_Middle", middleColor, outer, new SandShape(kind, center, size * middleScale));
            Add(level, name + "_Core", coreColor, middle, new SandShape(kind, center, size * coreScale));
        }

        /// <summary>Level 5: three onion balls in a pyramid, each with 3 layers of different colours.</summary>
        private static SandLevelData BuildOnionBalls()
        {
            var level = NewLevel(Size, Grain);
            level.modelRotation = new Vector3(10f, 0f, 0f);
            var ball = Vector3.one * 17f;
            AddLayered(level, "Ball_L", SandShapeKind.Ellipsoid, new Vector3(Mid - 9f, 9f, Mid), ball, Pink, Cream, Mint);
            AddLayered(level, "Ball_R", SandShapeKind.Ellipsoid, new Vector3(Mid + 9f, 9f, Mid), ball, Cream, Mint, Pink);
            AddLayered(level, "Ball_Top", SandShapeKind.Ellipsoid, new Vector3(Mid, 23.5f, Mid), ball, Mint, Pink, Cream);
            level.trayColorQueue = new List<int> { Pink, Cream, Mint };
            return level;
        }

        /// <summary>Level 6: tower of three gift boxes, each box hides a smaller box that hides a core box.</summary>
        private static SandLevelData BuildGiftTower()
        {
            var level = NewLevel(Size, Grain);
            AddLayered(level, "Gift_Bottom", SandShapeKind.Box, new Vector3(Mid, 8f, Mid), new Vector3(20f, 16f, 20f), Orange, Sky, Pink);
            AddLayered(level, "Gift_Middle", SandShapeKind.Box, new Vector3(Mid, 22.5f, Mid), new Vector3(15f, 13f, 15f), Sky, Pink, Orange);
            AddLayered(level, "Gift_Top", SandShapeKind.Box, new Vector3(Mid, 34f, Mid), new Vector3(11f, 10f, 11f), Pink, Orange, Sky);
            level.trayColorQueue = new List<int> { Orange, Sky, Pink };
            return level;
        }

        /// <summary>
        /// Level 7: stack of three sandwich cookies. The cream shows at the edge but belongs to (is locked by) the
        /// cookie on top of it: tapping it first makes it shake, it only comes out once that cookie is gone.
        /// </summary>
        private static SandLevelData BuildCookieStack()
        {
            var level = NewLevel(Size, Grain);
            level.modelRotation = new Vector3(25f, 20f, 0f);
            (int cookie, int cream, float x)[] sandwiches = { (Orange, Pink, Mid - 2f), (Pink, Mint, Mid + 2f), (Mint, Orange, Mid) };
            float y = 0f;
            for (int i = 0; i < sandwiches.Length; i++)
            {
                var (cookie, cream, x) = sandwiches[i];
                Add(level, $"Cookie{i}_Bottom", cookie, -1, Pillar(x, y + 1.75f, Mid, 18f, 3.5f));
                int top = Add(level, $"Cookie{i}_Top", cookie, -1, Pillar(x, y + 7.25f, Mid, 18f, 3.5f));
                Add(level, $"Cookie{i}_Cream", cream, top, Pillar(x, y + 4.5f, Mid, 15.5f, 2f));
                y += 9f;
            }
            level.trayColorQueue = new List<int> { Orange, Pink, Mint };
            return level;
        }

        /// <summary>Level 1: three-tier cake with cream rings and candies. No nesting: teaches tap → tray.</summary>
        private static SandLevelData BuildCake()
        {
            var level = NewLevel(Size, Grain);
            Add(level, "Tier_Bottom", Pink, -1, Pillar(Mid, 4.5f, Mid, 36f, 9f));
            Add(level, "Tier_Middle", Cream, -1, Pillar(Mid, 13f, Mid, 27f, 8f));
            Add(level, "Tier_Top", Pink, -1, Pillar(Mid, 20.5f, Mid, 18f, 7f));
            Add(level, "Ring_Bottom", Mint, -1, Shape(SandShapeKind.Torus, Mid, 9.5f, Mid, 36f, 3.5f, 36f));
            Add(level, "Ring_Middle", Mint, -1, Shape(SandShapeKind.Torus, Mid, 17f, Mid, 27f, 3.5f, 27f));
            Add(level, "Ring_Top", Cream, -1, Shape(SandShapeKind.Torus, Mid, 24f, Mid, 18f, 3.5f, 18f));
            Add(level, "Candy_L", Pink, -1, Ball(Mid - 5f, 26.5f, Mid, 5f));
            Add(level, "Candy_C", Cream, -1, Ball(Mid, 27.5f, Mid, 5.5f));
            Add(level, "Candy_R", Mint, -1, Ball(Mid + 5f, 26.5f, Mid, 5f));
            level.trayColorQueue = new List<int> { Pink, Cream, Mint };
            return level;
        }

        /// <summary>Level 2: mushroom. Cap and stem hide inner blocks (2–3 layers).</summary>
        private static SandLevelData BuildMushroom()
        {
            var level = NewLevel(Size, Grain);
            int cap = Add(level, "Cap", Pink, -1, Shape(SandShapeKind.Dome, Mid, 16f, Mid, 36f, 22f, 36f));
            int capInner = Add(level, "Cap_Inner", Orange, cap, Shape(SandShapeKind.Dome, Mid, 17.5f, Mid, 24f, 14f, 24f));
            Add(level, "Cap_Core", Mint, capInner, Shape(SandShapeKind.Dome, Mid, 18.5f, Mid, 12f, 7f, 12f));
            int stem = Add(level, "Stem", Cream, -1, Pillar(Mid, 8.5f, Mid, 11f, 17f));
            Add(level, "Stem_Core", Orange, stem, Pillar(Mid, 8f, Mid, 5f, 11f));
            Add(level, "Spot_A", Cream, -1, Ball(Mid + 9f, 25.5f, Mid, 6f));
            Add(level, "Spot_B", Cream, -1, Ball(Mid - 4f, 26f, Mid - 8f, 5.5f));
            Add(level, "Grass_A", Mint, -1, Shape(SandShapeKind.Dome, Mid - 10f, 0f, Mid + 8f, 12f, 8f, 12f));
            Add(level, "Grass_B", Mint, -1, Shape(SandShapeKind.Dome, Mid + 10f, 0f, Mid - 8f, 12f, 8f, 12f));
            Add(level, "Pebble_A", Pink, -1, Ball(Mid - 11f, 2.5f, Mid - 8f, 5f));
            Add(level, "Pebble_B", Pink, -1, Ball(Mid + 11f, 2.5f, Mid + 8f, 5f));
            Add(level, "Pebble_C", Orange, -1, Ball(Mid, 2.5f, Mid + 13f, 5f));
            level.trayColorQueue = new List<int> { Cream, Pink, Mint, Orange };
            return level;
        }

        /// <summary>Level 3: double-scoop ice cream in a cone, wafers and drips. Every big part hides a core.</summary>
        private static SandLevelData BuildIceCream()
        {
            var level = NewLevel(Size, Grain);
            int cone = Add(level, "Cone", Orange, -1, Shape(SandShapeKind.ConeDown, Mid, 10f, Mid, 18f, 20f, 18f));
            Add(level, "Cone_Core", Cream, cone, Shape(SandShapeKind.ConeDown, Mid, 11f, Mid, 9f, 12f, 9f));
            int scoop1 = Add(level, "Scoop_Low", Pink, -1, Ball(Mid, 24f, Mid, 19f));
            Add(level, "Scoop_Low_Core", Cream, scoop1, Ball(Mid, 24f, Mid, 10f));
            int scoop2 = Add(level, "Scoop_High", Sky, -1, Ball(Mid, 35f, Mid, 15f));
            Add(level, "Scoop_High_Core", Pink, scoop2, Ball(Mid, 35f, Mid, 8f));
            Add(level, "Cherry", Pink, -1, Ball(Mid, 41f, Mid, 5f));
            Add(level, "Wafer_L", Orange, -1, Box(Mid - 5f, 40f, Mid, 2f, 8f, 3f));
            Add(level, "Wafer_R", Orange, -1, Box(Mid + 5f, 40f, Mid, 2f, 8f, 3f));
            Add(level, "Drip_L", Sky, -1, Egg(Mid - 8f, 18f, Mid - 2f, 4f, 7f, 4f));
            Add(level, "Drip_R", Sky, -1, Egg(Mid + 8f, 18f, Mid + 2f, 4f, 7f, 4f));
            Add(level, "Sprinkle", Cream, -1, Ball(Mid, 27f, Mid - 9f, 5f));
            level.trayColorQueue = new List<int> { Sky, Pink, Orange, Cream };
            return level;
        }
    }
}
