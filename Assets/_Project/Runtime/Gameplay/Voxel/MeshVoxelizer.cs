using System.Collections.Generic;
using UnityEngine;

namespace GameKit.Gameplay.Voxel
{
    /// <summary>One block's surface: triangles in model space (all parts share one space).</summary>
    public sealed class MeshPart
    {
        public string Name;
        public Vector3[] Vertices;
        public int[] Triangles;
        public Color Color = Color.white;
    }

    public sealed class MeshVoxelResult
    {
        public VoxelGrid Grid;
        /// <summary>Parent of each part (-1 = none), found from which part's inside contains which (or given by the caller).</summary>
        public int[] Parents;
        /// <summary>Cells owned by each part after nesting.</summary>
        public int[] CellCounts;
        /// <summary>World size of one cell in model units.</summary>
        public float CellSize;
        /// <summary>Model-space position of the grid corner: cell (x,y,z) spans Origin + [x, x+1) * CellSize.</summary>
        public Vector3 Origin;
        /// <summary>
        /// Flat triples (startCell, underBlock, runLength) of cells where another independent part tree also occupies
        /// the cell under the winning owner, ordered highest priority first so removing the top piece restores the
        /// covered piece instead of leaving a hole.
        /// </summary>
        public List<int> Overlaps = new List<int>();
        public readonly List<string> Warnings = new List<string>();
    }

    /// <summary>
    /// Turns mesh parts into a solid grain grid:
    /// 1) mark the cells each part's triangles pass through;
    /// 2) per part, flood the outside from the border of the part's own box; what the flood cannot reach is that
    ///    part's inside (a part with holes floods through and stays a shell);
    /// 3) part B is nested in part A when almost all of B's inside is also inside A (A bigger), unless the caller
    ///    already knows the parents;
    /// 4) each cell goes to the smallest part whose inside holds it; cells inside the whole model that no
    ///    closed part holds go to the nearest part surface.
    /// Every per-part step only walks that part's box, so models with hundreds of small parts stay fast.
    /// </summary>
    public static class MeshVoxelizer
    {
        /// <summary>Cells of one part, stored over the part's box (one empty cell of margin on every side).</summary>
        private sealed class PartCells
        {
            public Vector3Int Min;
            public Vector3Int Size;
            public bool[] Surface;
            public bool[] Solid;
            public int SolidCount;
            public int SurfaceCount;

            public bool Empty => Surface == null;

            public bool Contains(int x, int y, int z) =>
                x >= Min.x && y >= Min.y && z >= Min.z && x < Min.x + Size.x && y < Min.y + Size.y && z < Min.z + Size.z;

            public int Local(int x, int y, int z) => (x - Min.x) + Size.x * ((y - Min.y) + Size.y * (z - Min.z));
        }

        public static MeshVoxelResult Build(IReadOnlyList<MeshPart> parts, int resolution, float nestThreshold = 0.9f,
            IReadOnlyList<int> knownParents = null, bool autoNestUnlinked = false, float minInteriorShare = 0.25f)
        {
            var result = new MeshVoxelResult();
            int partCount = parts.Count;
            if (partCount == 0 || partCount > short.MaxValue)
            {
                result.Warnings.Add("No parts to voxelize.");
                result.Grid = new VoxelGrid(Vector3Int.one);
                result.Parents = new int[0];
                result.CellCounts = new int[0];
                return result;
            }

            Vector3Int size = GridFrame(parts, resolution, out Vector3 origin, out float cell);

            var grid = new VoxelGrid(size);
            int cells = grid.CellCount;
            result.Grid = grid;
            result.CellSize = cell;
            result.Origin = origin;

            // 1) + 2) Surfaces and insides, per part box.
            var anySurface = new bool[cells];
            var boxes = new PartCells[partCount];
            for (int p = 0; p < partCount; p++)
            {
                PartCells box = Rasterize(parts[p], grid, origin, cell);
                boxes[p] = box;
                if (box.Empty)
                {
                    result.Warnings.Add($"Part {p} ({parts[p].Name}) has no triangles in the grid.");
                    continue;
                }

                FillInside(box);
                for (int z = 0; z < box.Size.z; z++)
                for (int y = 0; y < box.Size.y; y++)
                for (int x = 0; x < box.Size.x; x++)
                {
                    if (box.Surface[x + box.Size.x * (y + box.Size.y * z)])
                    {
                        anySurface[grid.Index(box.Min.x + x, box.Min.y + y, box.Min.z + z)] = true;
                    }
                }
                if (box.SolidCount <= box.SurfaceCount && box.SurfaceCount > 30)
                {
                    result.Warnings.Add($"Part {p} ({parts[p].Name}) is not closed: kept as a shell, it cannot hide other parts.");
                }
            }

            // 3) Nesting.
            result.Parents = new int[partCount];
            if (knownParents != null && knownParents.Count == partCount)
            {
                for (int b = 0; b < partCount; b++)
                {
                    int parent = knownParents[b];
                    result.Parents[b] = parent >= 0 && parent < partCount && parent != b ? parent : -1;
                }
                if (autoNestUnlinked)
                {
                    for (int b = 0; b < partCount; b++)
                    {
                        if (result.Parents[b] >= 0)
                        {
                            continue;
                        }
                        int parent = FindParent(b, boxes, nestThreshold, minInteriorShare);
                        if (parent >= 0 && !LeadsTo(result.Parents, parent, b))
                        {
                            result.Parents[b] = parent;
                        }
                    }
                }
            }
            else
            {
                for (int b = 0; b < partCount; b++)
                {
                    result.Parents[b] = FindParent(b, boxes, nestThreshold, 0f);
                }
            }

            // 4) Owners: smallest containing part wins (bigger parts are written first, smaller ones overwrite).
            var order = new List<int>(partCount);
            for (int p = 0; p < partCount; p++)
            {
                if (!boxes[p].Empty && boxes[p].SolidCount > 0)
                {
                    order.Add(p);
                }
            }
            order.Sort((a, b) =>
            {
                int bySize = boxes[b].SolidCount.CompareTo(boxes[a].SolidCount);
                return bySize != 0 ? bySize : b.CompareTo(a);
            });
            foreach (int p in order)
            {
                PartCells box = boxes[p];
                for (int z = 0; z < box.Size.z; z++)
                for (int y = 0; y < box.Size.y; y++)
                for (int x = 0; x < box.Size.x; x++)
                {
                    if (box.Solid[x + box.Size.x * (y + box.Size.y * z)])
                    {
                        grid[grid.Index(box.Min.x + x, box.Min.y + y, box.Min.z + z)] = (short)p;
                    }
                }
            }
            ClaimCoveredParts(grid, boxes, order);
            if (SkinsOnTop)
            {
                PutSkinsOnTop(grid, boxes);
            }
            FillLooseInside(grid, anySurface);
            result.Overlaps = BuildOverlaps(grid, boxes, order, result.Parents);

            result.CellCounts = new int[partCount];
            for (int i = 0; i < cells; i++)
            {
                if (grid[i] >= 0)
                {
                    result.CellCounts[grid[i]]++;
                }
            }
            for (int p = 0; p < partCount; p++)
            {
                if (result.CellCounts[p] == 0 && !boxes[p].Empty && boxes[p].SolidCount > 0)
                {
                    result.Warnings.Add($"Part {p} ({parts[p].Name}) is fully covered by smaller parts and has no grains.");
                }
            }
            return result;
        }

        /// <summary>
        /// Step 5 on by default: thin parts lying on a body keep the cells their own surface passes through. Without it
        /// the smaller-volume rule lets the body win wherever the two surfaces meet, and the thin part (a cuff, a band,
        /// a skin) turns into a holey lattice the body shows through.
        /// </summary>
        public static bool SkinsOnTop = true;

        /// <summary>How much more "skin" (surface cells per solid cell) a part must be to take a surface cell.</summary>
        private const float SkinMargin = 1.5f;

        /// <summary>
        /// Cells a part's triangles pass through go to that part when it is clearly thinner (more of its cells are
        /// surface) than the current owner. Thinnest parts are applied last, so they end on top.
        /// </summary>
        private static void PutSkinsOnTop(VoxelGrid grid, PartCells[] boxes)
        {
            var skin = new float[boxes.Length];
            var parts = new List<int>();
            for (int p = 0; p < boxes.Length; p++)
            {
                if (boxes[p].Empty || boxes[p].SolidCount == 0)
                {
                    continue;
                }
                skin[p] = boxes[p].SurfaceCount / (float)boxes[p].SolidCount;
                parts.Add(p);
            }
            parts.Sort((a, b) => skin[a].CompareTo(skin[b]));

            foreach (int p in parts)
            {
                PartCells box = boxes[p];
                for (int z = 0; z < box.Size.z; z++)
                for (int y = 0; y < box.Size.y; y++)
                for (int x = 0; x < box.Size.x; x++)
                {
                    if (!box.Surface[x + box.Size.x * (y + box.Size.y * z)])
                    {
                        continue;
                    }
                    int i = grid.Index(box.Min.x + x, box.Min.y + y, box.Min.z + z);
                    short owner = grid[i];
                    if (owner >= 0 && owner != p && skin[p] > SkinMargin * skin[owner])
                    {
                        grid[i] = (short)p;
                    }
                }
            }
        }

        /// <summary>
        /// A part whose cells were all taken by overlapping parts (a small piece pressed into a sibling) takes back
        /// its own inside cells from parts that keep enough cells of their own, so every closed part stays tappable.
        /// </summary>
        private static void ClaimCoveredParts(VoxelGrid grid, PartCells[] boxes, List<int> order)
        {
            var owned = new int[boxes.Length];
            for (int i = 0; i < grid.CellCount; i++)
            {
                if (grid[i] >= 0)
                {
                    owned[grid[i]]++;
                }
            }

            // Smallest first, so a tiny part never steals from an even tinier one that claimed before it.
            for (int k = order.Count - 1; k >= 0; k--)
            {
                int p = order[k];
                if (owned[p] > 0)
                {
                    continue;
                }
                PartCells box = boxes[p];
                for (int z = 0; z < box.Size.z; z++)
                for (int y = 0; y < box.Size.y; y++)
                for (int x = 0; x < box.Size.x; x++)
                {
                    if (!box.Solid[x + box.Size.x * (y + box.Size.y * z)])
                    {
                        continue;
                    }
                    int i = grid.Index(box.Min.x + x, box.Min.y + y, box.Min.z + z);
                    int other = grid[i];
                    if (other >= 0 && other != p && owned[other] > 2 * box.SolidCount)
                    {
                        owned[other]--;
                        grid[i] = (short)p;
                        owned[p]++;
                    }
                }
            }
        }

        /// <summary>
        /// Grid placement <see cref="Build"/> uses for these parts: cell size from the longest side and the
        /// resolution, one empty cell of padding on every side (so the outside flood starts everywhere).
        /// </summary>
        public static Vector3Int GridFrame(IReadOnlyList<MeshPart> parts, int resolution, out Vector3 origin, out float cell)
        {
            Bounds bounds = ComputeBounds(parts);
            float maxExtent = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
            cell = Mathf.Max(1e-5f, maxExtent / Mathf.Max(4, resolution));
            origin = bounds.min - Vector3.one * cell;
            return new Vector3Int(
                Mathf.CeilToInt(bounds.size.x / cell) + 2,
                Mathf.CeilToInt(bounds.size.y / cell) + 2,
                Mathf.CeilToInt(bounds.size.z / cell) + 2);
        }

        /// <summary>
        /// Smallest bigger part that holds at least <paramref name="threshold"/> of part b's inside (and at least
        /// <paramref name="minInteriorShare"/> strictly inside the outer part's interior, so flush surface decals are
        /// not hidden), or -1.
        /// </summary>
        private static int FindParent(int b, PartCells[] boxes, float threshold, float minInteriorShare)
        {
            PartCells inner = boxes[b];
            if (inner.Empty || inner.SolidCount == 0)
            {
                return -1;
            }

            int best = -1;
            for (int a = 0; a < boxes.Length; a++)
            {
                PartCells outer = boxes[a];
                if (a == b || outer.Empty || outer.SolidCount <= inner.SolidCount)
                {
                    continue;
                }
                if (best >= 0 && outer.SolidCount >= boxes[best].SolidCount)
                {
                    continue;
                }
                if (inner.Min.x + inner.Size.x <= outer.Min.x || outer.Min.x + outer.Size.x <= inner.Min.x ||
                    inner.Min.y + inner.Size.y <= outer.Min.y || outer.Min.y + outer.Size.y <= inner.Min.y ||
                    inner.Min.z + inner.Size.z <= outer.Min.z || outer.Min.z + outer.Size.z <= inner.Min.z)
                {
                    continue;
                }

                int shared = 0;
                int interior = 0;
                for (int z = 0; z < inner.Size.z; z++)
                for (int y = 0; y < inner.Size.y; y++)
                for (int x = 0; x < inner.Size.x; x++)
                {
                    if (!inner.Solid[x + inner.Size.x * (y + inner.Size.y * z)])
                    {
                        continue;
                    }
                    int gx = inner.Min.x + x, gy = inner.Min.y + y, gz = inner.Min.z + z;
                    if (outer.Contains(gx, gy, gz))
                    {
                        int local = outer.Local(gx, gy, gz);
                        if (outer.Solid[local])
                        {
                            shared++;
                            if (!outer.Surface[local])
                            {
                                interior++;
                            }
                        }
                    }
                }
                if (shared >= threshold * inner.SolidCount && interior >= minInteriorShare * inner.SolidCount)
                {
                    best = a;
                }
            }
            return best;
        }

        private static bool LeadsTo(int[] parents, int from, int target)
        {
            for (int guard = 0; from >= 0 && from < parents.Length && guard < parents.Length; guard++)
            {
                if (from == target)
                {
                    return true;
                }
                from = parents[from];
            }
            return false;
        }

        private static Bounds ComputeBounds(IReadOnlyList<MeshPart> parts)
        {
            bool any = false;
            var bounds = new Bounds();
            foreach (MeshPart part in parts)
            {
                if (part.Vertices == null || part.Triangles == null)
                {
                    continue;
                }
                foreach (int index in part.Triangles)
                {
                    Vector3 v = part.Vertices[index];
                    if (!any)
                    {
                        bounds = new Bounds(v, Vector3.zero);
                        any = true;
                    }
                    else
                    {
                        bounds.Encapsulate(v);
                    }
                }
            }
            return bounds;
        }

        /// <summary>
        /// Marks every cell a triangle passes through (sampling the triangle at under half a cell) in a box around
        /// the part with one cell of margin, so the inside flood can start on the box border.
        /// </summary>
        private static PartCells Rasterize(MeshPart part, VoxelGrid grid, Vector3 origin, float cell)
        {
            var box = new PartCells();
            if (part.Vertices == null || part.Triangles == null || part.Triangles.Length < 3)
            {
                return box;
            }

            var min = new Vector3Int(int.MaxValue, int.MaxValue, int.MaxValue);
            var max = new Vector3Int(int.MinValue, int.MinValue, int.MinValue);
            foreach (int index in part.Triangles)
            {
                Vector3 g = (part.Vertices[index] - origin) / cell;
                var c = new Vector3Int(Mathf.FloorToInt(g.x), Mathf.FloorToInt(g.y), Mathf.FloorToInt(g.z));
                min = Vector3Int.Min(min, c);
                max = Vector3Int.Max(max, c);
            }
            Vector3Int gridMax = grid.Size - Vector3Int.one;
            min = Vector3Int.Max(Vector3Int.zero, min - Vector3Int.one);
            max = Vector3Int.Min(gridMax, max + Vector3Int.one);
            if (min.x > max.x || min.y > max.y || min.z > max.z)
            {
                return box;
            }

            box.Min = min;
            box.Size = max - min + Vector3Int.one;
            box.Surface = new bool[box.Size.x * box.Size.y * box.Size.z];

            float step = cell * 0.45f;
            int[] tris = part.Triangles;
            for (int t = 0; t + 2 < tris.Length; t += 3)
            {
                Vector3 a = part.Vertices[tris[t]], b = part.Vertices[tris[t + 1]], c = part.Vertices[tris[t + 2]];
                float longest = Mathf.Max((b - a).magnitude, Mathf.Max((c - b).magnitude, (a - c).magnitude));
                int n = Mathf.Clamp(Mathf.CeilToInt(longest / step), 1, 4096);
                for (int i = 0; i <= n; i++)
                {
                    for (int j = 0; j <= n - i; j++)
                    {
                        float u = (float)i / n, v = (float)j / n;
                        Vector3 p = a + (b - a) * u + (c - a) * v;
                        Vector3 g = (p - origin) / cell;
                        int x = Mathf.FloorToInt(g.x), y = Mathf.FloorToInt(g.y), z = Mathf.FloorToInt(g.z);
                        if (box.Contains(x, y, z))
                        {
                            int local = box.Local(x, y, z);
                            if (!box.Surface[local])
                            {
                                box.Surface[local] = true;
                                box.SurfaceCount++;
                            }
                        }
                    }
                }
            }

            if (box.SurfaceCount == 0)
            {
                box.Surface = null;
            }
            return box;
        }

        /// <summary>Inside of one part = cells of its box the outside flood (6-connected, blocked by its surface) cannot reach.</summary>
        private static void FillInside(PartCells box)
        {
            Vector3Int s = box.Size;
            int count = s.x * s.y * s.z;
            var outside = new bool[count];
            var queue = new Queue<int>();

            for (int z = 0; z < s.z; z++)
            for (int y = 0; y < s.y; y++)
            for (int x = 0; x < s.x; x++)
            {
                bool border = x == 0 || y == 0 || z == 0 || x == s.x - 1 || y == s.y - 1 || z == s.z - 1;
                int i = x + s.x * (y + s.y * z);
                if (border && !box.Surface[i])
                {
                    outside[i] = true;
                    queue.Enqueue(i);
                }
            }

            int sxy = s.x * s.y;
            while (queue.Count > 0)
            {
                int i = queue.Dequeue();
                int x = i % s.x, y = (i / s.x) % s.y, z = i / sxy;
                if (x + 1 < s.x) Visit(i + 1, box.Surface, outside, queue);
                if (x > 0) Visit(i - 1, box.Surface, outside, queue);
                if (y + 1 < s.y) Visit(i + s.x, box.Surface, outside, queue);
                if (y > 0) Visit(i - s.x, box.Surface, outside, queue);
                if (z + 1 < s.z) Visit(i + sxy, box.Surface, outside, queue);
                if (z > 0) Visit(i - sxy, box.Surface, outside, queue);
            }

            box.Solid = new bool[count];
            int solid = 0;
            for (int i = 0; i < count; i++)
            {
                if (!outside[i])
                {
                    box.Solid[i] = true;
                    solid++;
                }
            }
            box.SolidCount = solid;
        }

        private static void Visit(int i, bool[] blocked, bool[] visited, Queue<int> queue)
        {
            if (!visited[i] && !blocked[i])
            {
                visited[i] = true;
                queue.Enqueue(i);
            }
        }

        /// <summary>Inside = cells the outside flood (6-connected, blocked by the surface) cannot reach.</summary>
        private static bool[] FillInsideGrid(VoxelGrid grid, bool[] surface)
        {
            int cells = grid.CellCount;
            var outside = new bool[cells];
            var queue = new Queue<int>();
            Vector3Int s = grid.Size;

            for (int i = 0; i < cells; i++)
            {
                Vector3Int c = grid.Cell(i);
                bool border = c.x == 0 || c.y == 0 || c.z == 0 || c.x == s.x - 1 || c.y == s.y - 1 || c.z == s.z - 1;
                if (border && !surface[i])
                {
                    outside[i] = true;
                    queue.Enqueue(i);
                }
            }

            int sxy = s.x * s.y;
            while (queue.Count > 0)
            {
                int i = queue.Dequeue();
                int x = i % s.x, y = (i / s.x) % s.y, z = i / sxy;
                if (x + 1 < s.x) Visit(i + 1, surface, outside, queue);
                if (x > 0) Visit(i - 1, surface, outside, queue);
                if (y + 1 < s.y) Visit(i + s.x, surface, outside, queue);
                if (y > 0) Visit(i - s.x, surface, outside, queue);
                if (z + 1 < s.z) Visit(i + sxy, surface, outside, queue);
                if (z > 0) Visit(i - sxy, surface, outside, queue);
            }

            var inside = new bool[cells];
            for (int i = 0; i < cells; i++)
            {
                inside[i] = !outside[i];
            }
            return inside;
        }

        /// <summary>Cells inside the whole model that no closed part claimed take the owner of the nearest owned cell.</summary>
        private static void FillLooseInside(VoxelGrid grid, bool[] anySurface)
        {
            bool[] modelInside = FillInsideGrid(grid, anySurface);
            var queue = new Queue<int>();
            for (int i = 0; i < grid.CellCount; i++)
            {
                if (grid[i] >= 0)
                {
                    queue.Enqueue(i);
                }
            }

            while (queue.Count > 0)
            {
                int i = queue.Dequeue();
                Vector3Int c = grid.Cell(i);
                short owner = grid[i];
                Spread(grid, c.x + 1, c.y, c.z, owner, modelInside, queue);
                Spread(grid, c.x - 1, c.y, c.z, owner, modelInside, queue);
                Spread(grid, c.x, c.y + 1, c.z, owner, modelInside, queue);
                Spread(grid, c.x, c.y - 1, c.z, owner, modelInside, queue);
                Spread(grid, c.x, c.y, c.z + 1, owner, modelInside, queue);
                Spread(grid, c.x, c.y, c.z - 1, owner, modelInside, queue);
            }
        }

        private static void Spread(VoxelGrid grid, int x, int y, int z, short owner, bool[] modelInside, Queue<int> queue)
        {
            if (!grid.InBounds(x, y, z))
            {
                return;
            }
            int i = grid.Index(x, y, z);
            if (grid[i] < 0 && modelInside[i])
            {
                grid[i] = owner;
                queue.Enqueue(i);
            }
        }

        /// <summary>
        /// Records cells occupied by another independent part tree beneath the winning owner, as compact triples
        /// (startCell, underBlock, runLength) ordered from highest to lowest priority. When a smaller piece overlaps
        /// a thin shell and is later collected, restoring these cells keeps the shell whole instead of leaving holes.
        /// </summary>
        private static List<int> BuildOverlaps(VoxelGrid grid, PartCells[] boxes, List<int> order, int[] parents)
        {
            var overlaps = new List<int>();
            int partCount = boxes.Length;
            int[] root = ComputeRoots(parents, partCount);
            var emitted = new HashSet<long>();

            // Smallest parts first (reverse of Step 4 write order) so the highest-priority underneath owner comes first.
            for (int k = order.Count - 1; k >= 0; k--)
            {
                int p = order[k];
                PartCells box = boxes[p];
                int pRoot = root[p];
                int runStart = -1;
                int runLen = 0;

                for (int z = 0; z < box.Size.z; z++)
                for (int y = 0; y < box.Size.y; y++)
                for (int x = 0; x < box.Size.x; x++)
                {
                    if (!box.Solid[x + box.Size.x * (y + box.Size.y * z)])
                    {
                        continue;
                    }
                    int cell = grid.Index(box.Min.x + x, box.Min.y + y, box.Min.z + z);
                    short top = grid[cell];
                    if (top < 0 || top >= partCount || root[top] == pRoot)
                    {
                        continue;
                    }
                    long key = ((long)cell << 16) | (uint)pRoot;
                    if (!emitted.Add(key))
                    {
                        continue;
                    }
                    if (runLen > 0 && cell == runStart + runLen)
                    {
                        runLen++;
                    }
                    else
                    {
                        if (runLen > 0)
                        {
                            overlaps.Add(runStart);
                            overlaps.Add(p);
                            overlaps.Add(runLen);
                        }
                        runStart = cell;
                        runLen = 1;
                    }
                }

                if (runLen > 0)
                {
                    overlaps.Add(runStart);
                    overlaps.Add(p);
                    overlaps.Add(runLen);
                }
            }
            return overlaps;
        }

        /// <summary>Remaps block indices in an overlap triple list (dropping entries whose block was removed).</summary>
        public static List<int> RemapOverlaps(IReadOnlyList<int> overlaps, int[] remap)
        {
            var result = new List<int>();
            if (overlaps == null || remap == null)
            {
                return result;
            }
            for (int i = 0; i + 2 < overlaps.Count; i += 3)
            {
                int block = overlaps[i + 1];
                if (block >= 0 && block < remap.Length && remap[block] >= 0)
                {
                    result.Add(overlaps[i]);
                    result.Add(remap[block]);
                    result.Add(overlaps[i + 2]);
                }
            }
            return result;
        }

        /// <summary>Drops overlap entries that belong to the same parent chain as the cell's current owner.</summary>
        public static List<int> FilterSameRootOverlaps(IReadOnlyList<int> overlaps, VoxelGrid grid, IReadOnlyList<int> parents)
        {
            var result = new List<int>();
            if (overlaps == null || grid == null || parents == null)
            {
                return result;
            }
            int count = parents.Count;
            int[] root = ComputeRoots(parents, count);
            for (int i = 0; i + 2 < overlaps.Count; i += 3)
            {
                int start = overlaps[i];
                int under = overlaps[i + 1];
                int len = overlaps[i + 2];
                if (under < 0 || under >= count || len <= 0)
                {
                    continue;
                }
                int underRoot = root[under];
                int runStart = -1;
                int runLen = 0;
                for (int k = 0; k < len; k++)
                {
                    int cell = start + k;
                    if (cell < 0 || cell >= grid.CellCount)
                    {
                        continue;
                    }
                    short top = grid[cell];
                    bool keep = top >= 0 && top < count && root[top] != underRoot;
                    if (keep)
                    {
                        if (runLen == 0)
                        {
                            runStart = cell;
                        }
                        runLen++;
                    }
                    else if (runLen > 0)
                    {
                        result.Add(runStart);
                        result.Add(under);
                        result.Add(runLen);
                        runLen = 0;
                    }
                }
                if (runLen > 0)
                {
                    result.Add(runStart);
                    result.Add(under);
                    result.Add(runLen);
                }
            }
            return result;
        }

        private static int[] ComputeRoots(IReadOnlyList<int> parents, int count)
        {
            var root = new int[count];
            for (int p = 0; p < count; p++)
            {
                int r = p;
                for (int guard = 0; r >= 0 && r < count && parents[r] >= 0 && guard < count; guard++)
                {
                    r = parents[r];
                }
                root[p] = r;
            }
            return root;
        }
    }
}
