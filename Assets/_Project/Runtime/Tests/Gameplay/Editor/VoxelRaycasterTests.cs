using GameKit.Gameplay.Voxel;
using NUnit.Framework;
using UnityEngine;

namespace GameKit.Tests.Gameplay
{
    public class VoxelRaycasterTests
    {
        private static readonly Vector3Int Size = new Vector3Int(4, 4, 4);

        private static int Index(int x, int y, int z) => x + Size.x * (y + Size.y * z);

        [Test]
        public void Raycast_FromOutside_HitsFirstSolidCell()
        {
            int target = Index(1, 2, 3);
            int behind = Index(1, 2, 1);

            int hit = VoxelRaycaster.Raycast(Size, new Vector3(1.5f, 2.5f, 10f), Vector3.back, i => i == target || i == behind);

            Assert.AreEqual(target, hit);
        }

        [Test]
        public void Raycast_SkipsNonSolid_ReachesCellBehind()
        {
            int behind = Index(1, 2, 1);

            int hit = VoxelRaycaster.Raycast(Size, new Vector3(1.5f, 2.5f, 10f), Vector3.back, i => i == behind);

            Assert.AreEqual(behind, hit);
        }

        [Test]
        public void Raycast_Miss_ReturnsMinusOne()
        {
            int hit = VoxelRaycaster.Raycast(Size, new Vector3(10f, 10f, 10f), Vector3.up, _ => true);

            Assert.AreEqual(-1, hit);
        }

        [Test]
        public void Raycast_Diagonal_HitsCornerCell()
        {
            int corner = Index(0, 0, 0);

            int hit = VoxelRaycaster.Raycast(Size, new Vector3(-1f, -1f, -1f), Vector3.one, i => i == corner);

            Assert.AreEqual(corner, hit);
        }

        [Test]
        public void Raycast_StartingInsideGrid_WalksForward()
        {
            int target = Index(3, 0, 0);

            int hit = VoxelRaycaster.Raycast(Size, new Vector3(0.5f, 0.5f, 0.5f), Vector3.right, i => i == target);

            Assert.AreEqual(target, hit);
        }

        [Test]
        public void RendererRaycast_RotatedModel_HitsBlockFacingRay()
        {
            var go = new GameObject("SandModelTest");
            try
            {
                var renderer = go.AddComponent<SandModelRenderer>();
                var grid = new VoxelGrid(new Vector3Int(2, 1, 1));
                grid[0, 0, 0] = 0;
                grid[1, 0, 0] = 1;
                LogAssert_IgnoreMaterialError();
                renderer.Build(grid, 1f, new[] { Color.red, Color.blue });

                // Unrotated: ray from +X hits block 1 (at +X).
                Assert.AreEqual(1, renderer.RaycastBlock(new Ray(new Vector3(5f, 0f, 0f), Vector3.left)));

                // Rotated 180° around Y: block 0 now faces +X.
                go.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
                Assert.AreEqual(0, renderer.RaycastBlock(new Ray(new Vector3(5f, 0f, 0f), Vector3.left)));

                renderer.SetCollected(0, true);
                Assert.AreEqual(1, renderer.RaycastBlock(new Ray(new Vector3(5f, 0f, 0f), Vector3.left)));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        private static void LogAssert_IgnoreMaterialError()
        {
            UnityEngine.TestTools.LogAssert.Expect(LogType.Error, "[SandModel] Surface material is not assigned.");
        }
    }
}
