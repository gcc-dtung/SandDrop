using System.Collections.Generic;
using UnityEngine;

namespace GameKit.Gameplay.Voxel
{
    /// <summary>
    /// Builds one smooth, closed surface around the cells of a block (naive surface nets on a lightly blurred
    /// occupancy field, then Laplacian smoothing). Output vertices are in grid units: cell (x,y,z) spans [x, x+1).
    /// </summary>
    public static class SurfaceNets
    {
        private const float Iso = 0.5f;

        /// <summary>
        /// Fills vertices / triangles (cleared first). Returns false when the block has no cells left.
        /// <paramref name="excluded"/> (indexed by grid cell, may be null) drops cells, e.g. sand already drained away.
        /// </summary>
        public static bool Build(VoxelGrid grid, int block, List<Vector3> vertices, List<int> triangles, int smoothIterations = 2,
            bool[] excluded = null)
        {
            vertices.Clear();
            triangles.Clear();

            // Bounding box of the block, padded by 2 so the blurred field drops to zero inside the region.
            var min = new Vector3Int(int.MaxValue, int.MaxValue, int.MaxValue);
            var max = new Vector3Int(int.MinValue, int.MinValue, int.MinValue);
            for (int i = 0; i < grid.CellCount; i++)
            {
                if (grid[i] == block && (excluded == null || !excluded[i]))
                {
                    Vector3Int c = grid.Cell(i);
                    min = Vector3Int.Min(min, c);
                    max = Vector3Int.Max(max, c);
                }
            }
            if (min.x > max.x)
            {
                return false;
            }
            min -= new Vector3Int(2, 2, 2);
            max += new Vector3Int(2, 2, 2);
            var n = new Vector3Int(max.x - min.x + 1, max.y - min.y + 1, max.z - min.z + 1);

            // Samples sit at cell centres.
            float[] field = Occupancy(grid, block, min, n, excluded);
            field = Blur(field, n);

            // One vertex per sample cube that the surface crosses.
            var cubes = new Vector3Int(n.x - 1, n.y - 1, n.z - 1);
            var cubeVertex = new int[cubes.x * cubes.y * cubes.z];
            for (int i = 0; i < cubeVertex.Length; i++)
            {
                cubeVertex[i] = -1;
            }

            for (int z = 0; z < cubes.z; z++)
            {
                for (int y = 0; y < cubes.y; y++)
                {
                    for (int x = 0; x < cubes.x; x++)
                    {
                        if (TryCubeVertex(field, n, x, y, z, out Vector3 local))
                        {
                            cubeVertex[x + cubes.x * (y + cubes.y * z)] = vertices.Count;
                            // Sample (sx,sy,sz) is the centre of cell min + s.
                            vertices.Add(local + (Vector3)min + new Vector3(0.5f, 0.5f, 0.5f));
                        }
                    }
                }
            }

            // One quad per sample edge where inside / outside changes, joining the 4 cubes around that edge.
            for (int z = 0; z < n.z; z++)
            {
                for (int y = 0; y < n.y; y++)
                {
                    for (int x = 0; x < n.x; x++)
                    {
                        bool inside = Sample(field, n, x, y, z) >= Iso;
                        for (int axis = 0; axis < 3; axis++)
                        {
                            var p = new Vector3Int(x, y, z);
                            Vector3Int q = p + Unit(axis);
                            if (q.x >= n.x || q.y >= n.y || q.z >= n.z)
                            {
                                continue;
                            }
                            bool insideQ = Sample(field, n, q.x, q.y, q.z) >= Iso;
                            if (inside == insideQ)
                            {
                                continue;
                            }

                            Vector3Int eb = Unit((axis + 1) % 3), ec = Unit((axis + 2) % 3);
                            int v0 = CubeAt(cubeVertex, cubes, p - eb - ec);
                            int v1 = CubeAt(cubeVertex, cubes, p - ec);
                            int v2 = CubeAt(cubeVertex, cubes, p);
                            int v3 = CubeAt(cubeVertex, cubes, p - eb);
                            if (v0 < 0 || v1 < 0 || v2 < 0 || v3 < 0)
                            {
                                continue;
                            }

                            Vector3 outward = (Vector3)Unit(axis) * (inside ? 1f : -1f);
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
                    }
                }
            }

            Smooth(vertices, triangles, smoothIterations);
            return triangles.Count > 0;
        }

        private static float[] Occupancy(VoxelGrid grid, int block, Vector3Int min, Vector3Int n, bool[] excluded)
        {
            var field = new float[n.x * n.y * n.z];
            for (int z = 0; z < n.z; z++)
            {
                for (int y = 0; y < n.y; y++)
                {
                    for (int x = 0; x < n.x; x++)
                    {
                        int gx = min.x + x, gy = min.y + y, gz = min.z + z;
                        if (grid[gx, gy, gz] == block && (excluded == null || !excluded[grid.Index(gx, gy, gz)]))
                        {
                            field[x + n.x * (y + n.y * z)] = 1f;
                        }
                    }
                }
            }
            return field;
        }

        /// <summary>
        /// Separable [1 6 1] / 8 blur: rounds the stair steps, keeps flat faces on the voxel boundary and keeps
        /// one-cell-thin plates and two-cell-thin rods (a stronger blur would erase them).
        /// </summary>
        private static float[] Blur(float[] field, Vector3Int n)
        {
            float[] a = field, b = new float[field.Length];
            for (int axis = 0; axis < 3; axis++)
            {
                Vector3Int e = Unit(axis);
                for (int z = 0; z < n.z; z++)
                {
                    for (int y = 0; y < n.y; y++)
                    {
                        for (int x = 0; x < n.x; x++)
                        {
                            float c = Sample(a, n, x, y, z);
                            float l = Sample(a, n, x - e.x, y - e.y, z - e.z);
                            float r = Sample(a, n, x + e.x, y + e.y, z + e.z);
                            b[x + n.x * (y + n.y * z)] = (l + 6f * c + r) * 0.125f;
                        }
                    }
                }
                (a, b) = (b, a);
            }
            return a;
        }

        private static bool TryCubeVertex(float[] field, Vector3Int n, int x, int y, int z, out Vector3 vertex)
        {
            vertex = Vector3.zero;
            int count = 0;
            // 12 cube edges: from corner (i,j,k) along each axis.
            for (int axis = 0; axis < 3; axis++)
            {
                Vector3Int e = Unit(axis);
                Vector3Int eb = Unit((axis + 1) % 3), ec = Unit((axis + 2) % 3);
                for (int k = 0; k < 4; k++)
                {
                    Vector3Int a = new Vector3Int(x, y, z) + ((k & 1) != 0 ? eb : Vector3Int.zero) + ((k & 2) != 0 ? ec : Vector3Int.zero);
                    Vector3Int b = a + e;
                    float fa = Sample(field, n, a.x, a.y, a.z), fb = Sample(field, n, b.x, b.y, b.z);
                    if ((fa >= Iso) == (fb >= Iso))
                    {
                        continue;
                    }
                    float t = (Iso - fa) / (fb - fa);
                    vertex += Vector3.Lerp(a, b, t) - new Vector3(x, y, z);
                    count++;
                }
            }
            if (count == 0)
            {
                return false;
            }
            vertex = vertex / count + new Vector3(x, y, z);
            return true;
        }

        private static void Smooth(List<Vector3> vertices, List<int> triangles, int iterations)
        {
            if (iterations <= 0 || vertices.Count == 0)
            {
                return;
            }

            var neighbours = new List<int>[vertices.Count];
            for (int i = 0; i < neighbours.Length; i++)
            {
                neighbours[i] = new List<int>(6);
            }
            for (int t = 0; t < triangles.Count; t += 3)
            {
                Link(neighbours, triangles[t], triangles[t + 1]);
                Link(neighbours, triangles[t + 1], triangles[t + 2]);
                Link(neighbours, triangles[t + 2], triangles[t]);
            }

            var next = new Vector3[vertices.Count];
            for (int it = 0; it < iterations; it++)
            {
                for (int i = 0; i < vertices.Count; i++)
                {
                    List<int> nb = neighbours[i];
                    if (nb.Count == 0)
                    {
                        next[i] = vertices[i];
                        continue;
                    }
                    Vector3 avg = Vector3.zero;
                    foreach (int j in nb)
                    {
                        avg += vertices[j];
                    }
                    next[i] = Vector3.Lerp(vertices[i], avg / nb.Count, 0.5f);
                }
                for (int i = 0; i < vertices.Count; i++)
                {
                    vertices[i] = next[i];
                }
            }
        }

        private static void Link(List<int>[] neighbours, int a, int b)
        {
            if (!neighbours[a].Contains(b))
            {
                neighbours[a].Add(b);
            }
            if (!neighbours[b].Contains(a))
            {
                neighbours[b].Add(a);
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

        private static float Sample(float[] field, Vector3Int n, int x, int y, int z)
        {
            if (x < 0 || y < 0 || z < 0 || x >= n.x || y >= n.y || z >= n.z)
            {
                return 0f;
            }
            return field[x + n.x * (y + n.y * z)];
        }

        private static Vector3Int Unit(int axis)
        {
            return axis == 0 ? new Vector3Int(1, 0, 0) : axis == 1 ? new Vector3Int(0, 1, 0) : new Vector3Int(0, 0, 1);
        }
    }
}
