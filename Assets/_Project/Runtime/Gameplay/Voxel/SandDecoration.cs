using System;
using UnityEngine;

namespace GameKit.Gameplay.Voxel
{
    /// <summary>A small mesh on the model (eyes, buttons...): drawn with its block, gone once the block is collected.</summary>
    [Serializable]
    public sealed class SandDecoration
    {
        public Mesh mesh;
        [Tooltip("Block it sits on; it disappears with that block. -1 = stays until the level ends.")]
        public int block = -1;
        public Color color = Color.white;
        [Tooltip("Mesh to grid space (grid units, cell (x,y,z) spans [x, x+1)).")]
        public Matrix4x4 gridMatrix = Matrix4x4.identity;
    }
}
