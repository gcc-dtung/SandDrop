using System;
using System.Collections.Generic;
using UnityEngine;

namespace GameKit.Gameplay.Voxel
{
    /// <summary>
    /// Builds one smooth, closed surface around the cells of a block (naive surface nets on a lightly blurred
    /// occupancy field, then Laplacian smoothing). Output vertices are in grid units: cell (x,y,z) spans [x, x+1).
    /// Work buffers live in a <see cref="Workspace"/>, so a remesh allocates nothing once they have grown.
    /// </summary>
    public static class SurfaceNets
    {
        private const float Iso = 0.5f;
        /// <summary>
        /// Occupancy of a cell owned by another solid block. Below <see cref="Iso"/> (so the neighbour never counts as
        /// inside) but high enough that the surface reaches ~0.45 cell into the neighbour: touching blocks overlap
        /// instead of leaving a seam, and one-cell tips wedged between other blocks are not blurred away. Kept that
        /// small so a block never pokes through a thin (1–2 cell) layer lying on it (0.45 here gave ~1.25 cells).
        /// </summary>
        private const float NeighbourOccupancy = 0.3f;
        /// <summary>Most distinct neighbours a surface-nets vertex can have (6 edges + quad diagonals is at most 18).</summary>
        private const int MaxNeighbours = 24;

        /// <summary>
        /// Work buffers for one build at a time. <see cref="MainThread"/> serves the main thread; a worker thread needs
        /// its own instance. Buffers only grow, so a rebuild allocates nothing once they are big enough.
        /// </summary>
        public sealed class Workspace
        {
            internal float[] FieldA = Array.Empty<float>();
            internal float[] FieldB = Array.Empty<float>();
            internal bool[] Inside = Array.Empty<bool>();
            internal int[] CubeVertex = Array.Empty<int>();
            internal int[] Neighbours = Array.Empty<int>();
            internal byte[] NeighbourCount = Array.Empty<byte>();
            internal Vector3[] Next = Array.Empty<Vector3>();
            internal readonly List<int> ScratchCells = new List<int>();
        }

        /// <summary>Workspace for builds on the main thread.</summary>
        public static readonly Workspace MainThread = new Workspace();

        /// <summary>
        /// Fills vertices / triangles (cleared first). Returns false when the block has no cells left.
        /// <paramref name="excluded"/> (indexed by grid cell, may be null) drops cells, e.g. sand already drained away.
        /// Scans the whole grid; prefer the overload that takes the block's cells when they are known.
        /// </summary>
        public static bool Build(VoxelGrid grid, int block, List<Vector3> vertices, List<int> triangles, int smoothIterations = 2,
            bool[] excluded = null)
        {
            List<int> cells = MainThread.ScratchCells;
            cells.Clear();
            for (int i = 0; i < grid.CellCount; i++)
            {
                if (grid[i] == block && (excluded == null || !excluded[i]))
                {
                    cells.Add(i);
                }
            }
            return Build(grid, cells, 0, vertices, triangles, smoothIterations);
        }

        /// <summary>
        /// Same as the block overload, for the cells <paramref name="cells"/>[<paramref name="start"/>..<paramref name="end"/>)
        /// (grid indices; end -1 = to the end of the list). Cost depends on the cells' bounding box, not on the whole
        /// grid. Without <paramref name="solidBlocks"/> it only reads the grid's size, so it is safe on a worker thread with its own
        /// <paramref name="workspace"/> (null = <see cref="MainThread"/>) while nobody writes that part of the cell list.
        /// With <paramref name="solidBlocks"/> (indexed by block) cells owned by another solid block (not
        /// <paramref name="self"/>) count as half full, so the surface closes against its neighbours; this reads cell
        /// owners from the grid.
        /// </summary>
        public static bool Build(VoxelGrid grid, IReadOnlyList<int> cells, int start, List<Vector3> vertices, List<int> triangles,
            int smoothIterations = 2, Workspace workspace = null, int end = -1, short self = -1, bool[] solidBlocks = null,
            bool blur = true)
        {
            Workspace ws = workspace ?? MainThread;
            vertices.Clear();
            triangles.Clear();
            if (cells == null)
            {
                return false;
            }
            end = end < 0 ? cells.Count : Mathf.Min(end, cells.Count);
            if (start >= end)
            {
                return false;
            }

            // Bounding box of the cells, padded by 2 so the blurred field drops to zero inside the region.
            var min = new Vector3Int(int.MaxValue, int.MaxValue, int.MaxValue);
            var max = new Vector3Int(int.MinValue, int.MinValue, int.MinValue);
            for (int i = start; i < end; i++)
            {
                Vector3Int c = grid.Cell(cells[i]);
                min = Vector3Int.Min(min, c);
                max = Vector3Int.Max(max, c);
            }
            min -= new Vector3Int(2, 2, 2);
            max += new Vector3Int(2, 2, 2);
            var n = new Vector3Int(max.x - min.x + 1, max.y - min.y + 1, max.z - min.z + 1);
            int sampleCount = n.x * n.y * n.z;

            // Samples sit at cell centres.
            Ensure(ref ws.FieldA, sampleCount);
            Ensure(ref ws.FieldB, sampleCount);
            Ensure(ref ws.Inside, sampleCount);
            Array.Clear(ws.FieldA, 0, sampleCount);
            for (int i = start; i < end; i++)
            {
                Vector3Int c = grid.Cell(cells[i]) - min;
                ws.FieldA[c.x + n.x * (c.y + n.y * c.z)] = 1f;
            }
            if (solidBlocks != null)
            {
                AddNeighbours(ws.FieldA, grid, min, n, self, solidBlocks);
            }
            // Without the blur the surface sits on the cell faces (straight, slightly bevelled voxels) and never bulges
            // past a block's own cells, so touching layers do not run over each other.
            float[] field = blur ? Blur(ws, n) : ws.FieldA;
            for (int i = 0; i < sampleCount; i++)
            {
                ws.Inside[i] = field[i] >= Iso;
            }

            // One vertex per sample cube that the surface crosses.
            var cubes = new Vector3Int(n.x - 1, n.y - 1, n.z - 1);
            int cubeCount = cubes.x * cubes.y * cubes.z;
            Ensure(ref ws.CubeVertex, cubeCount);
            int sx = 1, sy = n.x, sz = n.x * n.y;
            var minCorner = (Vector3)min;
            var half = new Vector3(0.5f, 0.5f, 0.5f);
            for (int z = 0; z < cubes.z; z++)
            {
                for (int y = 0; y < cubes.y; y++)
                {
                    int cube = cubes.x * (y + cubes.y * z);
                    int s = n.x * (y + n.y * z);
                    for (int x = 0; x < cubes.x; x++, cube++, s++)
                    {
                        bool c0 = ws.Inside[s];
                        // The surface crosses the cube only when its 8 corners are not all on one side.
                        if (ws.Inside[s + sx] == c0 && ws.Inside[s + sy] == c0 && ws.Inside[s + sx + sy] == c0 &&
                            ws.Inside[s + sz] == c0 && ws.Inside[s + sx + sz] == c0 && ws.Inside[s + sy + sz] == c0 &&
                            ws.Inside[s + sx + sy + sz] == c0)
                        {
                            ws.CubeVertex[cube] = -1;
                            continue;
                        }
                        ws.CubeVertex[cube] = vertices.Count;
                        // Sample (sx,sy,sz) is the centre of cell min + s.
                        vertices.Add(CubeVertex(field, n, x, y, z) + minCorner + half);
                    }
                }
            }

            // One quad per sample edge where inside / outside changes, joining the 4 cubes around that edge.
            for (int z = 0; z < n.z; z++)
            {
                for (int y = 0; y < n.y; y++)
                {
                    int s = n.x * (y + n.y * z);
                    for (int x = 0; x < n.x; x++, s++)
                    {
                        bool inP = ws.Inside[s];
                        if (x + 1 < n.x && ws.Inside[s + sx] != inP)
                        {
                            AddEdgeQuad(ws, vertices, triangles, cubes, new Vector3Int(x, y, z), 0, inP);
                        }
                        if (y + 1 < n.y && ws.Inside[s + sy] != inP)
                        {
                            AddEdgeQuad(ws, vertices, triangles, cubes, new Vector3Int(x, y, z), 1, inP);
                        }
                        if (z + 1 < n.z && ws.Inside[s + sz] != inP)
                        {
                            AddEdgeQuad(ws, vertices, triangles, cubes, new Vector3Int(x, y, z), 2, inP);
                        }
                    }
                }
            }

            Smooth(ws, vertices, triangles, smoothIterations);
            return triangles.Count > 0;
        }

        private static void AddNeighbours(float[] field, VoxelGrid grid, Vector3Int min, Vector3Int n, short self, bool[] solidBlocks)
        {
            for (int z = 0; z < n.z; z++)
            {
                for (int y = 0; y < n.y; y++)
                {
                    int s = n.x * (y + n.y * z);
                    for (int x = 0; x < n.x; x++, s++)
                    {
                        if (field[s] > 0f)
                        {
                            continue;
                        }
                        int gx = min.x + x, gy = min.y + y, gz = min.z + z;
                        short owner = grid[gx, gy, gz]; // Empty outside the grid.
                        if (owner >= 0 && owner != self && IsSolid(owner, solidBlocks) && IsDeep(grid, gx, gy, gz, solidBlocks))
                        {
                            field[s] = NeighbourOccupancy;
                        }
                    }
                }
            }
        }

        private static bool IsSolid(short owner, bool[] solidBlocks) => owner >= 0 && owner < solidBlocks.Length && solidBlocks[owner];

        /// <summary>
        /// A neighbour cell with solid cells on all 6 sides. Only those are reached into: a neighbour's skin (cells next
        /// to air) is left alone, so a block never pokes up through a thin layer lying on it (which flickered as
        /// specks of the colour underneath), while the seams below the skin still close.
        /// </summary>
        private static bool IsDeep(VoxelGrid grid, int x, int y, int z, bool[] solidBlocks)
        {
            return IsSolid(grid[x + 1, y, z], solidBlocks) && IsSolid(grid[x - 1, y, z], solidBlocks) &&
                   IsSolid(grid[x, y + 1, z], solidBlocks) && IsSolid(grid[x, y - 1, z], solidBlocks) &&
                   IsSolid(grid[x, y, z + 1], solidBlocks) && IsSolid(grid[x, y, z - 1], solidBlocks);
        }

        private static void AddEdgeQuad(Workspace ws, List<Vector3> vertices, List<int> triangles, Vector3Int cubes, Vector3Int p, int axis, bool inP)
        {
            Vector3Int eb = Unit((axis + 1) % 3), ec = Unit((axis + 2) % 3);
            int v0 = CubeAt(ws.CubeVertex, cubes, p - eb - ec);
            int v1 = CubeAt(ws.CubeVertex, cubes, p - ec);
            int v2 = CubeAt(ws.CubeVertex, cubes, p);
            int v3 = CubeAt(ws.CubeVertex, cubes, p - eb);
            if (v0 < 0 || v1 < 0 || v2 < 0 || v3 < 0)
            {
                return;
            }

            Vector3 outward = (Vector3)Unit(axis) * (inP ? 1f : -1f);
            Vector3 normal = Vector3.Cross(vertices[v1] - vertices[v0], vertices[v2] - vertices[v0]);
            if (Vector3.Dot(normal, outward) >= 0f)
            {
                AddQuad(triangles, v0, v1, v2, v3);
            }
            else
            {
                AddQuad(triangles, v0, v3, v2, v1);
            }
        }

        /// <summary>
        /// Separable [1 6 1] / 8 blur of the workspace's first field: rounds the stair steps, keeps flat faces on the voxel
        /// boundary and keeps one-cell-thin plates and two-cell-thin rods (a stronger blur would erase them).
        /// Samples outside the box count as 0.
        /// </summary>
        private static float[] Blur(Workspace ws, Vector3Int n)
        {
            float[] a = ws.FieldA, b = ws.FieldB;
            for (int axis = 0; axis < 3; axis++)
            {
                int stride = axis == 0 ? 1 : axis == 1 ? n.x : n.x * n.y;
                int length = axis == 0 ? n.x : axis == 1 ? n.y : n.z;
                for (int z = 0; z < n.z; z++)
                {
                    for (int y = 0; y < n.y; y++)
                    {
                        int i = n.x * (y + n.y * z);
                        for (int x = 0; x < n.x; x++, i++)
                        {
                            int along = axis == 0 ? x : axis == 1 ? y : z;
                            float c = a[i];
                            float l = along > 0 ? a[i - stride] : 0f;
                            float r = along + 1 < length ? a[i + stride] : 0f;
                            b[i] = (l + 6f * c + r) * 0.125f;
                        }
                    }
                }
                (a, b) = (b, a);
            }
            return a;
        }

        /// <summary>Average of the iso crossings on the 12 edges of cube (x,y,z), in box sample units.</summary>
        private static Vector3 CubeVertex(float[] field, Vector3Int n, int x, int y, int z)
        {
            Vector3 vertex = Vector3.zero;
            int count = 0;
            var origin = new Vector3(x, y, z);
            // 12 cube edges: from corner (i,j,k) along each axis.
            for (int axis = 0; axis < 3; axis++)
            {
                Vector3Int e = Unit(axis);
                Vector3Int eb = Unit((axis + 1) % 3), ec = Unit((axis + 2) % 3);
                for (int k = 0; k < 4; k++)
                {
                    Vector3Int a = new Vector3Int(x, y, z) + ((k & 1) != 0 ? eb : Vector3Int.zero) + ((k & 2) != 0 ? ec : Vector3Int.zero);
                    Vector3Int b = a + e;
                    float fa = field[a.x + n.x * (a.y + n.y * a.z)], fb = field[b.x + n.x * (b.y + n.y * b.z)];
                    if ((fa >= Iso) == (fb >= Iso))
                    {
                        continue;
                    }
                    float t = (Iso - fa) / (fb - fa);
                    vertex += Vector3.Lerp(a, b, t) - origin;
                    count++;
                }
            }
            return vertex / count + origin;
        }

        private static void Smooth(Workspace ws, List<Vector3> vertices, List<int> triangles, int iterations)
        {
            int count = vertices.Count;
            if (iterations <= 0 || count == 0)
            {
                return;
            }

            Ensure(ref ws.Neighbours, count * MaxNeighbours);
            Ensure(ref ws.NeighbourCount, count);
            Ensure(ref ws.Next, count);
            int[] neighbours = ws.Neighbours;
            byte[] neighbourCount = ws.NeighbourCount;
            Vector3[] next = ws.Next;
            Array.Clear(neighbourCount, 0, count);
            for (int t = 0; t < triangles.Count; t += 3)
            {
                Link(neighbours, neighbourCount, triangles[t], triangles[t + 1]);
                Link(neighbours, neighbourCount, triangles[t + 1], triangles[t + 2]);
                Link(neighbours, neighbourCount, triangles[t + 2], triangles[t]);
            }

            // Taubin smoothing: each iteration is a shrinking pass (lambda) followed by an inflating pass (mu). The
            // stair steps / wiggles of thin tails flatten out over many iterations, but the shape does not shrink the
            // way plain Laplacian smoothing would.
            for (int it = 0; it < iterations; it++)
            {
                SmoothPass(vertices, neighbours, neighbourCount, next, count, TaubinLambda);
                SmoothPass(vertices, neighbours, neighbourCount, next, count, TaubinMu);
            }
        }

        private const float TaubinLambda = 0.5f;
        private const float TaubinMu = -0.53f;

        private static void SmoothPass(List<Vector3> vertices, int[] neighbours, byte[] neighbourCount, Vector3[] next, int count, float factor)
        {
            for (int i = 0; i < count; i++)
            {
                int nbCount = neighbourCount[i];
                if (nbCount == 0)
                {
                    next[i] = vertices[i];
                    continue;
                }
                Vector3 avg = Vector3.zero;
                int from = i * MaxNeighbours;
                for (int j = 0; j < nbCount; j++)
                {
                    avg += vertices[neighbours[from + j]];
                }
                next[i] = Vector3.LerpUnclamped(vertices[i], avg / nbCount, factor);
            }
            for (int i = 0; i < count; i++)
            {
                vertices[i] = next[i];
            }
        }

        private static void Link(int[] neighbours, byte[] neighbourCount, int a, int b)
        {
            AddNeighbour(neighbours, neighbourCount, a, b);
            AddNeighbour(neighbours, neighbourCount, b, a);
        }

        private static void AddNeighbour(int[] neighbours, byte[] neighbourCount, int a, int b)
        {
            int from = a * MaxNeighbours;
            int count = neighbourCount[a];
            for (int j = 0; j < count; j++)
            {
                if (neighbours[from + j] == b)
                {
                    return;
                }
            }
            if (count < MaxNeighbours)
            {
                neighbours[from + count] = b;
                neighbourCount[a] = (byte)(count + 1);
            }
        }

        private static void AddQuad(List<int> triangles, int a, int b, int c, int d)
        {
            triangles.Add(a);
            triangles.Add(b);
            triangles.Add(c);
            triangles.Add(a);
            triangles.Add(c);
            triangles.Add(d);
        }

        private static int CubeAt(int[] cubeVertex, Vector3Int cubes, Vector3Int c)
        {
            if (c.x < 0 || c.y < 0 || c.z < 0 || c.x >= cubes.x || c.y >= cubes.y || c.z >= cubes.z)
            {
                return -1;
            }
            return cubeVertex[c.x + cubes.x * (c.y + cubes.y * c.z)];
        }

        private static void Ensure<T>(ref T[] buffer, int size)
        {
            if (buffer.Length < size)
            {
                buffer = new T[size + size / 4];
            }
        }

        private static Vector3Int Unit(int axis)
        {
            return axis == 0 ? new Vector3Int(1, 0, 0) : axis == 1 ? new Vector3Int(0, 1, 0) : new Vector3Int(0, 0, 1);
        }
    }
}
