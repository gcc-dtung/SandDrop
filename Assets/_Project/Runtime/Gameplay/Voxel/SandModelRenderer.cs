using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using GameKit.Core.Foundation.Logging;
using UnityEngine;
using UnityEngine.Rendering;

namespace GameKit.Gameplay.Voxel
{
    public enum SandRenderMode
    {
        /// <summary>One smooth joined mesh per block, sand look from the shader.</summary>
        Smooth,
        /// <summary>Separate instanced grains.</summary>
        Grains
    }

    /// <summary>
    /// Draws the sand model without a GameObject per block or grain.
    /// Smooth: one mesh per block (surface nets), drawn while the block is not collected.
    /// Grains: instanced grains; only grains touching an empty or collected cell are drawn.
    /// The filled part of the grid is centred on this transform; rotate the transform to rotate the model.
    /// </summary>
    public sealed class SandModelRenderer : MonoBehaviour
    {
        private const int BatchSize = 1023;
        private const string Tag = "SandModel";
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        [SerializeField] private SandRenderMode renderMode = SandRenderMode.Smooth;

        [Header("Smooth")]
        [SerializeField] private Material surfaceMaterial;
        [Tooltip("Taubin smoothing iterations (shrink + inflate) on each block mesh; more = fewer wiggles on thin tails.")]
        [SerializeField, Range(0, 12)] private int smoothIterations = 6;
        [Tooltip("Off: straight blocks on the voxel faces (no blur, no smoothing), so curved, bulging surfaces never run over " +
                 "the layers next to them. On: rounded sand blobs (blur + smoothIterations).")]
        [SerializeField] private bool roundShapes;

        private int SmoothPasses => roundShapes ? smoothIterations : 0;

        [Header("Decorations")]
        [Tooltip("Material for the small meshes on the model (eyes, buttons...); coloured per decoration through _Color.")]
        [SerializeField] private Material decorationMaterial;

        [Header("Grains")]
        [SerializeField] private Material grainMaterial;
        [Tooltip("Empty = a small generated icosahedron (rounded grain).")]
        [SerializeField] private Mesh grainMesh;
        [Tooltip("Grain size relative to its cell. Round grains need > 1 to close the gaps between them.")]
        [SerializeField, Range(0.5f, 1.6f)] private float grainFill = 1.45f;
        [Tooltip("Random offset of each grain, in cells, so rows of grains do not look like a grid.")]
        [SerializeField, Range(0f, 0.5f)] private float positionJitter = 0.25f;
        [Tooltip("Random size spread per grain.")]
        [SerializeField, Range(0f, 0.5f)] private float sizeJitter = 0.15f;
        [Tooltip("Random brightness spread per grain.")]
        [SerializeField, Range(0f, 0.4f)] private float colorJitter = 0.15f;
        [Header("Drain (collected block erodes away from the tap point)")]
        [Tooltip("Seconds for a tapped block to vanish. Most of it goes in the first frames (ease-out), so the tap answers at once.")]
        [SerializeField] private float drainDuration = 0.35f;
        [Tooltip("Seconds between mesh rebuilds while a block drains.")]
        [SerializeField] private float drainRebuildInterval = 0f;
        [Tooltip("Most draining blocks remeshed at the same time on worker threads (each block keeps one remesh going).")]
        [SerializeField, Min(1)] private int maxConcurrentRemeshes = 3;
        [Tooltip("Most grain positions handed to the flying sand per drained block (it only flies a sample of them).")]
        [SerializeField, Min(16)] private int maxDrainGrains = 1024;
        [Tooltip("Random spread (in cells) of the eroding edge so it looks like sand, not a sphere.")]
        [SerializeField] private float drainNoise = 3f;
        [Header("Inner layer fills the outer one")]
        [Tooltip("Seconds for an inner block to grow into the cells of the outer block that was just tapped.")]
        [SerializeField] private float growDuration = 0.25f;

        [Header("Tap feedback: punch (spring out, settle)")]
        [Tooltip("Off: on release the block stops where the press left it and drains at once (no spring). Temporarily off for testing.")]
        [SerializeField] private bool punchOnTap;
        [SerializeField] private float punchOut = 1.12f;
        [SerializeField] private float punchOutTime = 0.1f;
        [SerializeField] private float punchSettleTime = 0.16f;

        [Header("Hold to see inside")]
        [Tooltip("Transparent material for outer blocks while the player holds (GameKit/SandGhost).")]
        [SerializeField] private Material ghostMaterial;
        [Tooltip("Alpha of the held block while it is see-through.")]
        [SerializeField, Range(0f, 1f)] private float ghostAlpha = 0.15f;
        [SerializeField] private float revealFadeTime = 0.15f;
        [Header("Shake")]
        [SerializeField] private float shakeDistance = 0.6f;
        [SerializeField] private float shakeDuration = 0.35f;

        private readonly List<int> visibleCells = new List<int>();
        private readonly List<Matrix4x4[]> matrixBatches = new List<Matrix4x4[]>();
        private readonly List<Vector4[]> colorBatches = new List<Vector4[]>();
        private readonly List<MaterialPropertyBlock> blocks = new List<MaterialPropertyBlock>();
        private readonly List<int> batchCounts = new List<int>();
        private readonly List<Mesh> blockMeshes = new List<Mesh>();
        private MaterialPropertyBlock surfaceBlock;
        private MaterialPropertyBlock decorationBlock;
        private readonly List<SandDecoration> decorations = new List<SandDecoration>();

        private VoxelGrid grid;
        private float grainSize = 0.1f;
        private Color[] blockColors;
        private bool[] collected;
        /// <summary>Parent of each block, or null when inner blocks are drawn even while their parent is still there.</summary>
        private int[] hiddenUnderParent;
        private Vector3[] blockOffsets;
        private Vector3 gridCenter;
        private Tween[] shakeTweens;
        private Tween[] popTweens;
        private Vector3[] blockCenters;
        private float[] blockScales;
        /// <summary>Block shown see-through while held (-1 = none).</summary>
        private int ghostBlock = -1;
        private float reveal;
        private Tween revealTween;
        private static readonly int AlphaId = Shader.PropertyToID("_Alpha");
        private bool cellsDirty;
        private bool matricesDirty;

        private enum BlockPhase : byte
        {
            /// <summary>On the model.</summary>
            Present,
            /// <summary>Collected but still drawn whole until its drain starts.</summary>
            WaitingDrain,
            /// <summary>Collected, eroding away.</summary>
            Draining,
            /// <summary>Collected and fully gone.</summary>
            Gone
        }

        /// <summary>A block whose mesh changes over time: built from a window of <see cref="Order"/>.</summary>
        private abstract class MeshJob
        {
            public int[] Order;
            public float Start;
            /// <summary>A remesh of this block is running (the next one starts once it is uploaded).</summary>
            public bool Remeshing;
        }

        /// <summary>Collected block eroding away: drawn from Order[Removed..].</summary>
        private sealed class Drain : MeshJob
        {
            public int Removed;
            public float LastBuild;
        }

        /// <summary>Inner block filling the outer block's cells: drawn from Order[..Shown].</summary>
        private sealed class Grow : MeshJob
        {
            public int Shown;
            /// <summary>Cells in the mesh on screen.</summary>
            public int Applied;
        }

        private BlockPhase[] phases;
        private Drain[] drains;
        private Grow[] grows;
        private readonly List<int> growQueue = new List<int>();
        private readonly Dictionary<int, List<int>> growAdded = new Dictionary<int, List<int>>();
        private readonly List<Vector3> outlineScratch = new List<Vector3>();
        /// <summary>
        /// Underneath block stack per overlapped cell (highest priority first) from baked overlaps, so removing an
        /// overlapping piece restores the covered piece instead of leaving a hole in thin shells.
        /// </summary>
        private Dictionary<int, int> overlapHead;
        private short[] overlapBlock;
        private int[] overlapNext;
        /// <summary>Blocks touching each block (refreshed when a block grows), to remesh them when it is gone.</summary>
        private int[][] blockNeighbours;
        /// <summary>Blocks hidden right under each block (levels that hide inner blocks), for <see cref="MeshCells"/>.</summary>
        private List<int>[] childBlocks;
        private readonly HashSet<int> neighbourScratch = new HashSet<int>();
        private bool[] drainedCells;
        /// <summary>Grid cells of each block in index order, found once per <see cref="Build"/> (no full-grid scans later).</summary>
        private List<int>[] blockCells;
        /// <summary>Block the drain round-robin starts from next frame, so every draining block gets its turn.</summary>
        private int nextDrainBlock;
        /// <summary>Box (grid cells, inclusive) of each block's boundary cells; empty block = min &gt; max.</summary>
        private Vector3Int[] blockBoxMin;
        private Vector3Int[] blockBoxMax;
        /// <summary>Local position of each block's boundary cells, for measuring the outline of what is left.</summary>
        private Vector3[][] blockOutlines;

        /// <summary>Buffers for one worker-thread remesh; the main thread only uploads the result.</summary>
        private sealed class RemeshSlot
        {
            public readonly SurfaceNets.Workspace Workspace = new SurfaceNets.Workspace();
            public readonly List<Vector3> Vertices = new List<Vector3>();
            public readonly List<Vector3> Normals = new List<Vector3>();
            public readonly List<int> Triangles = new List<int>();
            public bool Busy;
        }

        private readonly List<RemeshSlot> remeshSlots = new List<RemeshSlot>();
        /// <summary>Bumped by every Build / Clear so a remesh started for the previous model is thrown away.</summary>
        private int buildVersion;
        private float[] drainKeys = Array.Empty<float>();
        private int[] drainBuckets = Array.Empty<int>();

        public VoxelGrid Grid => grid;
        public float GrainSize => grainSize;
        public int VisibleGrainCount => visibleCells.Count;
        public float DrainDuration => drainDuration;

        /// <summary>
        /// Shows a new model. <paramref name="blockColors"/> has one colour per block. With
        /// <paramref name="hideInnerParents"/> (parent per block) a block is not drawn or hit while its parent is still
        /// there, so thin outer layers never let the colour inside show through. With <paramref name="overlapRuns"/>
        /// (triples startCell, underBlock, runLength) cells overwritten by another independent piece are restored to
        /// the covered piece when the overlapping piece is collected.
        /// </summary>
        public void Build(VoxelGrid newGrid, float newGrainSize, Color[] newBlockColors, int[] hideInnerParents = null,
            IReadOnlyList<int> overlapRuns = null)
        {
            KillShakes();
            DestroyBlockMeshes();
            decorations.Clear();
            grid = newGrid;
            grainSize = Mathf.Max(0.001f, newGrainSize);
            blockColors = newBlockColors;
            int count = newBlockColors?.Length ?? 0;
            collected = new bool[count];
            blockOffsets = new Vector3[count];
            shakeTweens = new Tween[count];
            popTweens = new Tween[count];
            blockScales = new float[count];
            ghostBlock = -1;
            hiddenUnderParent = hideInnerParents != null && hideInnerParents.Length == count ? (int[])hideInnerParents.Clone() : null;
            for (int i = 0; i < count; i++)
            {
                blockScales[i] = 1f;
            }
            phases = new BlockPhase[count];
            drains = new Drain[count];
            grows = new Grow[count];
            drainedCells = newGrid != null ? new bool[newGrid.CellCount] : null;
            blockCells = newGrid != null ? newGrid.CellsByBlock(count) : null;
            InitOverlaps(overlapRuns, newGrid?.CellCount ?? 0, count);
            childBlocks = null;
            if (hiddenUnderParent != null)
            {
                childBlocks = new List<int>[count];
                for (int b = 0; b < count; b++)
                {
                    int parent = hiddenUnderParent[b];
                    if (parent >= 0 && parent < count && parent != b)
                    {
                        (childBlocks[parent] ??= new List<int>()).Add(b);
                    }
                }
            }
            nextDrainBlock = 0;
            buildVersion++;
            gridCenter = OccupiedCenter(newGrid);
            InitBlockOutlines(count);
            blockCenters = BlockCenters(newGrid, count);
            cellsDirty = true;

            if (renderMode == SandRenderMode.Smooth)
            {
                BuildBlockMeshes(count);
                if (surfaceMaterial == null)
                {
                    Log.Error(Tag, "Surface material is not assigned.", this);
                }
                return;
            }

            if (grainMesh == null)
            {
                grainMesh = GrainMesh.CreateIcosahedron();
            }
            if (grainMaterial == null)
            {
                Log.Error(Tag, "Grain material is not assigned.", this);
            }
            else if (!grainMaterial.enableInstancing)
            {
                Log.Warning(Tag, $"Material {grainMaterial.name} has GPU instancing off; turning it on.", this);
                grainMaterial.enableInstancing = true;
            }
        }

        public void Clear()
        {
            KillShakes();
            DestroyBlockMeshes();
            grid = null;
            blockCells = null;
            blockOutlines = null;
            overlapHead = null;
            overlapBlock = null;
            overlapNext = null;
            buildVersion++;
            decorations.Clear();
            visibleCells.Clear();
            batchCounts.Clear();
        }

        public bool IsCollected(int block) => collected != null && block >= 0 && block < collected.Length && collected[block];

        // ---- Read-only block info (Editor tools: Tools/GameKit/Sand/Block Picker) ----

        public int BlockCount => collected?.Length ?? 0;

        private bool ValidBlock(int block) => collected != null && block >= 0 && block < collected.Length;

        /// <summary>Hidden layer: its outer block is still on the model, so it is drawn as part of that block.</summary>
        public bool IsHiddenLayer(int block) => ValidBlock(block) && IsBuried(block);

        public Color GetBlockColor(int block) => ValidBlock(block) && blockColors != null ? blockColors[block] : Color.clear;

        public int GetBlockCellCount(int block) => ValidBlock(block) && blockCells != null ? blockCells[block].Count : 0;

        /// <summary>On the model, waiting to drain, draining or gone.</summary>
        public string GetBlockPhase(int block) => ValidBlock(block) && phases != null ? phases[block].ToString() : "-";

        /// <summary>Box around the block's outer cells, in this transform's local space. False for an empty block.</summary>
        public bool TryGetBlockBounds(int block, out Bounds localBounds)
        {
            localBounds = default;
            if (!ValidBlock(block) || blockBoxMin == null || blockBoxMin[block].x > blockBoxMax[block].x)
            {
                return false;
            }
            Vector3 min = CellToLocal(blockBoxMin[block]) - Vector3.one * (grainSize * 0.5f);
            Vector3 max = CellToLocal(blockBoxMax[block]) + Vector3.one * (grainSize * 0.5f);
            localBounds = new Bounds((min + max) * 0.5f, max - min);
            return true;
        }

        /// <summary>
        /// Removes (or restores) a block. With <paramref name="keepVisible"/> the block stops counting for taps and
        /// bounds right away but is still drawn until <see cref="BeginDrain"/> erodes it away.
        /// </summary>
        public void SetCollected(int block, bool value, bool keepVisible = false)
        {
            if (collected == null || block < 0 || block >= collected.Length || collected[block] == value)
            {
                return;
            }
            collected[block] = value;
            phases[block] = !value ? BlockPhase.Present
                : keepVisible && renderMode == SandRenderMode.Smooth ? BlockPhase.WaitingDrain : BlockPhase.Gone;
            if (phases[block] == BlockPhase.Gone)
            {
                RefreshNeighbours(block);
            }
            if (grows != null)
            {
                grows[block] = null; // A block tapped while still growing drains from its full shape.
            }
            if (value)
            {
                GrowChildrenInto(block);
                RestoreOverlapped(block);
            }
            cellsDirty = true;
        }

        /// <summary>
        /// The blocks right inside <paramref name="parent"/> take over its cells (each cell goes to the nearest inner
        /// block, spreading cell by cell from them), so the inner layer ends up the size and shape of the outer one.
        /// Their meshes grow outwards over <c>growDuration</c>. Only for levels that hide inner blocks.
        /// </summary>
        private void GrowChildrenInto(int parent)
        {
            if (hiddenUnderParent == null || grid == null || blockCells == null)
            {
                return;
            }

            growQueue.Clear();
            growAdded.Clear();
            for (int b = 0; b < hiddenUnderParent.Length; b++)
            {
                if (hiddenUnderParent[b] == parent && !collected[b] && blockCells[b].Count > 0)
                {
                    growAdded[b] = new List<int>();
                    growQueue.AddRange(blockCells[b]);
                }
            }
            if (growAdded.Count == 0)
            {
                return;
            }

            // Breadth-first from every inner cell at once: parent cells are claimed in order of distance.
            for (int head = 0; head < growQueue.Count; head++)
            {
                int cell = growQueue[head];
                short owner = grid[cell];
                Vector3Int c = grid.Cell(cell);
                Claim(c.x + 1, c.y, c.z, parent, owner);
                Claim(c.x - 1, c.y, c.z, parent, owner);
                Claim(c.x, c.y + 1, c.z, parent, owner);
                Claim(c.x, c.y - 1, c.z, parent, owner);
                Claim(c.x, c.y, c.z + 1, parent, owner);
                Claim(c.x, c.y, c.z - 1, parent, owner);
            }

            float now = Time.time;
            foreach (KeyValuePair<int, List<int>> pair in growAdded)
            {
                int b = pair.Key;
                List<int> added = pair.Value;
                if (added.Count == 0)
                {
                    continue;
                }
                // Grow from what is on screen (own cells + its hidden layers) out into the outer block's cells.
                var order = new List<int>(blockCells[b]);
                AddHiddenLayers(b, order);
                int shown = order.Count;
                order.AddRange(added);
                blockCells[b].AddRange(added);
                RefreshBlockOutline(b);
                blockCenters[b] = BlockCenter(b);
                if (renderMode == SandRenderMode.Smooth)
                {
                    grows[b] = new Grow { Order = order.ToArray(), Start = now, Shown = shown, Applied = shown };
                }
            }
            growQueue.Clear();
            growAdded.Clear();
        }

        private void Claim(int x, int y, int z, int parent, short owner)
        {
            if (!grid.InBounds(x, y, z))
            {
                return;
            }
            int i = grid.Index(x, y, z);
            if (grid[i] != parent)
            {
                return;
            }
            grid[i] = owner;
            growQueue.Add(i);
            growAdded[owner].Add(i);
        }

        private void InitOverlaps(IReadOnlyList<int> runs, int cellCount, int blockCount)
        {
            overlapHead = null;
            overlapBlock = null;
            overlapNext = null;
            if (runs == null || runs.Count < 3 || cellCount <= 0 || blockCount <= 0)
            {
                return;
            }
            int totalNodes = 0;
            for (int i = 0; i + 2 < runs.Count; i += 3)
            {
                int len = runs[i + 2];
                if (len > 0)
                {
                    totalNodes += len;
                }
            }
            if (totalNodes == 0)
            {
                return;
            }
            overlapHead = new Dictionary<int, int>(totalNodes);
            overlapBlock = new short[totalNodes];
            overlapNext = new int[totalNodes];
            int node = 0;
            // Push in reverse so walking from overlapHead visits underneath blocks in highest-to-lowest priority order.
            for (int i = (runs.Count / 3) * 3 - 3; i >= 0; i -= 3)
            {
                int start = runs[i];
                int block = runs[i + 1];
                int len = runs[i + 2];
                if (block < 0 || block >= blockCount || len <= 0)
                {
                    continue;
                }
                int end = Mathf.Min(cellCount, start + len);
                for (int c = Mathf.Max(0, start); c < end; c++)
                {
                    overlapBlock[node] = (short)block;
                    overlapNext[node] = overlapHead.TryGetValue(c, out int prev) ? prev : -1;
                    overlapHead[c] = node++;
                }
            }
        }

        /// <summary>
        /// Gives back cells of <paramref name="removed"/> that were not claimed by its own children to any independent
        /// block that originally also occupied those cells, so removing an overlapping piece never leaves a hole in a
        /// thin layer underneath it.
        /// </summary>
        private void RestoreOverlapped(int removed)
        {
            if (overlapHead == null || grid == null || blockCells == null)
            {
                return;
            }

            growAdded.Clear();
            foreach (int cell in blockCells[removed])
            {
                if (grid[cell] != removed || !overlapHead.TryGetValue(cell, out int node))
                {
                    continue;
                }
                while (node >= 0)
                {
                    int candidate = ResolveUnderBlock(overlapBlock[node]);
                    if (candidate >= 0 && candidate != removed)
                    {
                        grid[cell] = (short)candidate;
                        if (!growAdded.TryGetValue(candidate, out List<int> list))
                        {
                            growAdded[candidate] = list = new List<int>();
                        }
                        list.Add(cell);
                        break;
                    }
                    node = overlapNext[node];
                }
            }

            if (growAdded.Count == 0)
            {
                return;
            }

            // Snapshot each affected visible block's cells before appending, so Smooth mode can grow seamlessly into
            // the restored cells while the removed block drains.
            Dictionary<int, List<int>> existingByVisible = null;
            Dictionary<int, List<int>> addedByVisible = null;
            if (renderMode == SandRenderMode.Smooth)
            {
                existingByVisible = new Dictionary<int, List<int>>();
                addedByVisible = new Dictionary<int, List<int>>();
                foreach (KeyValuePair<int, List<int>> pair in growAdded)
                {
                    int vis = VisibleBlock(pair.Key);
                    if (vis < 0 || vis >= collected.Length || collected[vis] || phases[vis] != BlockPhase.Present)
                    {
                        continue;
                    }
                    if (!existingByVisible.ContainsKey(vis))
                    {
                        existingByVisible[vis] = new List<int>(MeshCells(vis));
                        addedByVisible[vis] = new List<int>();
                    }
                    addedByVisible[vis].AddRange(pair.Value);
                }
            }

            foreach (KeyValuePair<int, List<int>> pair in growAdded)
            {
                int b = pair.Key;
                blockCells[b].AddRange(pair.Value);
                RefreshBlockOutline(b);
                blockCenters[b] = BlockCenter(b);
            }

            if (existingByVisible != null)
            {
                float now = Time.time;
                bool instant = phases[removed] == BlockPhase.Gone;
                foreach (KeyValuePair<int, List<int>> pair in existingByVisible)
                {
                    int vis = pair.Key;
                    List<int> order = pair.Value;
                    int shown = order.Count;
                    order.AddRange(addedByVisible[vis]);
                    Grow prev = grows[vis];
                    int applied = instant ? 0 : prev != null ? Mathf.Min(shown, prev.Applied) : shown;
                    grows[vis] = new Grow
                    {
                        Order = order.ToArray(),
                        Start = instant ? now - growDuration : now,
                        Shown = instant ? order.Count : shown,
                        Applied = applied
                    };
                }
            }

            growAdded.Clear();
        }

        private int ResolveUnderBlock(int block)
        {
            if (block < 0 || block >= collected.Length)
            {
                return -1;
            }
            if (!collected[block])
            {
                return block;
            }
            // If the covered block was already collected and had a single inner copy layer that grew into its shape,
            // restore the overlapped cells to that active layer.
            int guard = 0;
            while (collected[block] && childBlocks != null && childBlocks[block] != null && childBlocks[block].Count == 1 && guard++ < 64)
            {
                block = childBlocks[block][0];
            }
            return !collected[block] ? block : -1;
        }

        /// <summary>
        /// Starts eroding a collected block from <paramref name="fromCell"/> outwards over <see cref="DrainDuration"/>.
        /// Fills <paramref name="localGrains"/> with grain positions of the block (this transform's local space, at most
        /// <c>maxDrainGrains</c>, evenly spread over the drain) in the order they leave, so flying sand can follow the
        /// eroding edge.
        /// </summary>
        public void BeginDrain(int block, Vector3Int fromCell, List<Vector3> localGrains)
        {
            localGrains.Clear();
            if (grid == null || phases == null || block < 0 || block >= phases.Length)
            {
                return;
            }

            int[] order = DrainOrder(blockCells[block], fromCell);
            int samples = Mathf.Min(order.Length, maxDrainGrains);
            float step = order.Length / (float)Mathf.Max(1, samples);
            // Grains leave from where the block is drawn now (squashed by the press, moved by a shake).
            Vector3 pivot = blockCenters[block];
            float scale = blockScales[block];
            Vector3 offset = blockOffsets[block];
            for (int i = 0; i < samples; i++)
            {
                Vector3 p = CellToLocal(grid.Cell(order[Mathf.Min(order.Length - 1, (int)(i * step))]));
                localGrains.Add(pivot + (p - pivot) * scale + offset);
            }

            if (phases[block] == BlockPhase.WaitingDrain)
            {
                phases[block] = BlockPhase.Draining;
                drains[block] = new Drain { Order = order, Start = Time.time, LastBuild = -1f };
            }
        }

        /// <summary>
        /// Cells of a block ordered by distance from <paramref name="fromCell"/> plus noise (so the edge looks like
        /// sand, not a sphere). Bucket sort in steps of a quarter cell: linear time, so a big block does not stall
        /// the tap frame.
        /// </summary>
        private int[] DrainOrder(List<int> cells, Vector3Int fromCell)
        {
            const float bucketsPerCell = 4f;
            int n = cells.Count;
            var order = new int[n];
            if (n == 0)
            {
                return order;
            }
            if (drainKeys.Length < n)
            {
                drainKeys = new float[n + n / 4];
            }
            float maxKey = 0f;
            for (int i = 0; i < n; i++)
            {
                Vector3Int c = grid.Cell(cells[i]);
                float key = Vector3.Distance(c, fromCell) + Unit(Hash((uint)cells[i]), 8) * drainNoise;
                drainKeys[i] = key;
                maxKey = Mathf.Max(maxKey, key);
            }

            int bucketCount = (int)(maxKey * bucketsPerCell) + 2;
            if (drainBuckets.Length < bucketCount)
            {
                drainBuckets = new int[bucketCount + bucketCount / 4];
            }
            Array.Clear(drainBuckets, 0, bucketCount);
            for (int i = 0; i < n; i++)
            {
                drainBuckets[(int)(drainKeys[i] * bucketsPerCell) + 1]++;
            }
            for (int k = 1; k < bucketCount; k++)
            {
                drainBuckets[k] += drainBuckets[k - 1];
            }
            for (int i = 0; i < n; i++)
            {
                order[drainBuckets[(int)(drainKeys[i] * bucketsPerCell)]++] = cells[i];
            }
            return order;
        }

        /// <summary>
        /// Cell hit by a world-space ray, or -1. A block that is still draining blocks the ray where its sand has not
        /// left yet (so a quick second tap cannot reach what is behind it); check <see cref="IsCollected"/> on the
        /// result.
        /// </summary>
        public int RaycastCell(Ray worldRay)
        {
            if (grid == null)
            {
                return -1;
            }
            Vector3 origin = WorldToGrid(worldRay.origin);
            Vector3 direction = transform.InverseTransformDirection(worldRay.direction);
            return VoxelRaycaster.Raycast(grid.Size, origin, direction, IsShown);
        }

        /// <summary>Hidden inner block: its parent is still on the model (only when the level hides inner blocks).</summary>
        private bool IsBuried(int block)
        {
            if (hiddenUnderParent == null)
            {
                return false;
            }
            int parent = hiddenUnderParent[block];
            return parent >= 0 && parent < collected.Length && !collected[parent];
        }

        /// <summary>Cell still drawn: not collected, or collected but its sand has not drained yet.</summary>
        private bool IsShown(int cell)
        {
            short b = grid[cell];
            if (b < 0)
            {
                return false;
            }
            if (!IsCollected(b))
            {
                return true; // A hidden layer's cell is drawn as part of the block around it.
            }
            BlockPhase phase = phases[b];
            return phase == BlockPhase.WaitingDrain || (phase == BlockPhase.Draining && !drainedCells[cell]);
        }

        /// <summary>First block hit by a world-space ray (ignores collected blocks), or -1.</summary>
        public int RaycastBlock(Ray worldRay)
        {
            if (grid == null)
            {
                return -1;
            }

            Vector3 origin = WorldToGrid(worldRay.origin);
            Vector3 direction = transform.InverseTransformDirection(worldRay.direction);
            int cell = VoxelRaycaster.Raycast(grid.Size, origin, direction, IsSolid);
            return cell >= 0 ? VisibleBlock(grid[cell]) : -1;
        }

        /// <summary>Adds the world position of every grain of the block (visible or buried).</summary>
        public void GetGrainWorldPositions(int block, List<Vector3> result)
        {
            if (grid == null || blockCells == null || block < 0 || block >= blockCells.Length)
            {
                return;
            }
            foreach (int i in blockCells[block])
            {
                result.Add(transform.TransformPoint(CellToLocal(grid.Cell(i))));
            }
        }

        /// <summary>
        /// Bounding sphere of the blocks not collected yet, in this transform's local space (before scale).
        /// Returns false when nothing is left.
        /// </summary>
        public bool TryGetRemainingBounds(out Vector3 localCenter, out float radius)
        {
            radius = 0f;
            if (!TryGetRemainingBox(out localCenter, out Vector3 halfSize))
            {
                return false;
            }
            radius = halfSize.magnitude;
            return true;
        }

        /// <summary>
        /// Adds the local position (before scale) of each boundary cell of the blocks not collected yet: enough to
        /// measure the outline of what is left without walking every cell. Returns false when nothing is left.
        /// </summary>
        public bool GetRemainingOutline(List<Vector3> localPoints)
        {
            localPoints.Clear();
            if (grid == null || collected == null)
            {
                return false;
            }
            for (int b = 0; b < blockOutlines.Length; b++)
            {
                if (!collected[b])
                {
                    localPoints.AddRange(blockOutlines[b]);
                }
            }
            if (localPoints.Count == 0)
            {
                return false;
            }

            // Decorations still shown can stick out of the sand (a flag, a horn): add their box corners.
            Matrix4x4 gridToLocal = Matrix4x4.Scale(Vector3.one * grainSize) * Matrix4x4.Translate(-gridCenter);
            foreach (SandDecoration decoration in decorations)
            {
                int b = decoration.block;
                if (b >= 0 && b < collected.Length && (collected[b] || IsBuried(b)))
                {
                    continue;
                }
                Matrix4x4 m = gridToLocal * decoration.gridMatrix;
                Bounds bounds = decoration.mesh.bounds;
                for (int k = 0; k < 8; k++)
                {
                    var corner = new Vector3((k & 1) == 0 ? bounds.min.x : bounds.max.x, (k & 2) == 0 ? bounds.min.y : bounds.max.y,
                        (k & 4) == 0 ? bounds.min.z : bounds.max.z);
                    localPoints.Add(m.MultiplyPoint3x4(corner));
                }
            }
            return true;
        }

        /// <summary>Box and outline (boundary cells) of every block.</summary>
        private void InitBlockOutlines(int count)
        {
            blockBoxMin = new Vector3Int[count];
            blockBoxMax = new Vector3Int[count];
            blockOutlines = new Vector3[count][];
            blockNeighbours = new int[count][];
            for (int b = 0; b < count; b++)
            {
                RefreshBlockOutline(b);
            }
        }

        /// <summary>
        /// Box and local points of a block's boundary cells (next to empty space or another block): what the fit /
        /// centring code measures, without walking every cell.
        /// </summary>
        private void RefreshBlockOutline(int b)
        {
            var min = new Vector3Int(int.MaxValue, int.MaxValue, int.MaxValue);
            var max = new Vector3Int(int.MinValue, int.MinValue, int.MinValue);
            outlineScratch.Clear();
            neighbourScratch.Clear();
            if (grid != null && blockCells != null)
            {
                short owner = (short)b;
                foreach (int i in blockCells[b])
                {
                    Vector3Int c = grid.Cell(i);
                    // Non-short-circuit | so every side records its neighbour.
                    if (Side(c.x + 1, c.y, c.z, owner) | Side(c.x - 1, c.y, c.z, owner) |
                        Side(c.x, c.y + 1, c.z, owner) | Side(c.x, c.y - 1, c.z, owner) |
                        Side(c.x, c.y, c.z + 1, owner) | Side(c.x, c.y, c.z - 1, owner))
                    {
                        min = Vector3Int.Min(min, c);
                        max = Vector3Int.Max(max, c);
                        outlineScratch.Add(CellToLocal(c));
                    }
                }
            }
            blockBoxMin[b] = min;
            blockBoxMax[b] = max;
            blockOutlines[b] = outlineScratch.ToArray();
            outlineScratch.Clear();
            blockNeighbours[b] = new int[neighbourScratch.Count];
            neighbourScratch.CopyTo(blockNeighbours[b]);
            neighbourScratch.Clear();
        }

        private Vector3 BlockCenter(int b)
        {
            List<int> cells = blockCells[b];
            if (cells.Count == 0)
            {
                return Vector3.zero;
            }
            Vector3 sum = Vector3.zero;
            foreach (int i in cells)
            {
                sum += CellToLocal(grid.Cell(i));
            }
            return sum / cells.Count;
        }

        /// <summary>True when the cell next door is not <paramref name="owner"/>'s; notes the block there, if any.</summary>
        private bool Side(int x, int y, int z, short owner)
        {
            short other = grid[x, y, z]; // Empty outside the grid.
            if (other == owner)
            {
                return false;
            }
            if (other >= 0)
            {
                neighbourScratch.Add(other);
            }
            return true;
        }

        /// <summary>
        /// Which blocks count as solid for the seam-closing in <see cref="SurfaceNets"/>: everything not gone yet.
        /// A copy, so a worker thread can read it while blocks change on the main thread.
        /// </summary>
        private bool[] SolidBlocks()
        {
            var solid = new bool[phases.Length];
            for (int b = 0; b < solid.Length; b++)
            {
                solid[b] = phases[b] != BlockPhase.Gone;
            }
            return solid;
        }

        /// <summary>
        /// <paramref name="block"/> is gone: remesh the blocks around it so they no longer reach into its space
        /// (their surfaces overlap a cell into solid neighbours to close the seams).
        /// </summary>
        private void RefreshNeighbours(int block)
        {
            if (blockNeighbours == null || grows == null || renderMode != SandRenderMode.Smooth)
            {
                return;
            }
            float now = Time.time;
            foreach (int n in blockNeighbours[block])
            {
                if (n == block || n >= phases.Length || phases[n] != BlockPhase.Present || collected[n] || blockMeshes.Count <= n ||
                    blockMeshes[n] == null)
                {
                    continue;
                }
                Grow g = grows[n];
                if (g != null)
                {
                    g.Applied = 0; // Growing: make sure its last remesh happens after this block is gone.
                    continue;
                }
                // A finished grow job is a full remesh.
                grows[n] = new Grow { Order = MeshCells(n).ToArray(), Start = now - growDuration, Shown = 0, Applied = 0 };
            }
        }


        /// <summary>
        /// Box around the blocks not collected yet, in this transform's local space (before scale).
        /// Returns false when nothing is left.
        /// </summary>
        public bool TryGetRemainingBox(out Vector3 localCenter, out Vector3 halfSize)
        {
            localCenter = Vector3.zero;
            halfSize = Vector3.zero;
            if (grid == null || collected == null)
            {
                return false;
            }

            // Boundary cells hold every block's outer cells, so the box of their boxes is the box of what is left.
            var min = new Vector3Int(int.MaxValue, int.MaxValue, int.MaxValue);
            var max = new Vector3Int(int.MinValue, int.MinValue, int.MinValue);
            for (int b = 0; b < blockBoxMin.Length; b++)
            {
                if (!collected[b] && blockBoxMin[b].x <= blockBoxMax[b].x)
                {
                    min = Vector3Int.Min(min, blockBoxMin[b]);
                    max = Vector3Int.Max(max, blockBoxMax[b]);
                }
            }
            if (min.x > max.x)
            {
                return false;
            }

            Vector3 lo = new Vector3(min.x, min.y, min.z);
            Vector3 hi = new Vector3(max.x + 1, max.y + 1, max.z + 1);
            // Decorations still shown (a flag, a horn) count too, so the model is centred on all that is drawn.
            foreach (SandDecoration decoration in decorations)
            {
                int b = decoration.block;
                if (b >= 0 && b < collected.Length && (collected[b] || IsBuried(b)))
                {
                    continue;
                }
                Bounds bounds = decoration.mesh.bounds;
                for (int k = 0; k < 8; k++)
                {
                    var corner = new Vector3((k & 1) == 0 ? bounds.min.x : bounds.max.x, (k & 2) == 0 ? bounds.min.y : bounds.max.y,
                        (k & 4) == 0 ? bounds.min.z : bounds.max.z);
                    Vector3 g = decoration.gridMatrix.MultiplyPoint3x4(corner);
                    lo = Vector3.Min(lo, g);
                    hi = Vector3.Max(hi, g);
                }
            }
            localCenter = ((lo + hi) * 0.5f - gridCenter) * grainSize;
            halfSize = (hi - lo) * 0.5f * grainSize;
            return true;
        }

        /// <summary>
        /// Tap feedback (finger lifted): the block springs out past its size, then settles (it keeps draining).
        /// With <c>punchOnTap</c> off (default) it just stops any scale tween and drains. No squash on press: shrinking
        /// an outer block pushed it into the layers and blocks around it.
        /// </summary>
        public void Punch(int block)
        {
            if (popTweens == null || block < 0 || block >= popTweens.Length)
            {
                return;
            }

            popTweens[block]?.Kill();
            popTweens[block] = null;
            if (!punchOnTap)
            {
                return;
            }
            DG.Tweening.Core.DOGetter<float> get = () => blockScales[block];
            DG.Tweening.Core.DOSetter<float> set = v => blockScales[block] = v;
            popTweens[block] = DOTween.Sequence()
                .Append(DOTween.To(get, set, punchOut, punchOutTime).SetEase(Ease.OutQuad))
                .Append(DOTween.To(get, set, 1f, punchSettleTime).SetEase(Ease.OutBack))
                .SetLink(gameObject);
        }

        /// <summary>
        /// Hold to see inside: <paramref name="block"/> fades to see-through so the layer right inside it shows (that
        /// layer stays solid, so deeper ones stay hidden). <paramref name="on"/> = false fades it back.
        /// </summary>
        public void SetReveal(int block, bool on)
        {
            if (on)
            {
                if (block < 0 || blockMeshes == null || block >= blockMeshes.Count)
                {
                    return;
                }
                if (block != ghostBlock)
                {
                    revealTween?.Kill();
                    reveal = 0f; // A different block starts from solid.
                }
                ghostBlock = block;
            }

            float target = on && ghostMaterial != null ? 1f : 0f;
            revealTween?.Kill();
            if (Mathf.Approximately(reveal, target))
            {
                reveal = target;
                return;
            }
            revealTween = DOTween.To(() => reveal, v => reveal = v, target, revealFadeTime)
                .SetEase(Ease.OutQuad)
                .SetLink(gameObject);
        }

        /// <summary>Short side-to-side wiggle for a block that cannot be tapped yet.</summary>
        public void Shake(int block, Vector3 worldAxis)
        {
            if (blockOffsets == null || block < 0 || block >= blockOffsets.Length)
            {
                return;
            }

            shakeTweens[block]?.Kill();
            Vector3 localAxis = transform.InverseTransformDirection(worldAxis).normalized * (shakeDistance * grainSize);
            float t = 0f;
            shakeTweens[block] = DOTween.To(() => t, v =>
                {
                    t = v;
                    blockOffsets[block] = localAxis * (Mathf.Sin(v * Mathf.PI * 6f) * (1f - v));
                    matricesDirty = true;
                }, 1f, shakeDuration)
                .SetEase(Ease.Linear)
                .SetLink(gameObject)
                .OnKill(() =>
                {
                    blockOffsets[block] = Vector3.zero;
                    matricesDirty = true;
                });
        }

        /// <summary>Local position of a grain; the filled part of the grid is centred on this transform.</summary>
        public Vector3 CellToLocal(Vector3Int cell)
        {
            return (new Vector3(cell.x + 0.5f, cell.y + 0.5f, cell.z + 0.5f) - gridCenter) * grainSize;
        }

        private Vector3 WorldToGrid(Vector3 world)
        {
            Vector3 local = transform.InverseTransformPoint(world);
            return local / grainSize + gridCenter;
        }

        /// <summary>Centre of the bounding box of all filled cells, in grid units.</summary>
        private static Vector3 OccupiedCenter(VoxelGrid g)
        {
            if (g == null)
            {
                return Vector3.zero;
            }

            var min = new Vector3Int(int.MaxValue, int.MaxValue, int.MaxValue);
            var max = new Vector3Int(int.MinValue, int.MinValue, int.MinValue);
            for (int i = 0; i < g.CellCount; i++)
            {
                if (g[i] >= 0)
                {
                    Vector3Int c = g.Cell(i);
                    min = Vector3Int.Min(min, c);
                    max = Vector3Int.Max(max, c);
                }
            }
            if (min.x > max.x)
            {
                return (Vector3)g.Size * 0.5f;
            }
            return ((Vector3)min + (Vector3)max + Vector3.one) * 0.5f;
        }

        private bool IsSolid(int cell)
        {
            short b = grid[cell];
            return b >= 0 && !IsCollected(b);
        }

        private void LateUpdate()
        {
            if (grid == null)
            {
                return;
            }

            DrawDecorations();

            if (renderMode == SandRenderMode.Smooth)
            {
                UpdateDrains();
                UpdateGrows();
                DrawBlockMeshes();
                return;
            }

            if (grainMaterial == null)
            {
                return;
            }

            if (cellsDirty)
            {
                RebuildVisibleCells();
                cellsDirty = false;
                matricesDirty = true;
            }
            if (matricesDirty || transform.hasChanged)
            {
                RebuildMatrices();
                matricesDirty = false;
                transform.hasChanged = false;
            }

            for (int i = 0; i < batchCounts.Count; i++)
            {
                Graphics.DrawMeshInstanced(grainMesh, 0, grainMaterial, matrixBatches[i], batchCounts[i], blocks[i],
                    ShadowCastingMode.Off, false, gameObject.layer);
            }
        }

        private void BuildBlockMeshes(int count)
        {
            DestroyBlockMeshes();
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var triangles = new List<int>();
            bool[] solid = SolidBlocks();
            for (int b = 0; b < count; b++)
            {
                Mesh mesh = null;
                if (!solid[b])
                {
                    blockMeshes.Add(null);
                    continue;
                }
                if (SurfaceNets.Build(grid, MeshCells(b), 0, vertices, triangles, SmoothPasses, null, -1, (short)b, solid,
                        roundShapes))
                {
                    ToLocal(vertices, gridCenter, grainSize);
                    ComputeNormals(vertices, triangles, normals);
                    mesh = new Mesh { name = $"SandBlock_{b}" };
                    mesh.MarkDynamic();
                    Upload(mesh, vertices, normals, triangles);
                }
                blockMeshes.Add(mesh);
            }
        }

        /// <summary>Small meshes that ride on blocks (set after <see cref="Build"/>; cleared by the next Build).</summary>
        public void SetDecorations(IReadOnlyList<SandDecoration> list)
        {
            decorations.Clear();
            if (list == null)
            {
                return;
            }
            foreach (SandDecoration decoration in list)
            {
                if (decoration != null && decoration.mesh != null)
                {
                    decorations.Add(decoration);
                }
            }
        }

        private void DrawDecorations()
        {
            if (decorations.Count == 0 || decorationMaterial == null)
            {
                return;
            }
            decorationBlock ??= new MaterialPropertyBlock();
            Matrix4x4 localToWorld = transform.localToWorldMatrix;
            Matrix4x4 gridToLocal = Matrix4x4.Scale(Vector3.one * grainSize) * Matrix4x4.Translate(-gridCenter);
            foreach (SandDecoration decoration in decorations)
            {
                int b = decoration.block;
                Matrix4x4 matrix = localToWorld;
                if (b >= 0 && b < collected.Length)
                {
                    // Gone as soon as its block is tapped, hidden while its block is buried; follows shake and punch.
                    if (collected[b] || IsBuried(b))
                    {
                        continue;
                    }
                    matrix *= Matrix4x4.Translate(blockOffsets[b]);
                    float scale = blockScales[b];
                    if (scale != 1f)
                    {
                        Vector3 c = blockCenters[b];
                        matrix *= Matrix4x4.Translate(c) * Matrix4x4.Scale(Vector3.one * scale) * Matrix4x4.Translate(-c);
                    }
                }
                decorationBlock.SetColor(ColorId, decoration.color);
                Graphics.DrawMesh(decoration.mesh, matrix * gridToLocal * decoration.gridMatrix, decorationMaterial, gameObject.layer,
                    null, 0, decorationBlock, ShadowCastingMode.Off, false);
            }
        }

        private void DrawBlockMeshes()
        {
            if (surfaceMaterial == null)
            {
                return;
            }
            surfaceBlock ??= new MaterialPropertyBlock();
            Matrix4x4 localToWorld = transform.localToWorldMatrix;
            for (int b = 0; b < blockMeshes.Count; b++)
            {
                Mesh mesh = blockMeshes[b];
                BlockPhase phase = phases[b];
                if (mesh == null || phase == BlockPhase.Gone || (collected[b] && phase == BlockPhase.Present))
                {
                    continue;
                }
                // Hidden inner layer (drawn as part of the block around it); while holding, the one right inside the
                // see-through block shows.
                if (IsBuried(b) && !(reveal > 0.001f && hiddenUnderParent[b] == ghostBlock))
                {
                    continue;
                }
                // SetColor converts gamma → linear itself.
                surfaceBlock.SetColor(ColorId, blockColors[b]);
                Matrix4x4 matrix = localToWorld * Matrix4x4.Translate(blockOffsets[b]);
                float scale = blockScales[b];
                if (scale != 1f)
                {
                    Vector3 c = blockCenters[b];
                    matrix *= Matrix4x4.Translate(c) * Matrix4x4.Scale(Vector3.one * scale) * Matrix4x4.Translate(-c);
                }
                bool ghost = reveal > 0.001f && b == ghostBlock && ghostMaterial != null;
                if (ghost)
                {
                    surfaceBlock.SetFloat(AlphaId, Mathf.Lerp(1f, ghostAlpha, reveal));
                }
                Graphics.DrawMesh(mesh, matrix, ghost ? ghostMaterial : surfaceMaterial, gameObject.layer, null, 0,
                    surfaceBlock, ShadowCastingMode.Off, false);
            }
        }

        /// <summary>
        /// Cells a block's mesh is built from: its own plus those of every layer hidden inside it. Inner layers take
        /// cells out of the outer one when a level is baked (often right on its surface); without them the outer
        /// block would show holes, and drawing the hidden layer there mixes its colour into the outer one.
        /// </summary>
        private List<int> MeshCells(int b)
        {
            if (childBlocks == null || childBlocks[b] == null)
            {
                return blockCells[b];
            }
            var cells = new List<int>(blockCells[b]);
            AddHiddenLayers(b, cells);
            return cells;
        }

        private void AddHiddenLayers(int b, List<int> cells)
        {
            if (childBlocks == null || childBlocks[b] == null)
            {
                return;
            }
            foreach (int child in childBlocks[b])
            {
                if (!collected[child])
                {
                    cells.AddRange(blockCells[child]);
                    AddHiddenLayers(child, cells);
                }
            }
        }

        /// <summary>
        /// The block a player sees at a cell: a cell of a hidden layer is drawn as part of the visible block around
        /// it (<see cref="MeshCells"/>), so a tap there is a tap on that block.
        /// </summary>
        public int VisibleBlock(int block)
        {
            if (hiddenUnderParent == null || collected == null)
            {
                return block;
            }
            int guard = 0;
            while (block >= 0 && block < collected.Length && IsBuried(block) && guard++ < 64)
            {
                block = hiddenUnderParent[block];
            }
            return block;
        }

        private void UpdateDrains()
        {
            if (drains == null)
            {
                return;
            }
            float now = Time.time;
            int first = nextDrainBlock;
            for (int k = 0; k < drains.Length; k++)
            {
                int b = (first + k) % drains.Length;
                Drain d = drains[b];
                if (d == null || phases[b] != BlockPhase.Draining)
                {
                    continue;
                }

                float t = drainDuration > 0f ? (now - d.Start) / drainDuration : 1f;
                if (t >= 1f)
                {
                    phases[b] = BlockPhase.Gone;
                    drains[b] = null;
                    RefreshNeighbours(b);
                    continue;
                }
                float progress = EaseOut(t);

                int target = Mathf.Clamp((int)(progress * d.Order.Length), 0, d.Order.Length);
                if (target <= d.Removed || d.Remeshing || now - d.LastBuild < drainRebuildInterval)
                {
                    continue;
                }
                RemeshSlot slot = FreeRemeshSlot();
                if (slot == null)
                {
                    continue;
                }
                for (int i = d.Removed; i < target; i++)
                {
                    int cell = d.Order[i];
                    if (grid[cell] == b)
                    {
                        drainedCells[cell] = true;
                    }
                }
                d.Removed = target;
                d.LastBuild = now;
                nextDrainBlock = (b + 1) % drains.Length;
                RemeshAsync(slot, b, d, d.Removed, -1).Forget();
            }
        }

        /// <summary>Inner blocks filling the outer one: show more of their cells each frame, nearest ones first.</summary>
        private void UpdateGrows()
        {
            if (grows == null)
            {
                return;
            }
            float now = Time.time;
            for (int b = 0; b < grows.Length; b++)
            {
                Grow g = grows[b];
                if (g == null)
                {
                    continue;
                }
                int total = g.Order.Length;
                if (g.Applied >= total)
                {
                    grows[b] = null;
                    continue;
                }
                float t = growDuration > 0f ? (now - g.Start) / growDuration : 1f;
                int target = t >= 1f ? total : g.Shown + (int)(EaseOut(t) * (total - g.Shown));
                target = Mathf.Max(target, g.Shown);
                if (target <= g.Shown && g.Shown < total || g.Remeshing)
                {
                    continue;
                }
                RemeshSlot slot = FreeRemeshSlot();
                if (slot == null)
                {
                    continue;
                }
                g.Shown = Mathf.Max(g.Shown, target);
                RemeshAsync(slot, b, g, 0, g.Shown).Forget();
            }
        }

        private static float EaseOut(float t) => 1f - (1f - t) * (1f - t);

        private RemeshSlot FreeRemeshSlot()
        {
            foreach (RemeshSlot slot in remeshSlots)
            {
                if (!slot.Busy)
                {
                    return slot;
                }
            }
            if (remeshSlots.Count >= maxConcurrentRemeshes)
            {
                return null;
            }
            var created = new RemeshSlot();
            remeshSlots.Add(created);
            return created;
        }

        /// <summary>
        /// Remeshes a block from Order[<paramref name="from"/>..<paramref name="to"/>) of its drain / grow job on a
        /// worker thread, then uploads the mesh here. A draining block is marked gone when nothing is left to draw.
        /// </summary>
        private async UniTaskVoid RemeshAsync(RemeshSlot slot, int b, MeshJob job, int from, int to)
        {
            slot.Busy = true;
            job.Remeshing = true;
            int version = buildVersion;
            VoxelGrid g = grid;
            Vector3 center = gridCenter;
            float size = grainSize;
            int smooth = SmoothPasses;
            bool round = roundShapes;
            bool[] solid = SolidBlocks();
            short self = (short)b;
            bool ok;
            try
            {
#if UNITY_WEBGL
                ok = BuildRemesh(slot, g, job.Order, from, to, center, size, smooth, self, solid, round);
#else
                ok = await UniTask.RunOnThreadPool(() => BuildRemesh(slot, g, job.Order, from, to, center, size, smooth, self, solid, round), true,
                    this.GetCancellationTokenOnDestroy());
#endif
            }
            catch (OperationCanceledException)
            {
                slot.Busy = false;
                return;
            }
            catch (Exception e)
            {
                slot.Busy = false;
                job.Remeshing = false;
                Log.Exception(e, this);
                return;
            }

            if (this != null && version == buildVersion)
            {
                if (job is Drain d)
                {
                    ApplyDrain(slot, b, d, ok);
                }
                else if (job is Grow grow)
                {
                    ApplyGrow(slot, b, grow, ok, to);
                }
            }
            job.Remeshing = false;
            slot.Busy = false;
        }

        private void ApplyDrain(RemeshSlot slot, int b, Drain d, bool ok)
        {
            if (drains == null || b >= drains.Length || drains[b] != d || phases[b] != BlockPhase.Draining)
            {
                return;
            }
            Mesh mesh = blockMeshes[b];
            if (!ok || mesh == null)
            {
                phases[b] = BlockPhase.Gone;
                drains[b] = null;
                RefreshNeighbours(b);
                return;
            }
            Upload(mesh, slot.Vertices, slot.Normals, slot.Triangles);
        }

        private void ApplyGrow(RemeshSlot slot, int b, Grow grow, bool ok, int shown)
        {
            if (grows == null || b >= grows.Length || grows[b] != grow || collected[b])
            {
                return;
            }
            grow.Applied = shown;
            if (!ok)
            {
                return;
            }
            Mesh mesh = blockMeshes[b];
            if (mesh == null)
            {
                mesh = new Mesh { name = $"SandBlock_{b}" };
                mesh.MarkDynamic();
                blockMeshes[b] = mesh;
            }
            Upload(mesh, slot.Vertices, slot.Normals, slot.Triangles);
        }

        /// <summary>Worker-thread part of a remesh: only reads the grid and the drain order.</summary>
        private static bool BuildRemesh(RemeshSlot slot, VoxelGrid g, int[] order, int from, int to, Vector3 center, float size,
            int smooth, short self, bool[] solid, bool round)
        {
            if (!SurfaceNets.Build(g, order, from, slot.Vertices, slot.Triangles, smooth, slot.Workspace, to, self, solid, round))
            {
                return false;
            }
            ToLocal(slot.Vertices, center, size);
            ComputeNormals(slot.Vertices, slot.Triangles, slot.Normals);
            return true;
        }

        private static void ToLocal(List<Vector3> vertices, Vector3 center, float size)
        {
            for (int i = 0; i < vertices.Count; i++)
            {
                vertices[i] = (vertices[i] - center) * size;
            }
        }

        /// <summary>Area-weighted smooth normals, computed by hand so it also runs off the main thread.</summary>
        private static void ComputeNormals(List<Vector3> vertices, List<int> triangles, List<Vector3> normals)
        {
            normals.Clear();
            for (int i = 0; i < vertices.Count; i++)
            {
                normals.Add(Vector3.zero);
            }
            for (int t = 0; t < triangles.Count; t += 3)
            {
                int a = triangles[t], b = triangles[t + 1], c = triangles[t + 2];
                Vector3 n = Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]);
                normals[a] += n;
                normals[b] += n;
                normals[c] += n;
            }
            for (int i = 0; i < normals.Count; i++)
            {
                normals[i] = normals[i].normalized;
            }
        }

        private static void Upload(Mesh mesh, List<Vector3> vertices, List<Vector3> normals, List<int> triangles)
        {
            mesh.Clear();
            mesh.indexFormat = vertices.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetTriangles(triangles, 0, true);
        }

        private void DestroyBlockMeshes()
        {
            foreach (Mesh mesh in blockMeshes)
            {
                if (mesh == null)
                {
                    continue;
                }
                if (Application.isPlaying)
                {
                    Destroy(mesh);
                }
                else
                {
                    DestroyImmediate(mesh);
                }
            }
            blockMeshes.Clear();
        }

        private void RebuildVisibleCells()
        {
            visibleCells.Clear();
            Vector3Int s = grid.Size;
            for (int z = 0; z < s.z; z++)
            {
                for (int y = 0; y < s.y; y++)
                {
                    for (int x = 0; x < s.x; x++)
                    {
                        int index = grid.Index(x, y, z);
                        if (IsSolid(index) && IsExposed(x, y, z))
                        {
                            visibleCells.Add(index);
                        }
                    }
                }
            }
        }

        private bool IsExposed(int x, int y, int z)
        {
            return IsOpen(x + 1, y, z) || IsOpen(x - 1, y, z) || IsOpen(x, y + 1, z) ||
                   IsOpen(x, y - 1, z) || IsOpen(x, y, z + 1) || IsOpen(x, y, z - 1);
        }

        private bool IsOpen(int x, int y, int z)
        {
            return !grid.InBounds(x, y, z) || !IsSolid(grid.Index(x, y, z));
        }

        private void RebuildMatrices()
        {
            int batchCount = (visibleCells.Count + BatchSize - 1) / BatchSize;
            while (matrixBatches.Count < batchCount)
            {
                matrixBatches.Add(new Matrix4x4[BatchSize]);
                colorBatches.Add(new Vector4[BatchSize]);
                blocks.Add(new MaterialPropertyBlock());
            }

            batchCounts.Clear();
            Matrix4x4 localToWorld = transform.localToWorldMatrix;
            float baseScale = grainSize * grainFill;

            for (int b = 0; b < batchCount; b++)
            {
                int start = b * BatchSize;
                int count = Mathf.Min(BatchSize, visibleCells.Count - start);
                Matrix4x4[] matrices = matrixBatches[b];
                Vector4[] colors = colorBatches[b];

                for (int i = 0; i < count; i++)
                {
                    int cellIndex = visibleCells[start + i];
                    short owner = grid[cellIndex];
                    uint h = Hash((uint)cellIndex);
                    Vector3 jitter = new Vector3(Unit(h, 0) - 0.5f, Unit(h, 1) - 0.5f, Unit(h, 2) - 0.5f) * (2f * positionJitter * grainSize);
                    Vector3 local = CellToLocal(grid.Cell(cellIndex)) + jitter + blockOffsets[owner];
                    Quaternion rotation = Quaternion.Euler(Unit(h, 3) * 360f, Unit(h, 4) * 360f, Unit(h, 5) * 360f);
                    float scale = baseScale * (1f + (Unit(h, 6) - 0.5f) * 2f * sizeJitter);
                    matrices[i] = localToWorld * Matrix4x4.TRS(local, rotation, new Vector3(scale, scale, scale));
                    colors[i] = GrainColor(owner, cellIndex);
                }

                blocks[b].SetVectorArray(ColorId, colors);
                batchCounts.Add(count);
            }
        }

        private Color GrainColor(int block, int cellIndex)
        {
            // Instanced colours skip Unity's gamma → linear conversion, so convert here (project is Linear).
            Color baseColor = QualitySettings.activeColorSpace == ColorSpace.Linear ? blockColors[block].linear : blockColors[block];
            float k = 1f + (Unit(Hash((uint)cellIndex), 7) - 0.5f) * 2f * colorJitter;
            return new Color(baseColor.r * k, baseColor.g * k, baseColor.b * k, 1f);
        }

        // Stable per-grain random numbers so a grain keeps its look between rebuilds.
        private static uint Hash(uint x)
        {
            x ^= x >> 16;
            x *= 0x7feb352dU;
            x ^= x >> 15;
            x *= 0x846ca68bU;
            x ^= x >> 16;
            return x;
        }

        /// <summary>Value in [0, 1] from a hash and a channel index.</summary>
        private static float Unit(uint hash, int channel)
        {
            return (Hash(hash + (uint)channel * 0x9E3779B9U) & 0xFFFF) / 65535f;
        }

        private void KillShakes()
        {
            if (shakeTweens != null)
            {
                foreach (Tween t in shakeTweens)
                {
                    t?.Kill();
                }
            }
            if (popTweens != null)
            {
                foreach (Tween t in popTweens)
                {
                    t?.Kill();
                }
            }
            revealTween?.Kill();
            reveal = 0f;
        }

        /// <summary>Local centre of each block's cells (pivot for the tap pop).</summary>
        private Vector3[] BlockCenters(VoxelGrid g, int count)
        {
            var sums = new Vector3[count];
            var counts = new int[count];
            if (g != null)
            {
                for (int i = 0; i < g.CellCount; i++)
                {
                    int b = g[i];
                    if (b >= 0 && b < count)
                    {
                        sums[b] += CellToLocal(g.Cell(i));
                        counts[b]++;
                    }
                }
            }
            for (int b = 0; b < count; b++)
            {
                sums[b] = counts[b] > 0 ? sums[b] / counts[b] : Vector3.zero;
            }
            return sums;
        }

        private void OnDestroy()
        {
            KillShakes();
            DestroyBlockMeshes();
        }
    }
}
