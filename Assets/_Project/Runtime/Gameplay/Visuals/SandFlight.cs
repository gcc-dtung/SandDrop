using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using GameKit.Gameplay.Voxel;
using UnityEngine;
using UnityEngine.Rendering;

namespace GameKit.Gameplay.Visuals
{
    /// <summary>
    /// Flying sand: grains drawn with GPU instancing along curved paths (no GameObject per grain).
    /// <see cref="FlyAsync"/> launches a stream of grains from source points to a target and completes when the
    /// last grain lands.
    /// </summary>
    public sealed class SandFlight : MonoBehaviour
    {
        private const int BatchSize = 1023;
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        [SerializeField] private Material grainMaterial;
        [Tooltip("Empty = generated icosahedron.")]
        [SerializeField] private Mesh grainMesh;
        [SerializeField] private float grainScale = 0.055f;
        [Tooltip("Grains per stream, sampled from the block's grains.")]
        [SerializeField] private int maxGrainsPerStream = 140;
        [Tooltip("Seconds between the first and the last grain leaving.")]
        [SerializeField] private float spread = 0.22f;
        [Tooltip("Most grains leave at the start (ease-out, like the block vanishing) instead of evenly over the leave time.")]
        [SerializeField] private bool frontLoadedLaunch = true;
        [SerializeField] private float flightTime = 0.38f;
        [Tooltip("How high the curve rises above the straight line.")]
        [SerializeField] private float arcHeight = 0.9f;
        [Tooltip("Grains land scattered inside this radius around the target.")]
        [SerializeField] private float landRadius = 0.05f;

        [Header("Held sand (target tray not ready yet)")]
        [SerializeField] private float holdRadius = 0.12f;
        [SerializeField] private float holdSpin = 4f;
        [Tooltip("Flight time from the hover point into the jar once released.")]
        [SerializeField] private float releaseFlightTime = 0.28f;
        [Tooltip("Grains leave the hover point over this many seconds once released.")]
        [SerializeField] private float releaseSpread = 0.18f;

        private struct Grain
        {
            public Vector3 Start;
            public Vector3 Control;
            /// <summary>Offset of the curve's control point from the mid point.</summary>
            public Vector3 Lift;
            /// <summary>When set, Start is <see cref="LocalStart"/> in this transform, read at launch time.</summary>
            public Transform Source;
            public Vector3 LocalStart;
            public bool Launched;
            public Vector3 End;
            public float StartTime;
            public float Duration;
            public Vector4 Color;
            public Quaternion Spin;
            /// <summary>Held grains: End is the hover point; they wait there until the stream is released.</summary>
            public SandStream Stream;
            public float HoldPhase;
            public float HoldRadius;
            public float ReleaseDelay;
            public Vector3 LandOffset;
        }

        private readonly List<Grain> grains = new List<Grain>();
        private readonly List<Matrix4x4[]> matrixBatches = new List<Matrix4x4[]>();
        private readonly List<Vector4[]> colorBatches = new List<Vector4[]>();
        private readonly List<MaterialPropertyBlock> blocks = new List<MaterialPropertyBlock>();
        private readonly List<int> batchCounts = new List<int>();
        private uint seed = 12345;

        public int ActiveGrains => grains.Count;

        /// <summary>Seconds after <see cref="FlyAsync"/> starts until the first grains land.</summary>
        public float FirstArrival => flightTime * 0.85f;

        /// <summary>Seconds after <see cref="FlyAsync"/> starts until the last grain lands.</summary>
        public float LastArrival => spread + flightTime * 1.15f;

        /// <summary>Seconds between the first and the last grain leaving.</summary>
        public float Spread => spread;

        /// <summary>Seconds until the last grain lands when grains leave over <paramref name="leaveTime"/>.</summary>
        public float LastArrivalFor(float leaveTime) => leaveTime + flightTime * 1.15f;

        /// <summary>Sends a stream of grains from <paramref name="sources"/> (world) to <paramref name="target"/>.</summary>
        public UniTask FlyAsync(IReadOnlyList<Vector3> sources, Color color, Vector3 target, CancellationToken token)
        {
            return FlyAsync(null, sources, color, target, spread, token);
        }

        /// <summary>
        /// Stream of grains leaving <paramref name="sources"/> in list order over <paramref name="leaveTime"/> seconds.
        /// With <paramref name="source"/> set, sources are local to it and each grain reads its start point when it
        /// leaves, so a model that moves or rotates meanwhile still sheds sand from the right place.
        /// </summary>
        public async UniTask FlyAsync(Transform source, IReadOnlyList<Vector3> sources, Color color, Vector3 target,
            float leaveTime, CancellationToken token)
        {
            if (sources == null || sources.Count == 0)
            {
                return;
            }
            AddGrains(source, sources, color, target, leaveTime, null);
            float total = LastArrivalFor(leaveTime);
            await UniTask.Delay(System.TimeSpan.FromSeconds(total), cancellationToken: token);
        }

        /// <summary>
        /// Like <see cref="FlyAsync(Transform, IReadOnlyList{Vector3}, Color, Vector3, float, CancellationToken)"/>
        /// but the grains gather and swirl at <paramref name="hover"/> until the returned stream is released.
        /// </summary>
        public SandStream LaunchHeld(Transform source, IReadOnlyList<Vector3> sources, Color color, Vector3 hover, float leaveTime)
        {
            var stream = new SandStream();
            float now = Time.time;
            stream.FirstHoverArrival = now + FirstArrival;
            stream.LastHoverArrival = now + LastArrivalFor(leaveTime);
            if (sources != null && sources.Count > 0)
            {
                AddGrains(source, sources, color, hover, leaveTime, stream);
            }
            return stream;
        }

        /// <summary>Seconds from now until the first held grain lands in its jar (call after Release).</summary>
        public float FirstReleasedLanding(SandStream stream)
        {
            float now = Time.time;
            return Mathf.Max(stream.FirstHoverArrival, stream.ReleaseTime) + releaseFlightTime * 0.8f - now;
        }

        /// <summary>Seconds from now until the last held grain lands in its jar (call after Release).</summary>
        public float LastReleasedLanding(SandStream stream)
        {
            float now = Time.time;
            return Mathf.Max(stream.LastHoverArrival, stream.ReleaseTime + releaseSpread) + releaseFlightTime - now;
        }

        private void AddGrains(Transform source, IReadOnlyList<Vector3> sources, Color color, Vector3 target,
            float leaveTime, SandStream stream)
        {
            if (grainMesh == null)
            {
                grainMesh = GrainMesh.CreateIcosahedron();
            }

            Color linear = QualitySettings.activeColorSpace == ColorSpace.Linear ? color.linear : color;
            int count = Mathf.Min(maxGrainsPerStream, sources.Count);
            float now = Time.time;
            float step = sources.Count / (float)count;
            for (int i = 0; i < count; i++)
            {
                Vector3 point = sources[Mathf.Min(sources.Count - 1, (int)(i * step + Random01() * step))];
                Vector3 end = target + new Vector3(RandomSigned(), RandomSigned() * 0.5f, RandomSigned()) * landRadius;
                Vector3 lift = Vector3.up * (arcHeight * (0.7f + Random01() * 0.6f)) +
                               new Vector3(RandomSigned(), 0f, RandomSigned()) * 0.25f;
                float shade = 0.88f + Random01() * 0.24f;
                grains.Add(new Grain
                {
                    Start = point,
                    Control = (point + end) * 0.5f + lift,
                    Lift = lift,
                    Source = source,
                    LocalStart = point,
                    Launched = source == null,
                    End = end,
                    StartTime = now + leaveTime * LaunchFraction(i / (float)count),
                    Duration = flightTime * (0.85f + Random01() * 0.3f),
                    Color = new Vector4(linear.r * shade, linear.g * shade, linear.b * shade, 1f),
                    Spin = Quaternion.Euler(Random01() * 360f, Random01() * 360f, Random01() * 360f),
                    Stream = stream,
                    HoldPhase = Random01() * Mathf.PI * 2f,
                    HoldRadius = holdRadius * (0.4f + Random01() * 0.6f),
                    ReleaseDelay = Random01() * releaseSpread,
                    LandOffset = new Vector3(RandomSigned(), RandomSigned() * 0.5f, RandomSigned()) * landRadius
                });
            }
        }

        /// <summary>
        /// When (0..1 of the leave time) the grain at <paramref name="f"/> (0..1 along the stream) leaves. Front-loaded
        /// it is the inverse of the block's ease-out vanish, so grains leave as fast as the sand disappears.
        /// </summary>
        private float LaunchFraction(float f)
        {
            return frontLoadedLaunch ? 1f - Mathf.Sqrt(Mathf.Clamp01(1f - f)) : f;
        }

        public void Clear()
        {
            grains.Clear();
            batchCounts.Clear();
        }

        private void LateUpdate()
        {
            batchCounts.Clear();
            if (grains.Count == 0 || grainMaterial == null)
            {
                return;
            }

            float now = Time.time;
            int written = 0;
            for (int i = grains.Count - 1; i >= 0; i--)
            {
                Grain g = grains[i];
                float t = (now - g.StartTime) / g.Duration;
                if (t >= 1f && g.Stream != null)
                {
                    // Held grain at its hover point: swirl, or fly into the jar once released.
                    if (!DrawHeld(ref g, now, ref written))
                    {
                        grains.RemoveAt(i);
                    }
                    continue;
                }
                if (t >= 1f)
                {
                    grains.RemoveAt(i);
                    continue;
                }
                if (t < 0f)
                {
                    continue; // not launched yet: still part of the model's look
                }
                if (!g.Launched)
                {
                    g.Launched = true;
                    if (g.Source != null)
                    {
                        g.Start = g.Source.TransformPoint(g.LocalStart);
                        g.Control = (g.Start + g.End) * 0.5f + g.Lift;
                    }
                    grains[i] = g;
                }

                // Ease-out: a grain leaves at full speed the moment the finger lifts, then slows into the jar.
                float e = t * (2f - t);
                float u = 1f - e;
                Vector3 pos = u * u * g.Start + 2f * u * e * g.Control + e * e * g.End;
                float scale = grainScale * (t > 0.8f ? Mathf.Lerp(1f, 0.6f, (t - 0.8f) / 0.2f) : 1f);
                Write(written++, Matrix4x4.TRS(pos, g.Spin, Vector3.one * scale), g.Color);
            }

            for (int b = 0; b * BatchSize < written; b++)
            {
                int count = Mathf.Min(BatchSize, written - b * BatchSize);
                blocks[b].SetVectorArray(ColorId, colorBatches[b]);
                Graphics.DrawMeshInstanced(grainMesh, 0, grainMaterial, matrixBatches[b], count, blocks[b],
                    ShadowCastingMode.Off, false, gameObject.layer);
            }
        }

        private Vector3 HoverPosition(in Grain g, float time)
        {
            float a = g.HoldPhase + time * holdSpin;
            // Radius grows in after arrival so a grain does not jump when it starts to swirl.
            float radius = g.HoldRadius * Mathf.Clamp01((time - (g.StartTime + g.Duration)) / 0.25f);
            return g.End + new Vector3(Mathf.Cos(a), Mathf.Sin(a * 1.7f) * 0.4f, Mathf.Sin(a)) * radius;
        }

        /// <summary>Draws a held grain. False when it has landed in its jar.</summary>
        private bool DrawHeld(ref Grain g, float now, ref int written)
        {
            float arrived = g.StartTime + g.Duration;
            SandStream stream = g.Stream;
            float leave = Mathf.Max(arrived, stream.ReleaseTime + g.ReleaseDelay);
            if (!stream.Released || now < leave)
            {
                Write(written++, Matrix4x4.TRS(HoverPosition(g, now), g.Spin, Vector3.one * grainScale), g.Color);
                return true;
            }

            float t = (now - leave) / releaseFlightTime;
            if (t >= 1f)
            {
                return false;
            }
            Vector3 from = HoverPosition(g, leave);
            Vector3 to = stream.Target + g.LandOffset;
            Vector3 control = (from + to) * 0.5f + Vector3.up * 0.15f;
            float e = t * t;
            float u = 1f - e;
            Vector3 pos = u * u * from + 2f * u * e * control + e * e * to;
            float scale = grainScale * (t > 0.8f ? Mathf.Lerp(1f, 0.6f, (t - 0.8f) / 0.2f) : 1f);
            Write(written++, Matrix4x4.TRS(pos, g.Spin, Vector3.one * scale), g.Color);
            return true;
        }

        private void Write(int index, Matrix4x4 matrix, Vector4 color)
        {
            int batch = index / BatchSize;
            while (matrixBatches.Count <= batch)
            {
                matrixBatches.Add(new Matrix4x4[BatchSize]);
                colorBatches.Add(new Vector4[BatchSize]);
                blocks.Add(new MaterialPropertyBlock());
            }
            matrixBatches[batch][index % BatchSize] = matrix;
            colorBatches[batch][index % BatchSize] = color;
        }

        private float Random01()
        {
            seed ^= seed << 13;
            seed ^= seed >> 17;
            seed ^= seed << 5;
            return (seed & 0xFFFFFF) / 16777215f;
        }

        private float RandomSigned() => Random01() * 2f - 1f;

        private void OnDisable()
        {
            Clear();
        }
    }
}
