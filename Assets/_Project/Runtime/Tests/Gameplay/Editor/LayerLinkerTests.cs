using System.Collections.Generic;
using GameKit.Gameplay.Level;
using GameKit.Gameplay.Voxel;
using NUnit.Framework;
using UnityEngine;

namespace GameKit.Tests.Gameplay
{
    public class LayerLinkerTests
    {
        private static void Fill(VoxelGrid grid, Vector3Int min, Vector3Int max, short block)
        {
            for (int z = min.z; z <= max.z; z++)
            for (int y = min.y; y <= max.y; y++)
            for (int x = min.x; x <= max.x; x++)
            {
                grid[x, y, z] = block;
            }
        }

        /// <summary>12³ grid: block 0 is a 1-cell shell (2..9), block 1 fills it (3..8).</summary>
        private static VoxelGrid ShellWithCore(bool hole)
        {
            var grid = new VoxelGrid(new Vector3Int(12, 12, 12));
            Fill(grid, new Vector3Int(2, 2, 2), new Vector3Int(9, 9, 9), 0);
            Fill(grid, new Vector3Int(3, 3, 3), new Vector3Int(8, 8, 8), 1);
            if (hole)
            {
                Fill(grid, new Vector3Int(5, 9, 5), new Vector3Int(6, 9, 6), 1); // Core peeks through the top.
            }
            return grid;
        }

        [Test]
        public void FindEnclosed_CorePeekingThroughShell_LinksCoreToShell()
        {
            List<LayerLinker.Link> links = LayerLinker.FindEnclosed(ShellWithCore(true), new[] { -1, -1 });

            Assert.AreEqual(1, links.Count);
            Assert.AreEqual(1, links[0].Child);
            Assert.AreEqual(0, links[0].Parent);
        }

        [Test]
        public void FindEnclosed_CoreFullyHidden_NotLinked()
        {
            // No surface: it already cannot be seen or tapped before the shell goes, so the level is left as it is.
            Assert.AreEqual(0, LayerLinker.FindEnclosed(ShellWithCore(false), new[] { -1, -1 }).Count);
        }

        [Test]
        public void FindEnclosed_ButtonOnBody_NotLinked()
        {
            var grid = new VoxelGrid(new Vector3Int(12, 12, 12));
            Fill(grid, new Vector3Int(1, 1, 1), new Vector3Int(10, 6, 10), 0); // Body.
            Fill(grid, new Vector3Int(4, 7, 4), new Vector3Int(7, 7, 7), 1);   // Flat button on top.

            Assert.AreEqual(0, LayerLinker.FindEnclosed(grid, new[] { -1, -1 }).Count);
        }

        [Test]
        public void FindEnclosed_InlayFlushWithSurface_NotLinked()
        {
            var grid = new VoxelGrid(new Vector3Int(12, 12, 12));
            Fill(grid, new Vector3Int(1, 1, 1), new Vector3Int(10, 6, 10), 0);
            Fill(grid, new Vector3Int(3, 6, 5), new Vector3Int(8, 6, 5), 1); // Stripe set into the top face.

            Assert.AreEqual(0, LayerLinker.FindEnclosed(grid, new[] { -1, -1 }).Count);
        }

        [Test]
        public void FindEnclosed_SideBySide_NotLinked()
        {
            var grid = new VoxelGrid(new Vector3Int(12, 6, 6));
            Fill(grid, new Vector3Int(0, 0, 0), new Vector3Int(5, 5, 5), 0);
            Fill(grid, new Vector3Int(6, 0, 0), new Vector3Int(11, 5, 5), 1);

            Assert.AreEqual(0, LayerLinker.FindEnclosed(grid, new[] { -1, -1 }).Count);
        }

        [Test]
        public void FindEnclosed_PiecesInsideCover_PeekingOneGoesUnderCover()
        {
            // Cover (block 0): 1-cell shell around a 3x3 grid of columns (blocks 1..9). The middle column (5) sees the
            // cover only up and down, other columns on its 4 sides; it peeks out of a hole in the top.
            var grid = new VoxelGrid(new Vector3Int(13, 9, 13));
            Fill(grid, new Vector3Int(1, 1, 1), new Vector3Int(11, 7, 11), 0);
            short block = 1;
            for (int cz = 0; cz < 3; cz++)
            for (int cx = 0; cx < 3; cx++)
            {
                Fill(grid, new Vector3Int(2 + cx * 3, 2, 2 + cz * 3), new Vector3Int(4 + cx * 3, 6, 4 + cz * 3), block++);
            }
            grid[6, 7, 6] = 5; // Hole in the top over the middle column.
            var parents = new int[10];
            for (int i = 0; i < parents.Length; i++)
            {
                parents[i] = -1;
            }

            Assert.AreEqual(0, LayerLinker.FindEnclosed(grid, parents).Count, "rule off by default");
            List<LayerLinker.Link> links = LayerLinker.FindEnclosed(grid, parents, canCover: b => true);

            Assert.AreEqual(1, links.Count);
            Assert.AreEqual(5, links[0].Child);
            Assert.AreEqual(0, links[0].Parent, "goes under the cover, not under a hidden neighbour column");

            // A piece that may not cover (e.g. not named "Hide…" in the source) keeps it as it is.
            Assert.AreEqual(0, LayerLinker.FindEnclosed(grid, parents, canCover: b => b != 0).Count);
        }

        [Test]
        public void FindEnclosed_ChildOfLinkedPieceKeepsItsParent()
        {
            // Core has its own inner layer (block 2, parent 1): the whole core tree goes under the shell.
            VoxelGrid grid = ShellWithCore(true);
            Fill(grid, new Vector3Int(5, 5, 5), new Vector3Int(6, 6, 6), 2);

            List<LayerLinker.Link> links = LayerLinker.FindEnclosed(grid, new[] { -1, -1, 1 });

            Assert.AreEqual(1, links.Count);
            Assert.AreEqual(1, links[0].Child);
            Assert.AreEqual(0, links[0].Parent);
        }
    }
}
