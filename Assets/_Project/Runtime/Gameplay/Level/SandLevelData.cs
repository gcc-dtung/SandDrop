using System;
using System.Collections.Generic;
using GameKit.Gameplay.Board;
using GameKit.Gameplay.Voxel;
using UnityEngine;

namespace GameKit.Gameplay.Level
{
    [Serializable]
    public sealed class SandBlockDef
    {
        public string name;
        public int colorIndex;
        [Tooltip("Index of the block that encloses this one, -1 = none.")]
        public int parent = -1;
        public List<SandShape> shapes = new List<SandShape>();
    }

    /// <summary>One level: a sand model made of coloured blocks (some nested) and the tray colour queue.</summary>
    [CreateAssetMenu(menuName = "GameKit/Sand/Level", fileName = "SandLevel")]
    public sealed class SandLevelData : ScriptableObject
    {
        /// <summary>Largest cut-off piece of a baked block given to its neighbour (<see cref="VoxelGrid.MergeCrumbs"/>).</summary>
        public const int CrumbSize = 8;

        public Vector3Int gridSize = new Vector3Int(24, 24, 24);
        [Tooltip("World size of one grain.")]
        public float grainSize = 0.1f;
        public Color[] palette = { Color.red, Color.blue, Color.green };
        public List<SandBlockDef> blocks = new List<SandBlockDef>();
        [Tooltip("Tray colours in the order they appear. Each entry = one tray of 3 blocks.")]
        public List<int> trayColorQueue = new List<int>();
        public Vector3 modelRotation;
        [Tooltip("Inner blocks are not drawn until the block around them is gone (layered models whose layers are too thin to hide them).")]
        public bool hideInnerBlocks;
        public List<SandDecoration> decorations = new List<SandDecoration>();

        [Header("Baked from a 3D model (Tools/GameKit/Sand)")]
        [Tooltip("GUID of the model this level was baked from; empty = built from the block shapes.")]
        public string sourceModelGuid;
        [Tooltip("Cells along the model's longest side when baking.")]
        public int bakeResolution = 44;
        [HideInInspector] public List<int> bakedCells = new List<int>();
        [HideInInspector] public List<int> bakedOverlaps = new List<int>();

        public bool IsBaked => bakedCells != null && bakedCells.Count > 0;

        public BoardBlock[] ToBoardBlocks()
        {
            var result = new BoardBlock[blocks.Count];
            for (int i = 0; i < blocks.Count; i++)
            {
                result[i] = new BoardBlock(blocks[i].colorIndex, blocks[i].parent);
            }
            return result;
        }

        public int[] Parents()
        {
            var result = new int[blocks.Count];
            for (int i = 0; i < blocks.Count; i++)
            {
                result[i] = blocks[i].parent;
            }
            return result;
        }

        public VoxelGrid BuildGrid()
        {
            if (IsBaked)
            {
                // Baked from meshes: clean up the crumbs left where pieces nearly overlap (specks of another colour).
                VoxelGrid baked = VoxelGrid.FromRunLength(gridSize, bakedCells);
                baked.MergeCrumbs(CrumbSize);
                return baked;
            }

            var shapes = new IReadOnlyList<SandShape>[blocks.Count];
            for (int i = 0; i < blocks.Count; i++)
            {
                shapes[i] = blocks[i].shapes;
            }
            return Voxelizer.Build(gridSize, shapes, Parents());
        }
    }
}
