using System.Collections.Generic;
using GameKit.Gameplay.Board;
using GameKit.Gameplay.Level;
using GameKit.Gameplay.Voxel;
using NUnit.Framework;
using UnityEngine;

namespace GameKit.Tests.Gameplay
{
    public class SandLevelTests
    {
        private readonly List<Object> created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (Object o in created)
            {
                Object.DestroyImmediate(o);
            }
            created.Clear();
        }

        private static SandShape Box(Vector3 center, Vector3 size) => new SandShape(SandShapeKind.Box, center, size);

        [Test]
        public void Voxelizer_Box_FillsExpectedCells()
        {
            var shapes = new List<IReadOnlyList<SandShape>> { new[] { Box(new Vector3(2, 2, 2), new Vector3(2, 2, 2)) } };

            VoxelGrid grid = Voxelizer.Build(new Vector3Int(4, 4, 4), shapes, new[] { -1 });

            Assert.AreEqual(8, grid.CellsByBlock(1)[0].Count);
            Assert.AreEqual(0, grid[1, 1, 1]);
            Assert.AreEqual(VoxelGrid.Empty, grid[0, 0, 0]);
        }

        [Test]
        public void Voxelizer_InnerBlockOverwritesOuter_EvenWhenListedFirst()
        {
            var inner = new[] { Box(new Vector3(3, 3, 3), new Vector3(2, 2, 2)) };
            var outer = new[] { Box(new Vector3(3, 3, 3), new Vector3(6, 6, 6)) };
            var shapes = new List<IReadOnlyList<SandShape>> { inner, outer };

            VoxelGrid grid = Voxelizer.Build(new Vector3Int(6, 6, 6), shapes, new[] { 1, -1 });
            var cells = grid.CellsByBlock(2);

            Assert.AreEqual(8, cells[0].Count);
            Assert.AreEqual(216 - 8, cells[1].Count);
            Assert.AreEqual(0, grid[3, 3, 3]);
        }

        [Test]
        public void Voxelizer_Ellipsoid_StaysInsideBoundingBox()
        {
            var shapes = new List<IReadOnlyList<SandShape>>
            {
                new[] { new SandShape(SandShapeKind.Ellipsoid, new Vector3(5, 5, 5), new Vector3(10, 10, 10)) }
            };

            VoxelGrid grid = Voxelizer.Build(new Vector3Int(10, 10, 10), shapes, null);
            int count = grid.CellsByBlock(1)[0].Count;

            Assert.Greater(count, 400);
            Assert.Less(count, 1000);
            Assert.AreEqual(VoxelGrid.Empty, grid[0, 0, 0]);
        }

        [Test]
        public void Shapes_ConeDomeTorus_ContainExpectedPoints()
        {
            var cone = new SandShape(SandShapeKind.Cone, Vector3.zero, new Vector3(10, 10, 10));
            Assert.IsTrue(cone.Contains(new Vector3(4.5f, -4.9f, 0)));   // wide base
            Assert.IsFalse(cone.Contains(new Vector3(4.5f, 4.5f, 0)));   // narrow top

            var coneDown = new SandShape(SandShapeKind.ConeDown, Vector3.zero, new Vector3(10, 10, 10));
            Assert.IsTrue(coneDown.Contains(new Vector3(4.5f, 4.9f, 0)));
            Assert.IsFalse(coneDown.Contains(new Vector3(4.5f, -4.5f, 0)));

            var dome = new SandShape(SandShapeKind.Dome, Vector3.zero, new Vector3(10, 10, 10));
            Assert.IsTrue(dome.Contains(new Vector3(0, 4f, 0)));
            Assert.IsFalse(dome.Contains(new Vector3(0, -1f, 0)));

            var torus = new SandShape(SandShapeKind.Torus, Vector3.zero, new Vector3(10, 2, 10));
            Assert.IsTrue(torus.Contains(new Vector3(4f, 0, 0)));        // on the ring
            Assert.IsFalse(torus.Contains(Vector3.zero));                // hole
            Assert.IsFalse(torus.Contains(new Vector3(4f, 1.5f, 0)));    // above the tube
        }

        [Test]
        public void GrainMesh_FacesPointOutward()
        {
            Mesh mesh = GrainMesh.CreateIcosahedron();
            created.Add(mesh);
            Vector3[] v = mesh.vertices;
            int[] tris = mesh.triangles;

            Assert.AreEqual(60, tris.Length);
            for (int i = 0; i < tris.Length; i += 3)
            {
                Vector3 a = v[tris[i]], b = v[tris[i + 1]], c = v[tris[i + 2]];
                Assert.Greater(Vector3.Dot(Vector3.Cross(b - a, c - a), a + b + c), 0f);
            }
        }

        [Test]
        public void VoxelGrid_CellRoundTrips()
        {
            var grid = new VoxelGrid(new Vector3Int(3, 4, 5));

            Assert.AreEqual(new Vector3Int(2, 3, 4), grid.Cell(grid.Index(2, 3, 4)));
        }

        [Test]
        public void ValidateStructure_GoodLevel_NoErrors()
        {
            var blocks = new[] { new BoardBlock(0), new BoardBlock(0), new BoardBlock(0, 0) };

            CollectionAssert.IsEmpty(LevelValidator.ValidateStructure(blocks, new[] { 0 }, 1));
        }

        [Test]
        public void ValidateStructure_ReportsBadCountsColoursAndCycles()
        {
            var blocks = new[] { new BoardBlock(0, 1), new BoardBlock(0, 0), new BoardBlock(5) };

            var errors = LevelValidator.ValidateStructure(blocks, new[] { 0, 2 }, 2);

            Assert.IsTrue(errors.Exists(e => e.Contains("not in the palette")));
            Assert.IsTrue(errors.Exists(e => e.Contains("loops")));
            Assert.IsTrue(errors.Exists(e => e.Contains("multiple of 3")));
            Assert.IsTrue(errors.Exists(e => e.Contains("no block uses it")));
        }

        [Test]
        public void Validate_BlockFullyCovered_ReportsNoGrains()
        {
            var level = ScriptableObject.CreateInstance<SandLevelData>();
            created.Add(level);
            level.gridSize = new Vector3Int(4, 4, 4);
            level.palette = new[] { Color.red };
            level.trayColorQueue = new List<int> { 0 };
            for (int i = 0; i < 3; i++)
            {
                level.blocks.Add(new SandBlockDef { name = "b" + i, colorIndex = 0, parent = i == 0 ? -1 : 0 });
            }
            level.blocks[0].shapes.Add(Box(new Vector3(2, 2, 2), new Vector3(4, 4, 4)));
            level.blocks[1].shapes.Add(Box(new Vector3(1, 1, 1), new Vector3(2, 2, 2)));
            // Block 2 has no shapes.

            var errors = LevelValidator.Validate(level);

            Assert.AreEqual(1, errors.Count, string.Join("\n", errors));
            StringAssert.Contains("Block 2", errors[0]);
        }

        [Test]
        public void Solver_FindsOrderThatNeedsWaitingRow()
        {
            // Two red outer blocks hide blue blocks; trays start red + green.
            var blocks = new[]
            {
                new BoardBlock(0), new BoardBlock(0), new BoardBlock(0),
                new BoardBlock(1, 0), new BoardBlock(1, 1), new BoardBlock(1),
                new BoardBlock(2), new BoardBlock(2), new BoardBlock(2)
            };

            var result = LevelSolver.Solve(blocks, new[] { 0, 2, 1 }, out var order);

            Assert.AreEqual(LevelSolver.Result.Solvable, result);
            var board = new SandBoard(blocks, new[] { 0, 2, 1 }, lockedTrays: 0);
            foreach (int i in order)
            {
                board.Collect(i);
            }
            Assert.AreEqual(BoardState.Won, board.State);
        }

        [Test]
        public void Solver_ImpossibleLevel_ReportsUnsolvable()
        {
            // Six greens nested one inside the other must all go before any red / blue,
            // but no green tray is open yet: the waiting row overflows.
            var blocks = new List<BoardBlock>();
            for (int i = 0; i < 6; i++)
            {
                blocks.Add(new BoardBlock(2, i - 1));
            }
            for (int i = 0; i < 3; i++)
            {
                blocks.Add(new BoardBlock(0, 5));
                blocks.Add(new BoardBlock(1, 5));
            }

            var result = LevelSolver.Solve(blocks, new[] { 0, 1, 2, 2 }, out _);

            Assert.AreEqual(LevelSolver.Result.Unsolvable, result);
        }
    }
}
