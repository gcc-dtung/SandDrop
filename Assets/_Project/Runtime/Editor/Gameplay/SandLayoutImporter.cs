using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GameKit.Gameplay.Board;
using GameKit.Gameplay.Level;
using GameKit.Gameplay.Voxel;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace GameKit.EditorTools.Gameplay
{
    /// <summary>
    /// Builds sand levels from level layout files (<c>LevelLayouts/LvN.json</c>): each part is one block, placed
    /// with its local position / rotation / scale under its parent part (<c>group</c> = parent part id, which is
    /// also the nesting parent). Parts with colour -1 get a colour so every colour ends with its declared count.
    /// Decorations (eyes, buttons...) ride on the block of their group; special items are not imported yet.
    /// </summary>
    public static class SandLayoutImporter
    {
        private const string LayoutsFolder = "Assets/_Project/LevelLayouts";
        private const string MeshRoot = "Assets/_Project/Assets/Models3D/";
        private const string OutputFolder = "Assets/_Project/Data/Levels/Imported";
        private const string DatabasePath = "Assets/_Project/Data/Levels/LevelDatabase.asset";
        private const int StartResolution = 96;
        private const int MaxResolution = 160;
        private const int ColorAttempts = 40;
        /// <summary>Scale of a child that is an exact copy of its parent, so it fits inside as the next layer.</summary>
        private const float CopyShrink = 0.85f;

        [Serializable]
        private sealed class LayoutColor
        {
            public int index;
            public int count;
            public Color color;
        }

        [Serializable]
        private sealed class LayoutPart
        {
            public int id;
            public string meshPath;
            public int colorIndex;
            public Color color;
            public Vector3 position;
            public Vector3 eulerAngles;
            public Vector3 scale;
            public int group;
        }

        [Serializable]
        private sealed class LayoutFile
        {
            public string level;
            public LayoutColor[] colors;
            public LayoutPart[] parts;
            public LayoutPart[] decorates;
        }

        [MenuItem("Tools/GameKit/Sand/Layouts/Import Selected Layouts")]
        public static void ImportSelected()
        {
            var layouts = Selection.objects.OfType<TextAsset>().ToList();
            if (layouts.Count == 0)
            {
                EditorUtility.DisplayDialog("Sand", "Select one or more LvN.json files in " + LayoutsFolder + ".", "OK");
                return;
            }
            ImportMany(layouts);
        }

        [MenuItem("Tools/GameKit/Sand/Layouts/Import All Layouts")]
        public static void ImportAll()
        {
            var layouts = AssetDatabase.FindAssets("t:TextAsset", new[] { LayoutsFolder })
                .Select(g => AssetDatabase.LoadAssetAtPath<TextAsset>(AssetDatabase.GUIDToAssetPath(g)))
                .Where(t => t != null && t.name.StartsWith("Lv"))
                .ToList();
            if (!EditorUtility.DisplayDialog("Sand", $"Import {layouts.Count} layouts into {OutputFolder}?\nThis can take a few minutes.", "Import", "Cancel"))
            {
                return;
            }
            ImportMany(layouts);
        }

        private static void ImportMany(List<TextAsset> layouts)
        {
            layouts = layouts.OrderBy(t => LevelNumber(t.name)).ToList();
            EnsureFolder(OutputFolder);
            var imported = new List<SandLevelData>();
            int failed = 0;
            try
            {
                AssetDatabase.StartAssetEditing();
                for (int i = 0; i < layouts.Count; i++)
                {
                    if (EditorUtility.DisplayCancelableProgressBar("Sand layouts", $"{layouts[i].name} ({i + 1}/{layouts.Count})", (float)i / layouts.Count))
                    {
                        break;
                    }

                    string path = $"{OutputFolder}/{layouts[i].name}.asset";
                    var level = AssetDatabase.LoadAssetAtPath<SandLevelData>(path);
                    bool created = level == null;
                    if (created)
                    {
                        level = ScriptableObject.CreateInstance<SandLevelData>();
                    }

                    if (!Import(layouts[i], level))
                    {
                        failed++;
                        if (created)
                        {
                            Object.DestroyImmediate(level);
                        }
                        continue;
                    }
                    if (created)
                    {
                        AssetDatabase.CreateAsset(level, path);
                    }
                    EditorUtility.SetDirty(level);
                    imported.Add(level);
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                EditorUtility.ClearProgressBar();
            }

            AddToDatabase(imported);
            AssetDatabase.SaveAssets();
            Debug.Log($"[SandLayoutImporter] Imported {imported.Count} level(s), {failed} failed.");
        }

        /// <summary>Fills <paramref name="level"/> from one layout file. Returns false when nothing could be built.</summary>
        public static bool Import(TextAsset layoutAsset, SandLevelData level)
        {
            LayoutFile layout;
            try
            {
                layout = JsonUtility.FromJson<LayoutFile>(layoutAsset.text);
            }
            catch (Exception e)
            {
                Debug.LogError($"[SandLayoutImporter] {layoutAsset.name}: cannot read layout ({e.Message}).", layoutAsset);
                return false;
            }
            if (layout?.parts == null || layout.parts.Length == 0 || layout.colors == null)
            {
                Debug.LogError($"[SandLayoutImporter] {layoutAsset.name}: layout has no parts.", layoutAsset);
                return false;
            }

            level.name = layoutAsset.name;

            if (!BuildMeshParts(layoutAsset, layout, out int[] parents, out Dictionary<int, int> indexById, out List<MeshPart> meshParts,
                    out Dictionary<string, Mesh> meshCache))
            {
                return false;
            }
            LayoutPart[] parts = layout.parts;

            // Voxelize; raise the resolution while thin nested layers still lose all their grains.
            MeshVoxelResult result = null;
            int resolution = StartResolution;
            while (true)
            {
                result = MeshVoxelizer.Build(meshParts, resolution, knownParents: parents);
                int empty = result.CellCounts.Count(c => c == 0);
                if (empty == 0 || resolution >= MaxResolution)
                {
                    if (empty > 0)
                    {
                        Debug.LogWarning($"[SandLayoutImporter] {layoutAsset.name}: {empty} part(s) have no grains even at resolution {resolution} " +
                                         "(too thin). They stay as empty blocks.", layoutAsset);
                    }
                    break;
                }
                resolution = Mathf.Min(MaxResolution, Mathf.CeilToInt(resolution * 1.3f));
            }

            // Palette = declared colours.
            int colorCount = layout.colors.Max(c => c.index) + 1;
            var palette = new Color[colorCount];
            var declared = new int[colorCount];
            foreach (LayoutColor c in layout.colors)
            {
                palette[c.index] = new Color(c.color.r, c.color.g, c.color.b, 1f);
                declared[c.index] = c.count;
            }

            var blocks = new List<SandBlockDef>(parts.Length);
            for (int i = 0; i < parts.Length; i++)
            {
                int color = parts[i].colorIndex;
                blocks.Add(new SandBlockDef
                {
                    name = meshParts[i].Name,
                    colorIndex = color >= 0 && color < colorCount ? color : -1,
                    parent = parents[i]
                });
            }

            level.palette = palette;
            level.blocks = blocks;
            level.gridSize = result.Grid.Size;
            level.bakedCells = result.Grid.ToRunLength();
            level.bakeResolution = resolution;
            level.grainSize = SandModelImporter.TargetWorldSize /
                              Mathf.Max(result.Grid.Size.x, Mathf.Max(result.Grid.Size.y, result.Grid.Size.z));
            level.modelRotation = new Vector3(10f, 0f, 0f);
            level.hideInnerBlocks = true;
            BuildDecorations(layout, indexById, meshCache, result.Origin, result.CellSize, level);
            level.sourceModelGuid = AssetGuid(layoutAsset);

            AssignColors(level, declared, LevelNumber(layoutAsset.name), out int peakWaiting);

            int decorations = level.decorations.Count;
            Debug.Log($"[SandLayoutImporter] {layoutAsset.name}: {blocks.Count} blocks, {colorCount} colours, grid {result.Grid.Size} " +
                      $"(res {resolution}), {blocks.Count(b => b.parent >= 0)} nested, peak waiting {peakWaiting}" +
                      (decorations > 0 ? $", {decorations} decorations" : "") + ".", level);
            return true;
        }

        /// <summary>Places every part's mesh in the level's space. Parent = the part whose id is this part's group.</summary>
        private static bool BuildMeshParts(TextAsset layoutAsset, LayoutFile layout, out int[] parents, out Dictionary<int, int> indexById,
            out List<MeshPart> meshParts, out Dictionary<string, Mesh> meshCache)
        {
            LayoutPart[] parts = layout.parts;
            indexById = new Dictionary<int, int>();
            for (int i = 0; i < parts.Length; i++)
            {
                indexById[parts[i].id] = i;
            }
            parents = new int[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                parents[i] = parts[i].group >= 0 && indexById.TryGetValue(parts[i].group, out int p) && p != i ? p : -1;
            }

            var matrices = new Matrix4x4?[parts.Length];
            meshParts = new List<MeshPart>(parts.Length);
            meshCache = new Dictionary<string, Mesh>();
            for (int i = 0; i < parts.Length; i++)
            {
                Mesh mesh = LoadMesh(parts[i].meshPath, meshCache);
                if (mesh == null)
                {
                    Debug.LogError($"[SandLayoutImporter] {layoutAsset.name}: mesh not found {MeshRoot}{parts[i].meshPath}.", layoutAsset);
                    return false;
                }
                Matrix4x4 m = WorldMatrix(i, parts, parents, matrices, 0);
                // A child that is an exact copy of its parent (same mesh, identity local transform) is the next layer
                // of the same shape: shrink it around the mesh centre so it becomes a core inside the parent.
                float shrink = CopyShrinkFactor(i, parts, parents);
                if (shrink < 1f)
                {
                    Vector3 c = mesh.bounds.center;
                    m *= Matrix4x4.Translate(c) * Matrix4x4.Scale(Vector3.one * shrink) * Matrix4x4.Translate(-c);
                }
                meshParts.Add(new MeshPart
                {
                    Name = $"{parts[i].id}_{Path.GetFileNameWithoutExtension(parts[i].meshPath)}",
                    Vertices = mesh.vertices.Select(v => m.MultiplyPoint3x4(v)).ToArray(),
                    Triangles = mesh.triangles
                });
            }
            return true;
        }

        /// <summary>
        /// Decorations (eyes, buttons...) as meshes riding on the block of their group, placed in grid space so the
        /// renderer can put them on the sand.
        /// </summary>
        private static void BuildDecorations(LayoutFile layout, Dictionary<int, int> indexById, Dictionary<string, Mesh> meshCache,
            Vector3 gridOrigin, float cellSize, SandLevelData level)
        {
            level.decorations = new List<SandDecoration>();
            if (layout.decorates == null)
            {
                return;
            }
            Matrix4x4 modelToGrid = Matrix4x4.Scale(Vector3.one / cellSize) * Matrix4x4.Translate(-gridOrigin);
            foreach (LayoutPart decoration in layout.decorates)
            {
                Mesh mesh = LoadMesh(decoration.meshPath, meshCache);
                if (mesh == null)
                {
                    continue;
                }
                level.decorations.Add(new SandDecoration
                {
                    mesh = mesh,
                    block = indexById.TryGetValue(decoration.group, out int block) ? block : -1,
                    color = new Color(decoration.color.r, decoration.color.g, decoration.color.b, 1f),
                    gridMatrix = modelToGrid * Matrix4x4.TRS(decoration.position, Quaternion.Euler(decoration.eulerAngles), decoration.scale)
                });
            }
        }

        /// <summary>Rebuilds only the decorations of an imported level from its layout (the sand stays as baked).</summary>
        public static bool RefreshDecorations(TextAsset layoutAsset, SandLevelData level)
        {
            var layout = JsonUtility.FromJson<LayoutFile>(layoutAsset.text);
            if (layout?.parts == null || layout.parts.Length != level.blocks.Count ||
                !BuildMeshParts(layoutAsset, layout, out _, out Dictionary<int, int> indexById, out List<MeshPart> meshParts,
                    out Dictionary<string, Mesh> meshCache))
            {
                return false;
            }
            Vector3Int size = MeshVoxelizer.GridFrame(meshParts, level.bakeResolution, out Vector3 origin, out float cell);
            if (size != level.gridSize)
            {
                Debug.LogError($"[SandLayoutImporter] {level.name}: grid does not match the layout any more; import it again.", level);
                return false;
            }
            BuildDecorations(layout, indexById, meshCache, origin, cell, level);
            EditorUtility.SetDirty(level);
            return true;
        }

        [MenuItem("Tools/GameKit/Sand/Layouts/Refresh Decorations Of Imported Levels")]
        public static void RefreshAllDecorations()
        {
            int done = 0, decorations = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:SandLevelData", new[] { OutputFolder }))
            {
                var level = AssetDatabase.LoadAssetAtPath<SandLevelData>(AssetDatabase.GUIDToAssetPath(guid));
                var layout = level != null ? AssetDatabase.LoadAssetAtPath<TextAsset>(AssetDatabase.GUIDToAssetPath(level.sourceModelGuid)) : null;
                if (layout != null && RefreshDecorations(layout, level))
                {
                    done++;
                    decorations += level.decorations.Count;
                }
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[SandLayoutImporter] Decorations refreshed on {done} level(s), {decorations} in total.");
        }

        /// <summary>
        /// Gives every block with colour -1 a colour from what is left of each declared count. Tries several shuffles
        /// (seeded by the level number) and keeps the one whose tray queue needs the fewest waiting slots
        /// (<see cref="TrayQueueBuilder"/>); stops early once the play fits the waiting row.
        /// </summary>
        private static void AssignColors(SandLevelData level, int[] declared, int levelNumber, out int peakWaiting)
        {
            var fixedColor = level.blocks.Select(b => b.colorIndex).ToArray();
            var left = (int[])declared.Clone();
            foreach (int c in fixedColor)
            {
                if (c >= 0 && c < left.Length)
                {
                    left[c]--;
                }
            }

            var pool = new List<int>();
            for (int c = 0; c < left.Length; c++)
            {
                for (int k = 0; k < left[c]; k++)
                {
                    pool.Add(c);
                }
            }
            var open = Enumerable.Range(0, fixedColor.Length).Where(i => fixedColor[i] < 0).ToList();
            if (pool.Count != open.Count)
            {
                Debug.LogWarning($"[SandLayoutImporter] {level.name}: {open.Count} blocks without colour but {pool.Count} colours left " +
                                 "from the declared counts.", level);
            }

            int[] parents = level.Parents();
            int[] best = null;
            List<int> bestQueue = null;
            peakWaiting = int.MaxValue;
            int attempts = open.Count == 0 ? 1 : ColorAttempts;
            for (int attempt = 0; attempt < attempts; attempt++)
            {
                var rng = new System.Random(levelNumber * 7919 + attempt);
                var shuffled = pool.OrderBy(_ => rng.Next()).ToList();
                var colors = (int[])fixedColor.Clone();
                for (int k = 0; k < open.Count; k++)
                {
                    colors[open[k]] = k < shuffled.Count ? shuffled[k] : 0;
                }

                TrayQueueBuilder.Result play = TrayQueueBuilder.BuildWithPlay(colors, parents);
                if (play.PeakWaiting < peakWaiting)
                {
                    peakWaiting = play.PeakWaiting;
                    best = colors;
                    bestQueue = play.Queue;
                }
                if (peakWaiting <= SandBoard.DefaultWaitingSlots - 1)
                {
                    break;
                }
            }

            for (int i = 0; i < best.Length; i++)
            {
                level.blocks[i].colorIndex = best[i];
            }
            level.trayColorQueue = bestQueue;
        }

        /// <summary>Re-picks the colours of the colour -1 blocks and the tray queue from the layout, without rebaking the sand.</summary>
        public static bool Recolor(TextAsset layoutAsset, SandLevelData level, out int peakWaiting)
        {
            peakWaiting = -1;
            var layout = JsonUtility.FromJson<LayoutFile>(layoutAsset.text);
            if (layout?.parts == null || layout.parts.Length != level.blocks.Count)
            {
                Debug.LogError($"[SandLayoutImporter] {layoutAsset.name}: layout and level do not match; import it again.", level);
                return false;
            }
            int colorCount = layout.colors.Max(c => c.index) + 1;
            var declared = new int[colorCount];
            foreach (LayoutColor c in layout.colors)
            {
                declared[c.index] = c.count;
            }
            for (int i = 0; i < layout.parts.Length; i++)
            {
                int color = layout.parts[i].colorIndex;
                level.blocks[i].colorIndex = color >= 0 && color < colorCount ? color : -1;
            }
            AssignColors(level, declared, LevelNumber(layoutAsset.name), out peakWaiting);
            EditorUtility.SetDirty(level);
            return true;
        }

        [MenuItem("Tools/GameKit/Sand/Layouts/Recolor Imported Levels")]
        public static void RecolorAll()
        {
            int fits = 0, total = 0;
            var guids = AssetDatabase.FindAssets("t:SandLevelData", new[] { OutputFolder });
            foreach (string guid in guids)
            {
                var level = AssetDatabase.LoadAssetAtPath<SandLevelData>(AssetDatabase.GUIDToAssetPath(guid));
                var layout = level != null ? AssetDatabase.LoadAssetAtPath<TextAsset>(AssetDatabase.GUIDToAssetPath(level.sourceModelGuid)) : null;
                if (layout == null || !Recolor(layout, level, out int peak))
                {
                    continue;
                }
                total++;
                if (peak <= SandBoard.DefaultWaitingSlots)
                {
                    fits++;
                }
                else
                {
                    Debug.LogWarning($"[SandLayoutImporter] {level.name}: best colouring still needs {peak} waiting slots.", level);
                }
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[SandLayoutImporter] Recoloured {total} level(s); {fits} have a winning play within {SandBoard.DefaultWaitingSlots} waiting slots.");
        }

        private static Matrix4x4 WorldMatrix(int i, LayoutPart[] parts, int[] parents, Matrix4x4?[] cache, int depth)
        {
            if (cache[i].HasValue)
            {
                return cache[i].Value;
            }
            LayoutPart part = parts[i];
            Matrix4x4 local = Matrix4x4.TRS(part.position, Quaternion.Euler(part.eulerAngles), part.scale);
            Matrix4x4 world = parents[i] >= 0 && depth < parts.Length
                ? WorldMatrix(parents[i], parts, parents, cache, depth + 1) * local
                : local;
            cache[i] = world;
            return world;
        }

        /// <summary>
        /// Extra scale for a part whose parent chain holds (near) copies: same mesh, no offset or rotation and a
        /// uniform scale between <see cref="CopyShrink"/> and 1. Each such link is pushed down to CopyShrink, so the
        /// layer left between parent and child is thick enough to keep grains. 1 = not a copy.
        /// </summary>
        private static float CopyShrinkFactor(int i, LayoutPart[] parts, int[] parents)
        {
            float factor = 1f;
            for (int guard = 0; parents[i] >= 0 && guard < parts.Length; guard++)
            {
                LayoutPart part = parts[i];
                float s = part.scale.x;
                bool copy = part.meshPath == parts[parents[i]].meshPath &&
                            part.position.sqrMagnitude < 1e-6f &&
                            Quaternion.Angle(Quaternion.Euler(part.eulerAngles), Quaternion.identity) < 0.5f &&
                            Mathf.Abs(part.scale.y - s) < 1e-3f && Mathf.Abs(part.scale.z - s) < 1e-3f &&
                            s > CopyShrink && s < 1.001f;
                if (!copy)
                {
                    break;
                }
                factor *= CopyShrink / s;
                i = parents[i];
            }
            return factor;
        }

        private static Mesh LoadMesh(string meshPath, Dictionary<string, Mesh> cache)
        {
            if (string.IsNullOrEmpty(meshPath))
            {
                return null;
            }
            if (!cache.TryGetValue(meshPath, out Mesh mesh))
            {
                mesh = AssetDatabase.LoadAssetAtPath<Mesh>(MeshRoot + meshPath);
                cache[meshPath] = mesh;
            }
            return mesh;
        }

        private static void AddToDatabase(List<SandLevelData> imported)
        {
            var database = AssetDatabase.LoadAssetAtPath<LevelDatabase>(DatabasePath);
            if (database == null)
            {
                Debug.LogWarning("[SandLayoutImporter] No LevelDatabase at " + DatabasePath + "; levels were not added.");
                return;
            }
            int added = 0;
            foreach (SandLevelData level in imported.OrderBy(l => LevelNumber(l.name)))
            {
                if (!database.levels.Contains(level))
                {
                    database.levels.Add(level);
                    added++;
                }
            }
            if (added > 0)
            {
                EditorUtility.SetDirty(database);
                Debug.Log($"[SandLayoutImporter] Added {added} level(s) to the end of the LevelDatabase.");
            }
        }

        private static string AssetGuid(Object asset) => AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(asset));

        private static int LevelNumber(string name)
        {
            string digits = new string(name.Where(char.IsDigit).ToArray());
            return int.TryParse(digits, out int n) ? n : int.MaxValue;
        }

        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder))
            {
                return;
            }
            string parent = Path.GetDirectoryName(folder)?.Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }
    }
}
