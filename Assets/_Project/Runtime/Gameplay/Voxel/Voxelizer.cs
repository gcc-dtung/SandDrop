using System;
using System.Collections.Generic;
using UnityEngine;

namespace GameKit.Gameplay.Voxel
{
    public enum SandShapeKind
    {
        Box,
        Ellipsoid,
        /// <summary>Cylinder along the Y axis.</summary>
        Cylinder,
        /// <summary>Cone along Y, wide at the bottom, point at the top.</summary>
        Cone,
        /// <summary>Cone along Y, wide at the top, point at the bottom (ice-cream cone).</summary>
        ConeDown,
        /// <summary>Upper half of the ellipsoid (Center = middle of the flat base).</summary>
        Dome,
        /// <summary>Ring around Y. Size.x / Size.z = outer diameter, Size.y = tube thickness.</summary>
        Torus
    }

    /// <summary>A solid shape in grid units (1 = one grain). Center and Size are full extents.</summary>
    [Serializable]
    public struct SandShape
    {
        public SandShapeKind Kind;
        public Vector3 Center;
        public Vector3 Size;

        public SandShape(SandShapeKind kind, Vector3 center, Vector3 size)
        {
            Kind = kind;
            Center = center;
            Size = size;
        }

        public bool Contains(Vector3 p)
        {
            Vector3 half = Size * 0.5f;
            if (half.x <= 0f || half.y <= 0f || half.z <= 0f)
            {
                return false;
            }

            Vector3 d = p - Center;
            switch (Kind)
            {
                case SandShapeKind.Box:
                    return Mathf.Abs(d.x) <= half.x && Mathf.Abs(d.y) <= half.y && Mathf.Abs(d.z) <= half.z;
                case SandShapeKind.Ellipsoid:
                    float ex = d.x / half.x, ey = d.y / half.y, ez = d.z / half.z;
                    return ex * ex + ey * ey + ez * ez <= 1f;
                case SandShapeKind.Cylinder:
                    return Mathf.Abs(d.y) <= half.y && RadialSq(d, half) <= 1f;
                case SandShapeKind.Cone:
                case SandShapeKind.ConeDown:
                    if (Mathf.Abs(d.y) > half.y)
                    {
                        return false;
                    }
                    float t = (d.y + half.y) / (2f * half.y); // 0 at the bottom, 1 at the top
                    float radius = Kind == SandShapeKind.Cone ? 1f - t : t;
                    return RadialSq(d, half) <= radius * radius;
                case SandShapeKind.Dome:
                    if (d.y < 0f)
                    {
                        return false;
                    }
                    float dx = d.x / half.x, dy = d.y / half.y, dz = d.z / half.z;
                    return dx * dx + dy * dy + dz * dz <= 1f;
                case SandShapeKind.Torus:
                    float tube = half.y;
                    float ringX = Mathf.Max(0.01f, half.x - tube), ringZ = Mathf.Max(0.01f, half.z - tube);
                    // Distance from the ring's centre line, with an elliptical ring scaled to a circle.
                    float rx = d.x / ringX, rz = d.z / ringZ;
                    float len = Mathf.Sqrt(rx * rx + rz * rz);
                    if (len < 1e-5f)
                    {
                        return false;
                    }
                    float q = (len - 1f) * Mathf.Min(ringX, ringZ);
                    return q * q + d.y * d.y <= tube * tube;
                default:
                    return false;
            }
        }

        private static float RadialSq(Vector3 d, Vector3 half)
        {
            float cx = d.x / half.x, cz = d.z / half.z;
            return cx * cx + cz * cz;
        }
    }

    /// <summary>Grid of grains; each cell holds the owning block index or -1.</summary>
    public sealed class VoxelGrid
    {
        public const short Empty = -1;

        private readonly short[] owner;

        public VoxelGrid(Vector3Int size)
        {
            if (size.x <= 0 || size.y <= 0 || size.z <= 0)
            {
                throw new ArgumentException("Grid size must be positive.");
            }

            Size = size;
            owner = new short[size.x * size.y * size.z];
            for (int i = 0; i < owner.Length; i++)
            {
                owner[i] = Empty;
            }
        }

        public Vector3Int Size { get; }
        public int CellCount => owner.Length;

        public bool InBounds(int x, int y, int z) => x >= 0 && y >= 0 && z >= 0 && x < Size.x && y < Size.y && z < Size.z;

        public int Index(int x, int y, int z) => x + Size.x * (y + Size.y * z);

        public Vector3Int Cell(int index)
        {
            int x = index % Size.x;
            int yz = index / Size.x;
            return new Vector3Int(x, yz % Size.y, yz / Size.y);
        }

        public short this[int x, int y, int z]
        {
            get => InBounds(x, y, z) ? owner[Index(x, y, z)] : Empty;
            set => owner[Index(x, y, z)] = value;
        }

        public short this[int index]
        {
            get => owner[index];
            set => owner[index] = value;
        }

        /// <summary>Run-length pairs (owner, count) over all cells, for storing a baked grid in an asset.</summary>
        public List<int> ToRunLength()
        {
            var runs = new List<int>();
            int i = 0;
            while (i < owner.Length)
            {
                short value = owner[i];
                int start = i;
                while (i < owner.Length && owner[i] == value)
                {
                    i++;
                }
                runs.Add(value);
                runs.Add(i - start);
            }
            return runs;
        }

        /// <summary>Rebuilds a grid from <see cref="ToRunLength"/>. Throws when the runs do not match the size.</summary>
        public static VoxelGrid FromRunLength(Vector3Int size, IReadOnlyList<int> runs)
        {
            var grid = new VoxelGrid(size);
            int index = 0;
            for (int r = 0; r + 1 < runs.Count; r += 2)
            {
                short value = (short)runs[r];
                int count = runs[r + 1];
                if (count < 0 || index + count > grid.owner.Length)
                {
                    throw new ArgumentException("Baked cells do not match the grid size.");
                }
                for (int k = 0; k < count; k++)
                {
                    grid.owner[index++] = value;
                }
            }
            if (index != grid.owner.Length)
            {
                throw new ArgumentException("Baked cells do not match the grid size.");
            }
            return grid;
        }

        /// <summary>Cell indices owned by each block.</summary>
        public List<int>[] CellsByBlock(int blockCount)
        {
            var result = new List<int>[blockCount];
            for (int b = 0; b < blockCount; b++)
            {
                result[b] = new List<int>();
            }

            for (int i = 0; i < owner.Length; i++)
            {
                short b = owner[i];
                if (b >= 0 && b < blockCount)
                {
                    result[b].Add(i);
                }
            }
            return result;
        }

        /// <summary>
        /// Gives every crumb (a piece of a block of at most <paramref name="maxSize"/> cells, cut off from the rest of
        /// that block) to the block most of its neighbours belong to. Baking pieces whose surfaces nearly overlap
        /// leaves hundreds of such crumbs, drawn as specks of another colour. A block that is only crumbs, and crumbs
        /// with no other block next to them, are left alone. Returns how many cells changed owner.
        /// </summary>
        public int MergeCrumbs(int maxSize)
        {
            if (maxSize <= 0)
            {
                return 0;
            }

            var label = new int[owner.Length];
            int nextLabel = 0;
            var queue = new List<int>();
            var crumbs = new List<int[]>();
            var votes = new Dictionary<short, int>();
            int moved = 0;
            int blockCount = 0;
            foreach (short o in owner)
            {
                blockCount = Mathf.Max(blockCount, o + 1);
            }
            List<int>[] cellsByBlock = CellsByBlock(blockCount);

            for (int b = 0; b < blockCount; b++)
            {
                crumbs.Clear();
                int biggest = 0;
                foreach (int start in cellsByBlock[b])
                {
                    if (label[start] != 0)
                    {
                        continue;
                    }
                    nextLabel++;
                    queue.Clear();
                    queue.Add(start);
                    label[start] = nextLabel;
                    for (int head = 0; head < queue.Count; head++)
                    {
                        Vector3Int c = Cell(queue[head]);
                        Visit(c.x + 1, c.y, c.z, b, nextLabel, label, queue);
                        Visit(c.x - 1, c.y, c.z, b, nextLabel, label, queue);
                        Visit(c.x, c.y + 1, c.z, b, nextLabel, label, queue);
                        Visit(c.x, c.y - 1, c.z, b, nextLabel, label, queue);
                        Visit(c.x, c.y, c.z + 1, b, nextLabel, label, queue);
                        Visit(c.x, c.y, c.z - 1, b, nextLabel, label, queue);
                    }
                    biggest = Mathf.Max(biggest, queue.Count);
                    if (queue.Count <= maxSize)
                    {
                        crumbs.Add(queue.ToArray());
                    }
                }
                if (biggest <= maxSize)
                {
                    continue; // All crumbs: keep the block as it is rather than lose it.
                }

                foreach (int[] crumb in crumbs)
                {
                    votes.Clear();
                    foreach (int cell in crumb)
                    {
                        Vector3Int c = Cell(cell);
                        Vote(c.x + 1, c.y, c.z, b, votes);
                        Vote(c.x - 1, c.y, c.z, b, votes);
                        Vote(c.x, c.y + 1, c.z, b, votes);
                        Vote(c.x, c.y - 1, c.z, b, votes);
                        Vote(c.x, c.y, c.z + 1, b, votes);
                        Vote(c.x, c.y, c.z - 1, b, votes);
                    }
                    short best = Empty;
                    int bestVotes = 0;
                    foreach (KeyValuePair<short, int> vote in votes)
                    {
                        if (vote.Value > bestVotes || (vote.Value == bestVotes && vote.Key < best))
                        {
                            best = vote.Key;
                            bestVotes = vote.Value;
                        }
                    }
                    if (best == Empty)
                    {
                        continue;
                    }
                    foreach (int cell in crumb)
                    {
                        owner[cell] = best;
                    }
                    moved += crumb.Length;
                }
            }
            return moved;
        }

        private void Visit(int x, int y, int z, int block, int value, int[] label, List<int> queue)
        {
            if (!InBounds(x, y, z))
            {
                return;
            }
            int i = Index(x, y, z);
            if (label[i] == 0 && owner[i] == block)
            {
                label[i] = value;
                queue.Add(i);
            }
        }

        private void Vote(int x, int y, int z, int block, Dictionary<short, int> votes)
        {
            short other = this[x, y, z]; // Empty outside the grid.
            if (other >= 0 && other != block)
            {
                votes.TryGetValue(other, out int count);
                votes[other] = count + 1;
            }
        }
    }

    /// <summary>Turns block shapes into a grain grid. Inner blocks overwrite the blocks around them.</summary>
    public static class Voxelizer
    {
        /// <param name="shapesByBlock">Shapes of each block (a block is the union of its shapes).</param>
        /// <param name="parents">Parent (enclosing) block of each block, -1 for none.</param>
        public static VoxelGrid Build(Vector3Int size, IReadOnlyList<IReadOnlyList<SandShape>> shapesByBlock, IReadOnlyList<int> parents)
        {
            if (shapesByBlock.Count > short.MaxValue)
            {
                throw new ArgumentException("Too many blocks.");
            }

            var grid = new VoxelGrid(size);
            int[] order = OrderByDepth(parents, shapesByBlock.Count);

            foreach (int block in order)
            {
                var shapes = shapesByBlock[block];
                if (shapes == null)
                {
                    continue;
                }

                foreach (SandShape shape in shapes)
                {
                    Fill(grid, shape, (short)block);
                }
            }
            return grid;
        }

        /// <summary>Block indices with outer blocks first, so nested blocks are written after (and over) them.</summary>
        public static int[] OrderByDepth(IReadOnlyList<int> parents, int count)
        {
            var depth = new int[count];
            for (int i = 0; i < count; i++)
            {
                int d = 0;
                int p = ParentOf(parents, i);
                while (p >= 0 && p < count && d <= count) // d <= count stops on cycles
                {
                    d++;
                    p = ParentOf(parents, p);
                }
                depth[i] = d;
            }

            var order = new int[count];
            for (int i = 0; i < count; i++)
            {
                order[i] = i;
            }
            Array.Sort(order, (a, b) => depth[a] != depth[b] ? depth[a].CompareTo(depth[b]) : a.CompareTo(b));
            return order;
        }

        private static int ParentOf(IReadOnlyList<int> parents, int index)
        {
            return parents != null && index < parents.Count ? parents[index] : -1;
        }

        private static void Fill(VoxelGrid grid, SandShape shape, short block)
        {
            Vector3 half = shape.Size * 0.5f;
            int minX = Mathf.Max(0, Mathf.FloorToInt(shape.Center.x - half.x));
            int minY = Mathf.Max(0, Mathf.FloorToInt(shape.Center.y - half.y));
            int minZ = Mathf.Max(0, Mathf.FloorToInt(shape.Center.z - half.z));
            int maxX = Mathf.Min(grid.Size.x - 1, Mathf.CeilToInt(shape.Center.x + half.x));
            int maxY = Mathf.Min(grid.Size.y - 1, Mathf.CeilToInt(shape.Center.y + half.y));
            int maxZ = Mathf.Min(grid.Size.z - 1, Mathf.CeilToInt(shape.Center.z + half.z));

            for (int z = minZ; z <= maxZ; z++)
            {
                for (int y = minY; y <= maxY; y++)
                {
                    for (int x = minX; x <= maxX; x++)
                    {
                        if (shape.Contains(new Vector3(x + 0.5f, y + 0.5f, z + 0.5f)))
                        {
                            grid[x, y, z] = block;
                        }
                    }
                }
            }
        }
    }
}
