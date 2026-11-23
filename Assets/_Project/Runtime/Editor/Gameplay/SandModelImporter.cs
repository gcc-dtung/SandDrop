using System.Collections.Generic;
using System.Linq;
using GameKit.Gameplay.Level;
using GameKit.Gameplay.Voxel;
using UnityEditor;
using UnityEngine;

namespace GameKit.EditorTools.Gameplay
{
    /// <summary>
    /// Bakes a 3D model (FBX / OBJ / prefab / scene object) into a sand level.
    /// One block per mesh part: each submesh, and on textured submeshes each colour area of the texture.
    /// Parts fully inside other parts become nested blocks automatically.
    /// </summary>
    public static class SandModelImporter
    {
        private const string LevelsFolder = "Assets/_Project/Data/Levels";
        private const string DatabasePath = LevelsFolder + "/LevelDatabase.asset";
        /// <summary>Longest side of the model in world units once it is sand (fits the portrait camera).</summary>
        public const float TargetWorldSize = 2.9f;
        private const float SameColorDistance = 0.08f;

        [MenuItem("Tools/GameKit/Sand/Create Level From Selected Model")]
        public static void CreateFromSelection()
        {
            var model = Selection.activeGameObject;
            if (model == null)
            {
                EditorUtility.DisplayDialog("Sand", "Select a model (FBX / OBJ / prefab) in the Project window or a GameObject in the scene.", "OK");
                return;
            }

            var level = ScriptableObject.CreateInstance<SandLevelData>();
            Bake(model, level, level.bakeResolution);
            string path = AssetDatabase.GenerateUniqueAssetPath($"{LevelsFolder}/{model.name}.asset");
            AssetDatabase.CreateAsset(level, path);

            var database = AssetDatabase.LoadAssetAtPath<LevelDatabase>(DatabasePath);
            if (database != null && !database.levels.Contains(level))
            {
                database.levels.Add(level);
                EditorUtility.SetDirty(database);
            }
            AssetDatabase.SaveAssets();
            Selection.activeObject = level;
            SandLevelTools.Validate(level);
        }

        [MenuItem("Tools/GameKit/Sand/Rebake Selected Level")]
        public static void RebakeSelection()
        {
            if (!(Selection.activeObject is SandLevelData level) || string.IsNullOrEmpty(level.sourceModelGuid))
            {
                EditorUtility.DisplayDialog("Sand", "Select a level that was baked from a model.", "OK");
                return;
            }

            string sourcePath = AssetDatabase.GUIDToAssetPath(level.sourceModelGuid);
            var layout = AssetDatabase.LoadAssetAtPath<TextAsset>(sourcePath);
            if (layout != null)
            {
                SandLayoutImporter.Import(layout, level);
                EditorUtility.SetDirty(level);
                AssetDatabase.SaveAssets();
                SandLevelTools.Validate(level);
                return;
            }

            var model = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
            if (model == null)
            {
                Debug.LogError("[SandModelImporter] Source model not found for " + level.name, level);
                return;
            }
            Bake(model, level, level.bakeResolution);
            AssetDatabase.SaveAssets();
            SandLevelTools.Validate(level);
        }

        /// <summary>Voxelizes the model into the level (grid, blocks, palette, tray queue).</summary>
        public static void Bake(GameObject model, SandLevelData level, int resolution)
        {
            List<MeshPart> parts = CollectParts(model);
            MeshVoxelResult result = MeshVoxelizer.Build(parts, resolution);
            foreach (string warning in result.Warnings)
            {
                Debug.LogWarning($"[SandModelImporter] {model.name}: {warning}", level);
            }

            // Drop parts that ended with no grains and renumber the rest.
            var keep = new List<int>();
            var remap = new int[parts.Count];
            for (int p = 0; p < parts.Count; p++)
            {
                remap[p] = result.CellCounts[p] > 0 ? keep.Count : -1;
                if (remap[p] >= 0)
                {
                    keep.Add(p);
                }
            }

            VoxelGrid grid = result.Grid;
            for (int i = 0; i < grid.CellCount; i++)
            {
                if (grid[i] >= 0)
                {
                    grid[i] = (short)remap[grid[i]];
                }
            }

            // Palette from part colours, merging near-equal ones.
            var palette = new List<Color>();
            var oldBlocks = level.blocks;
            var blocks = new List<SandBlockDef>();
            foreach (int p in keep)
            {
                int parent = result.Parents[p];
                while (parent >= 0 && remap[parent] < 0)
                {
                    parent = result.Parents[parent];
                }
                blocks.Add(new SandBlockDef
                {
                    name = parts[p].Name,
                    colorIndex = PaletteIndex(palette, parts[p].Color),
                    parent = parent >= 0 ? remap[parent] : -1
                });
            }

            // Rebake of the same model: keep colours the designer picked.
            bool keepColors = level.IsBaked && oldBlocks.Count == blocks.Count &&
                              oldBlocks.Select(b => b.name).SequenceEqual(blocks.Select(b => b.name));
            if (keepColors)
            {
                for (int i = 0; i < blocks.Count; i++)
                {
                    blocks[i].colorIndex = oldBlocks[i].colorIndex;
                }
            }
            else
            {
                level.palette = palette.ToArray();
            }

            level.blocks = blocks;
            level.gridSize = grid.Size;
            level.bakedCells = grid.ToRunLength();
            level.bakeResolution = resolution;
            level.grainSize = TargetWorldSize / Mathf.Max(grid.Size.x, Mathf.Max(grid.Size.y, grid.Size.z));
            if (level.modelRotation == Vector3.zero)
            {
                level.modelRotation = new Vector3(15f, 30f, 0f);
            }
            string path = AssetDatabase.GetAssetPath(model);
            level.sourceModelGuid = string.IsNullOrEmpty(path) ? "" : AssetDatabase.AssetPathToGUID(path);
            level.trayColorQueue = BuildTrayQueue(level);
            EditorUtility.SetDirty(level);

            Debug.Log($"[SandModelImporter] {model.name}: {blocks.Count} blocks, {level.palette.Length} colours, " +
                      $"grid {grid.Size}, {blocks.Count(b => b.parent >= 0)} nested.", level);
        }

        /// <summary>Tray queue in the order a natural play-through reveals the colours (see <see cref="TrayQueueBuilder"/>).</summary>
        public static List<int> BuildTrayQueue(SandLevelData level)
        {
            int colorCount = level.palette.Length;
            var colors = level.blocks.Select(b => b.colorIndex >= 0 && b.colorIndex < colorCount ? b.colorIndex : -1).ToList();
            return TrayQueueBuilder.Build(colors, level.Parents());
        }

        private static int PaletteIndex(List<Color> palette, Color color)
        {
            for (int i = 0; i < palette.Count; i++)
            {
                Color p = palette[i];
                float d = Mathf.Abs(p.r - color.r) + Mathf.Abs(p.g - color.g) + Mathf.Abs(p.b - color.b);
                if (d < SameColorDistance * 3f)
                {
                    return i;
                }
            }
            palette.Add(new Color(color.r, color.g, color.b, 1f));
            return palette.Count - 1;
        }

        /// <summary>Mesh parts in the model root's space.</summary>
        public static List<MeshPart> CollectParts(GameObject root)
        {
            var parts = new List<MeshPart>();
            Matrix4x4 toRoot = root.transform.worldToLocalMatrix;

            foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                Mesh mesh = filter.sharedMesh;
                if (mesh == null)
                {
                    continue;
                }

                Matrix4x4 m = toRoot * filter.transform.localToWorldMatrix;
                Vector3[] vertices = mesh.vertices.Select(v => m.MultiplyPoint3x4(v)).ToArray();
                Vector2[] uv = mesh.uv;
                Material[] materials = filter.TryGetComponent(out MeshRenderer renderer) ? renderer.sharedMaterials : new Material[0];

                for (int s = 0; s < mesh.subMeshCount; s++)
                {
                    Material material = s < materials.Length ? materials[s] : null;
                    int[] triangles = mesh.GetTriangles(s);
                    string baseName = mesh.subMeshCount > 1 ? $"{filter.name}_{(material != null ? material.name : s.ToString())}" : filter.name;

                    Texture2D texture = ReadableTexture(material);
                    if (texture == null || uv.Length != vertices.Length)
                    {
                        parts.Add(new MeshPart { Name = baseName, Vertices = vertices, Triangles = triangles, Color = MaterialColor(material) });
                        continue;
                    }

                    // Textured: split triangles by the texture colour at their centre (palette / atlas style models).
                    Color tint = MaterialColor(material);
                    var groups = new Dictionary<int, List<int>>();
                    var groupColor = new Dictionary<int, Color>();
                    for (int t = 0; t + 2 < triangles.Length; t += 3)
                    {
                        Vector2 centre = (uv[triangles[t]] + uv[triangles[t + 1]] + uv[triangles[t + 2]]) / 3f;
                        Color c = texture.GetPixelBilinear(centre.x, centre.y) * tint;
                        int key = Quantize(c);
                        if (!groups.TryGetValue(key, out List<int> list))
                        {
                            groups[key] = list = new List<int>();
                            groupColor[key] = c;
                        }
                        list.Add(triangles[t]);
                        list.Add(triangles[t + 1]);
                        list.Add(triangles[t + 2]);
                    }
                    Object.DestroyImmediate(texture);

                    int n = 0;
                    foreach (var pair in groups)
                    {
                        parts.Add(new MeshPart
                        {
                            Name = groups.Count > 1 ? $"{baseName}_{n++}" : baseName,
                            Vertices = vertices,
                            Triangles = pair.Value.ToArray(),
                            Color = groupColor[pair.Key]
                        });
                    }
                }
            }
            return parts;
        }

        private static int Quantize(Color c)
        {
            int r = Mathf.RoundToInt(Mathf.Clamp01(c.r) * 7f), g = Mathf.RoundToInt(Mathf.Clamp01(c.g) * 7f), b = Mathf.RoundToInt(Mathf.Clamp01(c.b) * 7f);
            return (r << 6) | (g << 3) | b;
        }

        private static Color MaterialColor(Material material)
        {
            if (material == null)
            {
                return Color.white;
            }
            if (material.HasProperty("_BaseColor"))
            {
                return material.GetColor("_BaseColor");
            }
            return material.HasProperty("_Color") ? material.color : Color.white;
        }

        /// <summary>CPU copy of the material's main texture (works for textures without Read/Write), or null.</summary>
        private static Texture2D ReadableTexture(Material material)
        {
            if (material == null || !(material.mainTexture is Texture2D source))
            {
                return null;
            }

            RenderTexture rt = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            RenderTexture previous = RenderTexture.active;
            Graphics.Blit(source, rt);
            RenderTexture.active = rt;
            var copy = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
            copy.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
            copy.Apply();
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);
            return copy;
        }
    }
}
