using GameKit.Gameplay.Visuals;
using NUnit.Framework;
using UnityEngine;

namespace GameKit.Tests.Gameplay
{
    public class ProceduralMeshTests
    {
        private static void AssertFacesMatchNormals(Mesh mesh)
        {
            Vector3[] v = mesh.vertices;
            Vector3[] n = mesh.normals;
            int[] t = mesh.triangles;
            Assert.Greater(t.Length, 0, mesh.name);
            for (int i = 0; i < t.Length; i += 3)
            {
                Vector3 a = v[t[i]], b = v[t[i + 1]], c = v[t[i + 2]];
                Vector3 face = Vector3.Cross(b - a, c - a);
                if (face.sqrMagnitude < 1e-14f)
                {
                    continue; // degenerate triangle at a pole
                }
                Assert.GreaterOrEqual(Vector3.Dot(face, n[t[i]] + n[t[i + 1]] + n[t[i + 2]]), 0f, $"{mesh.name} triangle {i / 3}");
            }
        }

        [Test]
        public void AllTrayMeshes_FaceOutward()
        {
            foreach (Mesh mesh in new[]
                     {
                         ProceduralMesh.Jar(), ProceduralMesh.JarSand(), ProceduralMesh.JarLid(), ProceduralMesh.Plate(),
                         ProceduralMesh.TrayBase(), ProceduralMesh.TrayFloor(), ProceduralMesh.Badge()
                     })
            {
                try
                {
                    AssertFacesMatchNormals(mesh);
                }
                finally
                {
                    Object.DestroyImmediate(mesh);
                }
            }
        }

        [Test]
        public void RoundedBox_KeepsSize()
        {
            Mesh mesh = ProceduralMesh.RoundedBox("Test", new Vector3(2f, 1f, 0.5f), 0.1f);
            try
            {
                Vector3 size = mesh.bounds.size;
                Assert.AreEqual(2f, size.x, 1e-3f);
                Assert.AreEqual(1f, size.y, 1e-3f);
                Assert.AreEqual(0.5f, size.z, 1e-3f);
            }
            finally
            {
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void JarSand_FitsInsideJar()
        {
            Mesh jar = ProceduralMesh.Jar();
            Mesh sand = ProceduralMesh.JarSand();
            try
            {
                Assert.Less(sand.bounds.extents.x, jar.bounds.extents.x);
                Assert.Less(sand.bounds.max.y, jar.bounds.max.y);
                Assert.AreEqual(0f, jar.bounds.min.y, 1e-4f, "jar origin is its bottom");
            }
            finally
            {
                Object.DestroyImmediate(jar);
                Object.DestroyImmediate(sand);
            }
        }
    }
}
