using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using GameKit.Core.Contracts;
using GameKit.Core.Foundation.Events;
using GameKit.Core.Foundation.Logging;
using GameKit.Gameplay.Board;
using GameKit.Gameplay.Visuals;
using GameKit.Gameplay.Voxel;
using UnityEngine;

namespace GameKit.Gameplay.Level
{
    /// <summary>
    /// Turns board results into animation without ever blocking input (model → view, MVVM style):
    /// <see cref="SandBoard"/> is the model and changes instantly on every tap; this class plays the matching
    /// animation on the views. Each tray and each waiting slot is its own lane: animations on different lanes run
    /// at the same time, animations on one lane wait for each other (a tray only closes after every pour into it has
    /// landed; sand only pours into a new tray after it has popped in).
    /// </summary>
    public sealed class SandBoardPresenter
    {
        private const string Tag = "BoardPresenter";

        private readonly SandModelRenderer model;
        private readonly SandFlight flight;
        private readonly List<TrayView> trays;
        private readonly List<WaitingSlotView> slots;
        private readonly Func<int, Color> palette;

        // Structural animation chain per tray (complete → assign) and per waiting slot (fill → empty).
        private readonly Dictionary<int, UniTask> trayTail = new Dictionary<int, UniTask>();
        private readonly Dictionary<int, List<UniTask>> trayPours = new Dictionary<int, List<UniTask>>();
        private readonly Dictionary<int, UniTask> slotTail = new Dictionary<int, UniTask>();
        private readonly List<UniTask> running = new List<UniTask>();

        private CancellationToken token;
        private Color[] blockColors;

        public SandBoardPresenter(SandModelRenderer model, SandFlight flight, List<TrayView> trays,
            List<WaitingSlotView> slots, Func<int, Color> palette)
        {
            this.model = model;
            this.flight = flight;
            this.trays = trays;
            this.slots = slots;
            this.palette = palette;
        }

        /// <summary>True while any animation is still playing.</summary>
        public bool IsAnimating
        {
            get
            {
                Prune();
                return running.Count > 0;
            }
        }

        /// <summary>New level: forget all lanes. Running work stops through <paramref name="levelToken"/>.</summary>
        public void Reset(CancellationToken levelToken, Color[] colors)
        {
            token = levelToken;
            blockColors = colors;
            trayTail.Clear();
            trayPours.Clear();
            slotTail.Clear();
            running.Clear();
        }

        /// <summary>
        /// Schedules the animation for one board result. <paramref name="block"/> is the tapped block (-1 if none)
        /// and <paramref name="fromCell"/> the grid cell that was hit, where the block starts to drain.
        /// </summary>
        public void Play(IReadOnlyList<BoardStep> steps, int block, Vector3Int fromCell)
        {
            Prune();
            foreach (BoardStep step in steps)
            {
                Schedule(step, block, fromCell);
            }
        }

        /// <summary>Completes once every scheduled animation (including ones added meanwhile) has finished.</summary>
        public async UniTask WhenIdleAsync()
        {
            while (true)
            {
                Prune();
                if (running.Count == 0)
                {
                    return;
                }
                await UniTask.WhenAll(running.ToArray());
            }
        }

        private void Schedule(BoardStep step, int block, Vector3Int fromCell)
        {
            switch (step.Kind)
            {
                case BoardStepKind.BlockToTray:
                {
                    int t = step.Tray;
                    UniTask after = TrayTail(t);
                    Func<JarView> reserve = () => trays[t].ReserveJar();
                    UniTask pour = PourBlock(step.Block, fromCell, after, reserve, HoverPoint(trays[t].transform));
                    TrayPours(t).Add(pour);
                    break;
                }
                case BoardStepKind.BlockToWaiting:
                {
                    int s = step.Slot;
                    UniTask after = SlotTail(s);
                    Color color = palette(step.Color);
                    Func<JarView> reserve = () => slots[s].Reserve(color);
                    slotTail[s] = PourBlock(step.Block, fromCell, after, reserve, HoverPoint(slots[s].transform));
                    break;
                }
                case BoardStepKind.WaitingToTray:
                {
                    int s = step.Slot, t = step.Tray;
                    UniTask afterSlot = SlotTail(s);
                    UniTask afterTray = TrayTail(t);
                    Color color = palette(step.Color);
                    UniTask move = Run(async () =>
                    {
                        await UniTask.WhenAll(afterSlot, afterTray);
                        token.ThrowIfCancellationRequested();
                        WaitingSlotView slot = slots[s];
                        JarView jar = trays[t].ReserveJar();
                        await PourAsync(null, PilePoints(slot.Jar.MouthPosition), color, slot.Jar, jar, flight.Spread);
                        slot.Clear();
                    });
                    slotTail[s] = move;
                    TrayPours(t).Add(move);
                    break;
                }
                case BoardStepKind.TrayCompleted:
                {
                    int t = step.Tray;
                    UniTask after = TrayTail(t);
                    UniTask[] pours = TrayPours(t).ToArray();
                    trayPours[t] = new List<UniTask>();
                    trayTail[t] = Run(async () =>
                    {
                        await after;
                        await UniTask.WhenAll(pours);
                        token.ThrowIfCancellationRequested();
                        EventBus<TrayCompleted>.Raise(new TrayCompleted { Tray = t, Color = step.Color });
                        await trays[t].PlayCompleteAsync(token);
                    });
                    break;
                }
                case BoardStepKind.TrayAssigned:
                {
                    int t = step.Tray;
                    UniTask after = TrayTail(t);
                    // An unlocked tray has no pours yet, so its assign only waits for its own chain.
                    UniTask[] pours = TrayPours(t).ToArray();
                    Color color = palette(step.Color);
                    trayTail[t] = Run(async () =>
                    {
                        await after;
                        await UniTask.WhenAll(pours);
                        token.ThrowIfCancellationRequested();
                        await trays[t].PlayAssignAsync(step.Color, color, token);
                    });
                    break;
                }
            }
        }

        /// <summary>
        /// The block drains right away (the tap always answers at once). When its jar is ready the sand flies
        /// straight in; when the tray / slot is still busy (closing, popping in, emptying) the sand gathers and
        /// swirls above it, then drops in once <paramref name="after"/> completes.
        /// </summary>
        private UniTask PourBlock(int block, Vector3Int fromCell, UniTask after, Func<JarView> reserveJar, Vector3 hover)
        {
            var localGrains = new List<Vector3>();
            model.BeginDrain(block, fromCell, localGrains);
            Color color = blockColors != null && block >= 0 && block < blockColors.Length ? blockColors[block] : Color.white;
            float leave = model.DrainDuration;

            if (after.Status != UniTaskStatus.Pending)
            {
                return Run(() => PourAsync(model.transform, localGrains, color, null, reserveJar(), leave));
            }

            SandStream stream = flight.LaunchHeld(model.transform, localGrains, color, hover, leave);
            return Run(async () =>
            {
                await after;
                token.ThrowIfCancellationRequested();
                JarView jar = reserveJar();
                if (jar == null)
                {
                    Log.Warning(Tag, "No free jar for held sand.");
                    stream.Release(hover);
                    return;
                }
                stream.Release(jar.MouthPosition);
                float first = Mathf.Max(0f, flight.FirstReleasedLanding(stream));
                float last = Mathf.Max(first + 0.05f, flight.LastReleasedLanding(stream));
                UniTask fill = jar.FillToAsync(1f, first, last - first, token);
                UniTask land = UniTask.Delay(TimeSpan.FromSeconds(last), cancellationToken: token);
                await UniTask.WhenAll(fill, land);
            });
        }

        /// <summary>Where held sand waits: a little above the tray / slot.</summary>
        private static Vector3 HoverPoint(Transform target)
        {
            return target.position + Vector3.up * 0.4f;
        }

        /// <summary>
        /// Sand flies from the sources into <paramref name="to"/>; the target jar fills while grains land and the
        /// source jar (waiting slot) empties while they leave.
        /// </summary>
        private UniTask PourAsync(Transform source, List<Vector3> points, Color color, JarView from, JarView to, float leaveTime)
        {
            if (to == null)
            {
                Log.Warning(Tag, "No free jar for arriving sand.");
                return UniTask.CompletedTask;
            }

            float landStart = flight.FirstArrival;
            float landTime = Mathf.Max(0.05f, flight.LastArrivalFor(leaveTime) - landStart);
            UniTask fly = flight.FlyAsync(source, points, color, to.MouthPosition, leaveTime, token);
            UniTask fill = to.FillToAsync(1f, landStart, landTime, token);
            UniTask drain = from != null ? from.FillToAsync(0f, 0f, leaveTime + 0.1f, token) : UniTask.CompletedTask;
            return UniTask.WhenAll(fly, fill, drain);
        }

        /// <summary>
        /// Starts <paramref name="body"/> and returns a task that several later steps may await at the same time
        /// (a completion source, since a plain or preserved UniTask allows only one awaiter while pending).
        /// </summary>
        private UniTask Run(Func<UniTask> body)
        {
            var done = new UniTaskCompletionSource();
            RunAsync(body, done).Forget();
            UniTask task = done.Task;
            running.Add(task);
            return task;
        }

        /// <summary>Cancellation (restart / next level) just ends the lane; later steps check the token themselves.</summary>
        private static async UniTaskVoid RunAsync(Func<UniTask> body, UniTaskCompletionSource done)
        {
            try
            {
                await body();
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception e)
            {
                Log.Exception(e);
            }
            finally
            {
                done.TrySetResult();
            }
        }

        private UniTask TrayTail(int tray)
        {
            return trayTail.TryGetValue(tray, out UniTask task) ? task : UniTask.CompletedTask;
        }

        private UniTask SlotTail(int slot)
        {
            return slotTail.TryGetValue(slot, out UniTask task) ? task : UniTask.CompletedTask;
        }

        private List<UniTask> TrayPours(int tray)
        {
            if (!trayPours.TryGetValue(tray, out List<UniTask> list))
            {
                list = new List<UniTask>();
                trayPours[tray] = list;
            }
            return list;
        }

        private void Prune()
        {
            running.RemoveAll(t => t.Status != UniTaskStatus.Pending);
        }

        private static List<Vector3> PilePoints(Vector3 centre)
        {
            var points = new List<Vector3>(40);
            for (int i = 0; i < 40; i++)
            {
                float a = i * 2.39996f;
                float r = 0.12f * Mathf.Sqrt(i / 40f);
                points.Add(centre + new Vector3(Mathf.Cos(a) * r, (i % 5) * 0.02f, Mathf.Sin(a) * r));
            }
            return points;
        }
    }
}
