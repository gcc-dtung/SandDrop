using System;
using UnityEngine;

namespace GameKit.Gameplay.Voxel
{
    /// <summary>Grid ray walk (Amanatides–Woo). Grid space: cell (x,y,z) spans [x, x+1) on each axis.</summary>
    public static class VoxelRaycaster
    {
        /// <summary>Returns the index of the first cell along the ray where <paramref name="isSolid"/> is true, or -1.</summary>
        public static int Raycast(Vector3Int size, Vector3 origin, Vector3 direction, Func<int, bool> isSolid)
        {
            if (direction.sqrMagnitude < 1e-12f || isSolid == null)
            {
                return -1;
            }
            direction.Normalize();

            if (!IntersectBox(size, origin, direction, out float tEnter))
            {
                return -1;
            }

            // Start just inside the grid.
            Vector3 p = origin + direction * (tEnter + 1e-4f);
            int x = Mathf.Clamp(Mathf.FloorToInt(p.x), 0, size.x - 1);
            int y = Mathf.Clamp(Mathf.FloorToInt(p.y), 0, size.y - 1);
            int z = Mathf.Clamp(Mathf.FloorToInt(p.z), 0, size.z - 1);

            int stepX = Math.Sign(direction.x), stepY = Math.Sign(direction.y), stepZ = Math.Sign(direction.z);
            float tDeltaX = stepX != 0 ? Mathf.Abs(1f / direction.x) : float.PositiveInfinity;
            float tDeltaY = stepY != 0 ? Mathf.Abs(1f / direction.y) : float.PositiveInfinity;
            float tDeltaZ = stepZ != 0 ? Mathf.Abs(1f / direction.z) : float.PositiveInfinity;
            float tMaxX = stepX != 0 ? ((stepX > 0 ? x + 1 : x) - p.x) / direction.x : float.PositiveInfinity;
            float tMaxY = stepY != 0 ? ((stepY > 0 ? y + 1 : y) - p.y) / direction.y : float.PositiveInfinity;
            float tMaxZ = stepZ != 0 ? ((stepZ > 0 ? z + 1 : z) - p.z) / direction.z : float.PositiveInfinity;

            int maxSteps = size.x + size.y + size.z + 3;
            for (int i = 0; i < maxSteps; i++)
            {
                if (x < 0 || y < 0 || z < 0 || x >= size.x || y >= size.y || z >= size.z)
                {
                    return -1;
                }

                int index = x + size.x * (y + size.y * z);
                if (isSolid(index))
                {
                    return index;
                }

                if (tMaxX < tMaxY && tMaxX < tMaxZ)
                {
                    x += stepX;
                    tMaxX += tDeltaX;
                }
                else if (tMaxY < tMaxZ)
                {
                    y += stepY;
                    tMaxY += tDeltaY;
                }
                else
                {
                    z += stepZ;
                    tMaxZ += tDeltaZ;
                }
            }
            return -1;
        }

        private static bool IntersectBox(Vector3Int size, Vector3 origin, Vector3 dir, out float tEnter)
        {
            float tMin = 0f, tMax = float.PositiveInfinity;
            for (int axis = 0; axis < 3; axis++)
            {
                float o = origin[axis], d = dir[axis], max = size[axis];
                if (Mathf.Abs(d) < 1e-8f)
                {
                    if (o < 0f || o > max)
                    {
                        tEnter = 0f;
                        return false;
                    }
                    continue;
                }

                float t1 = (0f - o) / d, t2 = (max - o) / d;
                if (t1 > t2)
                {
                    (t1, t2) = (t2, t1);
                }
                tMin = Mathf.Max(tMin, t1);
                tMax = Mathf.Min(tMax, t2);
                if (tMin > tMax)
                {
                    tEnter = 0f;
                    return false;
                }
            }
            tEnter = tMin;
            return true;
        }
    }
}
