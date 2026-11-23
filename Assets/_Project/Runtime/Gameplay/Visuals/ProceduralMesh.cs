using System.Collections.Generic;
using UnityEngine;

namespace GameKit.Gameplay.Visuals
{
    /// <summary>Small generated meshes for the tray area: turned (lathe) shapes and rounded boxes.</summary>
    public static class ProceduralMesh
    {
        /// <summary>
        /// Turns a profile (x = radius, y = height, listed bottom to top) around the Y axis.
        /// A point with radius 0 closes the shape there. Normals are smooth around and along the profile.
        /// </summary>
        public static Mesh Lathe(string name, IReadOnlyList<Vector2> profile, int segments = 32)
        {
            int rings = profile.Count;
            var vertices = new List<Vector3>(rings * (segments + 1));
            var normals = new List<Vector3>(rings * (segments + 1));
            var uvs = new List<Vector2>(rings * (segments + 1));
            var triangles = new List<int>();

            for (int r = 0; r < rings; r++)
            {
                // 2D normal of the profile at this point (pointing away from the axis for an outward wall).
                Vector2 prev = profile[Mathf.Max(0, r - 1)];
                Vector2 next = profile[Mathf.Min(rings - 1, r + 1)];
                Vector2 tangent = (next - prev).normalized;
                var n2 = new Vector2(tangent.y, -tangent.x);

                for (int s = 0; s <= segments; s++)
                {
                    float a = s / (float)segments * Mathf.PI * 2f;
                    float cos = Mathf.Cos(a), sin = Mathf.Sin(a);
                    vertices.Add(new Vector3(profile[r].x * cos, profile[r].y, profile[r].x * sin));
                    normals.Add(new Vector3(n2.x * cos, n2.y, n2.x * sin).normalized);
                    uvs.Add(new Vector2(s / (float)segments, r / (float)(rings - 1)));
                }
            }

            int stride = segments + 1;
            for (int r = 0; r < rings - 1; r++)
            {
                for (int s = 0; s < segments; s++)
                {
                    int a = r * stride + s, b = a + 1, c = a + stride, d = c + 1;
                    // Clockwise seen from outside (Unity front faces).
                    triangles.Add(a); triangles.Add(c); triangles.Add(b);
                    triangles.Add(b); triangles.Add(c); triangles.Add(d);
                }
            }

            FaceOutward(vertices, normals, triangles);

            var mesh = new Mesh { name = name };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// Box with rounded edges and corners (radius clamped to half the smallest side), centred on the origin.
        /// Built from a subdivided cube whose points are pulled onto the rounded shape.
        /// </summary>
        public static Mesh RoundedBox(string name, Vector3 size, float radius, int subdivisions = 10)
        {
            Vector3 half = size * 0.5f;
            radius = Mathf.Clamp(radius, 0f, Mathf.Min(half.x, Mathf.Min(half.y, half.z)));
            Vector3 inner = half - Vector3.one * radius;

            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var triangles = new List<int>();

            // 6 faces: normal, and two axes spanning the face (u x v = normal for outward winding).
            Vector3[] faceNormals = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
            foreach (Vector3 n in faceNormals)
            {
                Vector3 u = new Vector3(n.y, n.z, n.x);
                Vector3 v = Vector3.Cross(n, u);
                int start = vertices.Count;
                for (int j = 0; j <= subdivisions; j++)
                {
                    for (int i = 0; i <= subdivisions; i++)
                    {
                        Vector3 p = n + u * (i / (float)subdivisions * 2f - 1f) + v * (j / (float)subdivisions * 2f - 1f);
                        p = Vector3.Scale(p, half);
                        var clamped = new Vector3(
                            Mathf.Clamp(p.x, -inner.x, inner.x),
                            Mathf.Clamp(p.y, -inner.y, inner.y),
                            Mathf.Clamp(p.z, -inner.z, inner.z));
                        Vector3 offset = p - clamped;
                        Vector3 normal = offset.sqrMagnitude > 1e-10f ? offset.normalized : n;
                        vertices.Add(clamped + normal * radius);
                        normals.Add(normal);
                    }
                }

                int row = subdivisions + 1;
                for (int j = 0; j < subdivisions; j++)
                {
                    for (int i = 0; i < subdivisions; i++)
                    {
                        int a = start + j * row + i, b = a + 1, c = a + row, d = c + 1;
                        triangles.Add(a); triangles.Add(c); triangles.Add(b);
                        triangles.Add(b); triangles.Add(c); triangles.Add(d);
                    }
                }
            }

            FaceOutward(vertices, normals, triangles);

            var mesh = new Mesh { name = name };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Flips any triangle whose winding disagrees with its vertex normals (Unity: clockwise = front).</summary>
        private static void FaceOutward(List<Vector3> vertices, List<Vector3> normals, List<int> triangles)
        {
            for (int t = 0; t < triangles.Count; t += 3)
            {
                Vector3 a = vertices[triangles[t]], b = vertices[triangles[t + 1]], c = vertices[triangles[t + 2]];
                Vector3 outward = normals[triangles[t]] + normals[triangles[t + 1]] + normals[triangles[t + 2]];
                if (Vector3.Dot(Vector3.Cross(b - a, c - a), outward) < 0f)
                {
                    (triangles[t + 1], triangles[t + 2]) = (triangles[t + 2], triangles[t + 1]);
                }
            }
        }

        // ---- Shapes used by the tray area (units: world metres at tray scale 1) ----

        /// <summary>Glass jar wall, open at the top. Origin at the jar bottom.</summary>
        public static Mesh Jar() => Lathe("Jar_Glass", new[]
        {
            new Vector2(0f, 0f), new Vector2(0.088f, 0f), new Vector2(0.1f, 0.012f), new Vector2(0.104f, 0.05f),
            new Vector2(0.104f, 0.22f), new Vector2(0.096f, 0.25f), new Vector2(0.074f, 0.272f), new Vector2(0.07f, 0.29f),
            new Vector2(0.078f, 0.296f), new Vector2(0.078f, 0.312f), new Vector2(0.07f, 0.318f)
        });

        /// <summary>Sand inside the jar, full height; scale Y to show how full it is. Origin at the bottom.</summary>
        public static Mesh JarSand() => Lathe("Jar_Sand", new[]
        {
            new Vector2(0f, 0.006f), new Vector2(0.084f, 0.006f), new Vector2(0.094f, 0.018f), new Vector2(0.096f, 0.05f),
            new Vector2(0.096f, 0.215f), new Vector2(0.088f, 0.236f), new Vector2(0.05f, 0.25f), new Vector2(0f, 0.254f)
        });

        /// <summary>Cork lid that closes a full jar.</summary>
        public static Mesh JarLid() => Lathe("Jar_Lid", new[]
        {
            new Vector2(0f, 0.27f), new Vector2(0.064f, 0.27f), new Vector2(0.07f, 0.3f), new Vector2(0.084f, 0.31f),
            new Vector2(0.086f, 0.336f), new Vector2(0.078f, 0.346f), new Vector2(0.04f, 0.35f), new Vector2(0f, 0.35f)
        });

        /// <summary>Round plate a waiting jar stands on.</summary>
        public static Mesh Plate() => Lathe("Waiting_Plate", new[]
        {
            new Vector2(0f, 0f), new Vector2(0.115f, 0f), new Vector2(0.128f, 0.01f), new Vector2(0.13f, 0.022f),
            new Vector2(0.118f, 0.03f), new Vector2(0.1f, 0.026f), new Vector2(0f, 0.026f)
        });

        /// <summary>Tray base (rounded slab) and its inset floor.</summary>
        public static Mesh TrayBase() => RoundedBox("Tray_Base", new Vector3(0.8f, 0.12f, 0.76f), 0.08f);

        public static Mesh TrayFloor() => RoundedBox("Tray_Floor", new Vector3(0.68f, 0.03f, 0.64f), 0.03f, 6);

        /// <summary>"+" badge for locked trays: rounded disc.</summary>
        public static Mesh Badge() => Lathe("Tray_Badge", new[]
        {
            new Vector2(0f, 0f), new Vector2(0.085f, 0f), new Vector2(0.095f, 0.012f), new Vector2(0.095f, 0.028f),
            new Vector2(0.085f, 0.04f), new Vector2(0f, 0.04f)
        });
    }
}
