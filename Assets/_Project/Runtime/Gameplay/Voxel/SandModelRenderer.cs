using System.Collections.Generic;
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
        [Tooltip("Laplacian smoothing passes on each block mesh.")]
        [SerializeField, Range(0, 6)] private int smoothIterations = 2;

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
        [SerializeField] private float drainDuration = 0.6f;
        [Tooltip("Seconds between mesh rebuilds while a block drains.")]
        [SerializeField] private float drainRebuildInterval = 0.04f;
        [Tooltip("Random spread (in cells) of the eroding edge so it looks like sand, not a sphere.")]
        [SerializeField] private float drainNoise = 3f;
        [Header("Tap feedback: punch (squash in, spring out, settle)")]
        [SerializeField] private float punchIn = 0.86f;
        [SerializeField] private float punchOut = 1.12f;
        [SerializeField] private float punchInTime = 0.06f;
        [SerializeField] private float punchOutTime = 0.1f;
        [SerializeField] private float punchSettleTime = 0.16f;
        [Tooltip("Scale of the block under the finger while pressed / held.")]
        [SerializeField] private float pressedScale = 0.9f;
        [SerializeField] private float pressInTime = 0.1f;
        [SerializeField] private float pressOutTime = 0.28f;

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
        /// <summary>Cells on the outside of a block (next to empty space or another block), for measuring what is left.</summary>
        private readonly List<int> boundaryCells = new List<int>();
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

        private sealed class Drain
        {
            public int[] Order;
            public float Start;
            public int Removed;
            public float LastBuild;
        }

        private BlockPhase[] phases;
        private Drain[] drains;
        private bool[] drainedCells;
        private readonly List<Vector3> meshVertices = new List<Vector3>();
        private readonly List<int> meshTriangles = new List<int>();

        public VoxelGrid Grid => grid;
        public float GrainSize => grainSize;
        public int VisibleGrainCount => visibleCells.Count;
        public float DrainDuration => drainDuration;

        /// <summary>
        /// Shows a new model. <paramref name="blockColors"/> has one colour per block. With
        /// <paramref name="hideInnerParents"/> (parent per block) a block is not drawn or hit while its parent is still
        /// there, so thin outer layers never let the colour inside show through.
        /// </summary>
        public void Build(VoxelGrid newGrid, float newGrainSize, Color[] newBlockColors, int[] hideInnerParents = null)
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
            drainedCells = newGrid != null ? new bool[newGrid.CellCount] : null;
            gridCenter = OccupiedCenter(newGrid);
            FindBoundaryCells();
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
            boundaryCells.Clear();
            decorations.Clear();
            visibleCells.Clear();
            batchCounts.Clear();
        }

        public bool IsCollected(int block) => collected != null && block >= 0 && block < collected.Length && collected[block];

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
            cellsDirty = true;
        }

        /// <summary>
        /// Starts eroding a collected block from <paramref name="fromCell"/> outwards over <see cref="DrainDuration"/>.
        /// Fills <paramref name="localGrains"/> with the block's grain positions (this transform's local space) in the
        /// order they leave, so flying sand can follow the eroding edge.
        /// </summary>
        public void BeginDrain(int block, Vector3Int fromCell, List<Vector3> localGrains)
        {
            localGrains.Clear();
            if (grid == null || phases == null || block < 0 || block >= phases.Length)
            {
                return;
            }

            var cells = new List<int>();
            for (int i = 0; i < grid.CellCount; i++)
            {
                if (grid[i] == block)
                {
                    cells.Add(i);
                }
            }
            int[] order = cells.ToArray();
            var keys = new float[order.Length];
            for (int i = 0; i < order.Length; i++)
            {
                Vector3Int c = grid.Cell(order[i]);
                keys[i] = Vector3.Distance(c, fromCell) + Unit(Hash((uint)order[i]), 8) * drainNoise;
            }
            System.Array.Sort(keys, order);
            foreach (int cell in order)
            {
                localGrains.Add(CellToLocal(grid.Cell(cell)));
            }

            if (phases[block] == BlockPhase.WaitingDrain)
            {
                phases[block] = BlockPhase.Draining;
                drains[block] = new Drain { Order = order, Start = Time.time, LastBuild = -1f };
            }
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
                return !IsBuried(b);
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
            return cell >= 0 ? grid[cell] : -1;
        }

        /// <summary>Adds the world position of every grain of the block (visible or buried).</summary>
        public void GetGrainWorldPositions(int block, List<Vector3> result)
        {
            if (grid == null)
            {
                return;
            }
            for (int i = 0; i < grid.CellCount; i++)
            {
                if (grid[i] == block)
                {
                    result.Add(transform.TransformPoint(CellToLocal(grid.Cell(i))));
                }
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
            foreach (int i in boundaryCells)
            {
                int owner = grid[i];
                if (owner < collected.Length && !collected[owner])
                {
                    localPoints.Add(CellToLocal(grid.Cell(i)));
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

        private void FindBoundaryCells()
        {
            boundaryCells.Clear();
            if (grid == null)
            {
                return;
            }
            Vector3Int s = grid.Size;
            for (int i = 0; i < grid.CellCount; i++)
            {
                short owner = grid[i];
                if (owner < 0)
                {
                    continue;
                }
                Vector3Int c = grid.Cell(i);
                if (Differs(c.x + 1, c.y, c.z, owner) || Differs(c.x - 1, c.y, c.z, owner) ||
                    Differs(c.x, c.y + 1, c.z, owner) || Differs(c.x, c.y - 1, c.z, owner) ||
                    Differs(c.x, c.y, c.z + 1, owner) || Differs(c.x, c.y, c.z - 1, owner))
                {
                    boundaryCells.Add(i);
                }
            }
        }

        private bool Differs(int x, int y, int z, short owner) => !grid.InBounds(x, y, z) || grid[grid.Index(x, y, z)] != owner;

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

            // Boundary cells hold every block's outer cells, so their box is the box of what is left.
            var min = new Vector3Int(int.MaxValue, int.MaxValue, int.MaxValue);
            var max = new Vector3Int(int.MinValue, int.MinValue, int.MinValue);
            foreach (int i in boundaryCells)
            {
                int owner = grid[i];
                if (owner >= 0 && (owner >= collected.Length || !collected[owner]))
                {
                    Vector3Int c = grid.Cell(i);
                    min = Vector3Int.Min(min, c);
                    max = Vector3Int.Max(max, c);
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
        /// Touch feedback on the block under the finger: squashes in while pressed / held, springs back (with a small
        /// overshoot) when released. A tap then plays <see cref="Punch"/> from the squashed size.
        /// </summary>
        public void SetPressed(int block, bool pressed)
        {
            if (popTweens == null || block < 0 || block >= popTweens.Length)
            {
                return;
            }

            popTweens[block]?.Kill();
            float target = pressed ? pressedScale : 1f;
            if (Mathf.Abs(blockScales[block] - target) < 1e-4f)
            {
                blockScales[block] = target;
                return;
            }
            popTweens[block] = DOTween.To(() => blockScales[block], v => blockScales[block] = v, target,
                    pressed ? pressInTime : pressOutTime)
                .SetEase(pressed ? Ease.OutQuad : Ease.OutBack)
                .SetLink(gameObject);
        }

        /// <summary>Tap feedback: the block squashes in, springs out past its size, then settles (it keeps draining).</summary>
        public void Punch(int block)
        {
            if (popTweens == null || block < 0 || block >= popTweens.Length)
            {
                return;
            }

            // Starts from the current scale, so a block already squashed by the press springs straight out.
            popTweens[block]?.Kill();
            DG.Tweening.Core.DOGetter<float> get = () => blockScales[block];
            DG.Tweening.Core.DOSetter<float> set = v => blockScales[block] = v;
            popTweens[block] = DOTween.Sequence()
                .Append(DOTween.To(get, set, punchIn, punchInTime).SetEase(Ease.OutQuad))
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
            var triangles = new List<int>();
            for (int b = 0; b < count; b++)
            {
                Mesh mesh = null;
                if (SurfaceNets.Build(grid, b, vertices, triangles, smoothIterations))
                {
                    for (int i = 0; i < vertices.Count; i++)
                    {
                        vertices[i] = (vertices[i] - gridCenter) * grainSize;
                    }
                    mesh = new Mesh { name = $"SandBlock_{b}" };
                    if (vertices.Count > 65000)
                    {
                        mesh.indexFormat = IndexFormat.UInt32;
                    }
                    mesh.SetVertices(vertices);
                    mesh.SetTriangles(triangles, 0);
                    mesh.RecalculateNormals();
                    mesh.RecalculateBounds();
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
                // Hidden inner layer; while holding, the one right inside the see-through block shows.
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
                Graphics.DrawMesh(mesh, matrix, ghost ? ghostMaterial : surfaceMaterial, gameObject.layer, null, 0, surfaceBlock,
                    ShadowCastingMode.Off, false);
            }
        }

        private void UpdateDrains()
        {
            if (drains == null)
            {
                return;
            }
            float now = Time.time;
            for (int b = 0; b < drains.Length; b++)
            {
                Drain d = drains[b];
                if (d == null || phases[b] != BlockPhase.Draining)
                {
                    continue;
                }

                float progress = drainDuration > 0f ? (now - d.Start) / drainDuration : 1f;
                if (progress >= 1f)
                {
                    phases[b] = BlockPhase.Gone;
                    drains[b] = null;
                    continue;
                }

                int target = Mathf.Clamp((int)(progress * d.Order.Length), 0, d.Order.Length);
                if (target <= d.Removed || now - d.LastBuild < drainRebuildInterval)
                {
                    continue;
                }
                for (int i = d.Removed; i < target; i++)
                {
                    drainedCells[d.Order[i]] = true;
                }
                d.Removed = target;
                d.LastBuild = now;
                if (!RebuildBlockMesh(b))
                {
                    phases[b] = BlockPhase.Gone;
                    drains[b] = null;
                }
            }
        }

        /// <summary>Remeshes a block without its drained cells. False when nothing is left to draw.</summary>
        private bool RebuildBlockMesh(int b)
        {
            Mesh mesh = blockMeshes[b];
            if (mesh == null || !SurfaceNets.Build(grid, b, meshVertices, meshTriangles, smoothIterations, drainedCells))
            {
                return false;
            }
            for (int i = 0; i < meshVertices.Count; i++)
            {
                meshVertices[i] = (meshVertices[i] - gridCenter) * grainSize;
            }
            mesh.Clear();
            mesh.indexFormat = meshVertices.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(meshVertices);
            mesh.SetTriangles(meshTriangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return true;
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
