using System.Collections.Generic;
using System.Linq;
using GameKit.Gameplay.Voxel;
using NUnit.Framework;
using UnityEngine;

namespace GameKit.Tests.Gameplay
{
    public class MeshVoxelizerTests
    {
        private static Mesh SphereMesh => Resources.GetBuiltinResource<Mesh>("New-Sphere.fbx");

        private static MeshPart Sphere(string name, Vector3 center, float diameter)
        {
            Mesh mesh = SphereMesh;
            return new MeshPart
            {
                Name = name,
                Vertices = mesh.vertices.Select(v => center + v * diameter).ToArray(),
                Triangles = mesh.triangles
            };
        }

        [Test]
        public void Build_SphereInsideSphere_InnerIsNestedAndOwnsCentre()
        {
            var parts = new List<MeshPart> { Sphere("Outer", Vector3.zero, 2f), Sphere("Inner", Vector3.zero, 0.8f) };

            MeshVoxelResult result = MeshVoxelizer.Build(parts, 30);

            Assert.AreEqual(-1, result.Parents[0]);
            Assert.AreEqual(0, result.Parents[1]);
            Assert.Greater(result.CellCounts[0], result.CellCounts[1]);
            Assert.Greater(result.CellCounts[1], 0);
            Vector3Int s = result.Grid.Size;
            Assert.AreEqual(1, result.Grid[s.x / 2, s.y / 2, s.z / 2]);
            CollectionAssert.IsEmpty(result.Warnings);
        }

        [Test]
        public void Build_KnownParents_AreUsedAsGiven()
        {
            var parts = new List<MeshPart> { Sphere("A", new Vector3(-1f, 0f, 0f), 1.5f), Sphere("B", new Vector3(1f, 0f, 0f), 1.5f) };

            MeshVoxelResult result = MeshVoxelizer.Build(parts, 30, knownParents: new[] { -1, 0 });

            Assert.AreEqual(-1, result.Parents[0]);
            Assert.AreEqual(0, result.Parents[1]);
            Assert.Greater(result.CellCounts[0], 0);
            Assert.Greater(result.CellCounts[1], 0);
        }

        [Test]
        public void Build_PartMostlyCoveredBySmallerParts_KeepsGrains()
        {
            // "Middle" is mostly covered by two slightly smaller overlapping balls; it must still own grains.
            var parts = new List<MeshPart>
            {
                Sphere("Big", Vector3.zero, 3f),
                Sphere("Middle", Vector3.zero, 0.9f),
                Sphere("Left", new Vector3(-0.2f, 0f, 0f), 0.85f),
                Sphere("Right", new Vector3(0.2f, 0f, 0f), 0.85f)
            };

            MeshVoxelResult result = MeshVoxelizer.Build(parts, 40);

            Assert.Greater(result.CellCounts[1], 0);
            Assert.Greater(result.CellCounts[0], result.CellCounts[1]);
        }

        [Test]
        public void Build_SolidSphere_FillsInside()
        {
            MeshVoxelResult result = MeshVoxelizer.Build(new List<MeshPart> { Sphere("Ball", Vector3.zero, 2f) }, 20);

            // Grid is 22 cells (20 + padding); a solid ball of diameter 20 cells is about 4189 cells.
            Assert.Greater(result.CellCounts[0], 3500);
            Assert.Less(result.CellCounts[0], 5000);
        }

        [Test]
        public void Build_SideBySide_NoNesting()
        {
            var parts = new List<MeshPart> { Sphere("A", new Vector3(-1f, 0f, 0f), 1.5f), Sphere("B", new Vector3(1f, 0f, 0f), 1.5f) };

            MeshVoxelResult result = MeshVoxelizer.Build(parts, 30);

            Assert.AreEqual(-1, result.Parents[0]);
            Assert.AreEqual(-1, result.Parents[1]);
        }

        [Test]
        public void Build_OpenPart_WarnsAndStaysShell()
        {
            Mesh mesh = SphereMesh;
            // Drop the top half of the triangles so the sphere has a hole.
            int[] half = mesh.triangles.Take(mesh.triangles.Length / 2).ToArray();
            var part = new MeshPart { Name = "Bowl", Vertices = mesh.vertices.Select(v => v * 2f).ToArray(), Triangles = half };

            MeshVoxelResult result = MeshVoxelizer.Build(new List<MeshPart> { part }, 20);

            Assert.IsTrue(result.Warnings.Any(w => w.Contains("not closed")));
        }

        [Test]
        public void RunLength_RoundTrips()
        {
            var grid = new VoxelGrid(new Vector3Int(3, 2, 2));
            grid[0, 0, 0] = 1;
            grid[1, 0, 0] = 1;
            grid[2, 1, 1] = 4;

            List<int> runs = grid.ToRunLength();
            VoxelGrid back = VoxelGrid.FromRunLength(grid.Size, runs);

            for (int i = 0; i < grid.CellCount; i++)
            {
                Assert.AreEqual(grid[i], back[i]);
            }
            Assert.Throws<System.ArgumentException>(() => VoxelGrid.FromRunLength(new Vector3Int(2, 2, 2), runs));
        }
    }
}
