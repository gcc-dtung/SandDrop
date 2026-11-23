using System.Collections.Generic;
using GameKit.Gameplay.Voxel;
using NUnit.Framework;
using UnityEngine;

namespace GameKit.Tests.Gameplay
{
    public class SurfaceNetsTests
    {
        private static VoxelGrid Ball(int size, float radius, short block)
        {
            var grid = new VoxelGrid(new Vector3Int(size, size, size));
            var c = Vector3.one * (size * 0.5f);
            for (int z = 0; z < size; z++)
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                if ((new Vector3(x + 0.5f, y + 0.5f, z + 0.5f) - c).magnitude <= radius)
                {
                    grid[x, y, z] = block;
                }
            }
            return grid;
        }

        [Test]
        public void Build_Ball_ClosedSurfaceFacingOutward()
        {
            VoxelGrid grid = Ball(16, 5f, 0);
            var vertices = new List<Vector3>();
            var triangles = new List<int>();

            Assert.IsTrue(SurfaceNets.Build(grid, 0, vertices, triangles));

            var centre = Vector3.one * 8f;
            int outward = 0;
            for (int t = 0; t < triangles.Count; t += 3)
            {
                Vector3 a = vertices[triangles[t]], b = vertices[triangles[t + 1]], c = vertices[triangles[t + 2]];
                if (Vector3.Dot(Vector3.Cross(b - a, c - a), (a + b + c) / 3f - centre) > 0f)
                {
                    outward++;
                }
            }
            Assert.AreEqual(triangles.Count / 3, outward, "every face should point away from the centre");

            // Closed: every edge is shared by exactly two triangles.
            var edges = new Dictionary<long, int>();
            for (int t = 0; t < triangles.Count; t += 3)
            {
                for (int k = 0; k < 3; k++)
                {
                    int i = triangles[t + k], j = triangles[t + (k + 1) % 3];
                    long key = ((long)Mathf.Min(i, j) << 32) | (uint)Mathf.Max(i, j);
                    edges[key] = edges.TryGetValue(key, out int n) ? n + 1 : 1;
                }
            }
            foreach (int n in edges.Values)
            {
                Assert.AreEqual(2, n);
            }

            // Radius stays close to the voxel ball (smoothing shrinks a little).
            float r = 0f;
            foreach (Vector3 v in vertices)
            {
                r += (v - centre).magnitude;
            }
            Assert.AreEqual(5f, r / vertices.Count, 0.8f);
        }

        [Test]
        public void Build_ThinPlate_IsKept()
        {
            var grid = new VoxelGrid(new Vector3Int(10, 5, 10));
            for (int z = 2; z < 8; z++)
            for (int x = 2; x < 8; x++)
            {
                grid[x, 2, z] = 0;
            }

            Assert.IsFalse(SurfaceNets.Build(new VoxelGrid(grid.Size), 0, new List<Vector3>(), new List<int>()), "empty grid");
            Assert.IsTrue(SurfaceNets.Build(grid, 0, new List<Vector3>(), new List<int>()), "one-cell plate survives the blur");
        }

        [Test]
        public void Build_OnlyUsesOwnBlock()
        {
            VoxelGrid grid = Ball(16, 5f, 0);
            grid[8, 8, 8] = 1; // block 1 buried in the middle of block 0

            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            SurfaceNets.Build(grid, 0, vertices, triangles);
            int outerTriangles = triangles.Count;

            Assert.IsFalse(SurfaceNets.Build(grid, 2, vertices, triangles));
            Assert.Greater(outerTriangles, 0);
        }
        [Test]
        public void Build_ExcludedCells_AreLeftOut()
        {
            VoxelGrid grid = Ball(16, 5f, 0);
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            var excluded = new bool[grid.CellCount];

            // Drop the upper half: no vertex may sit well above the middle any more.
            for (int i = 0; i < grid.CellCount; i++)
            {
                excluded[i] = grid.Cell(i).y >= 8;
            }
            Assert.IsTrue(SurfaceNets.Build(grid, 0, vertices, triangles, 2, excluded));
            foreach (Vector3 v in vertices)
            {
                Assert.Less(v.y, 9f);
            }

            // Everything excluded: nothing to build.
            for (int i = 0; i < excluded.Length; i++)
            {
                excluded[i] = true;
            }
            Assert.IsFalse(SurfaceNets.Build(grid, 0, vertices, triangles, 2, excluded));
        }
    }
}
