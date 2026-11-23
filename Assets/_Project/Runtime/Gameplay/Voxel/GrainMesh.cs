using UnityEngine;

namespace GameKit.Gameplay.Voxel
{
    /// <summary>Small generated meshes for sand grains.</summary>
    public static class GrainMesh
    {
        /// <summary>Flat-shaded icosahedron with radius 0.5 (fits a unit cell): 20 faces, 60 vertices.</summary>
        public static Mesh CreateIcosahedron()
        {
            float t = (1f + Mathf.Sqrt(5f)) * 0.5f;
            Vector3[] p =
            {
                new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0),
                new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t),
                new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1)
            };
            int[] f =
            {
                0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11,
                1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
                3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9,
                4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1
            };

            var vertices = new Vector3[f.Length];
            var triangles = new int[f.Length];
            for (int i = 0; i < f.Length; i++)
            {
                vertices[i] = p[f[i]].normalized * 0.5f;
                triangles[i] = i;
            }

            // Make every face point outward for Unity's winding, whatever order the table uses.
            for (int i = 0; i < f.Length; i += 3)
            {
                Vector3 a = vertices[i], b = vertices[i + 1], c = vertices[i + 2];
                if (Vector3.Dot(Vector3.Cross(b - a, c - a), a + b + c) < 0f)
                {
                    vertices[i + 1] = c;
                    vertices[i + 2] = b;
                }
            }

            var mesh = new Mesh { name = "SandGrain_Icosahedron", vertices = vertices, triangles = triangles };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
