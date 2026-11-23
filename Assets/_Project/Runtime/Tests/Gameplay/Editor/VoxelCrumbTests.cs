using GameKit.Gameplay.Voxel;
using NUnit.Framework;
using UnityEngine;

namespace GameKit.Tests.Gameplay
{
    public class VoxelCrumbTests
    {
        /// <summary>8x8x8 grid: block 0 fills the bottom half, block 1 the top half.</summary>
        private static VoxelGrid TwoHalves()
        {
            var grid = new VoxelGrid(new Vector3Int(8, 8, 8));
            for (int z = 0; z < 8; z++)
            for (int y = 0; y < 8; y++)
            for (int x = 0; x < 8; x++)
            {
                grid[x, y, z] = (short)(y < 4 ? 0 : 1);
            }
            return grid;
        }

        [Test]
        public void MergeCrumbs_LoneCellInsideOtherBlock_JoinsThatBlock()
        {
            VoxelGrid grid = TwoHalves();
            grid[3, 6, 3] = 0; // A crumb of block 0 inside block 1.

            int moved = grid.MergeCrumbs(8);

            Assert.AreEqual(1, moved);
            Assert.AreEqual(1, grid[3, 6, 3]);
            Assert.AreEqual(0, grid[3, 1, 3], "the main part of block 0 stays");
        }

        [Test]
        public void MergeCrumbs_PieceBiggerThanLimit_Stays()
        {
            VoxelGrid grid = TwoHalves();
            for (int x = 2; x < 5; x++)
            for (int z = 2; z < 5; z++)
            {
                grid[x, 6, z] = 0; // 9 cells, over the limit.
            }

            Assert.AreEqual(0, grid.MergeCrumbs(8));
            Assert.AreEqual(0, grid[3, 6, 3]);
        }

        [Test]
        public void MergeCrumbs_BlockMadeOnlyOfCrumbs_IsKept()
        {
            VoxelGrid grid = TwoHalves();
            grid[1, 6, 1] = 2;
            grid[5, 6, 5] = 2; // Block 2 is two lone cells: nothing bigger to keep, so it stays.

            Assert.AreEqual(0, grid.MergeCrumbs(8));
            Assert.AreEqual(2, grid[1, 6, 1]);
            Assert.AreEqual(2, grid[5, 6, 5]);
        }

        [Test]
        public void MergeCrumbs_CrumbWithOnlyAirAround_Stays()
        {
            var grid = new VoxelGrid(new Vector3Int(6, 6, 6));
            for (int x = 0; x < 3; x++)
            for (int y = 0; y < 3; y++)
            for (int z = 0; z < 3; z++)
            {
                grid[x, y, z] = 0;
            }
            grid[5, 5, 5] = 0; // Floating crumb of block 0, no other block next to it.

            Assert.AreEqual(0, grid.MergeCrumbs(8));
            Assert.AreEqual(0, grid[5, 5, 5]);
        }
    }
}
