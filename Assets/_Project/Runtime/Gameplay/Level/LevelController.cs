using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using GameKit.Core.Contracts;
using GameKit.Core.Foundation.Events;
using GameKit.Core.Foundation.Logging;
using GameKit.Core.Foundation.Pool;
using GameKit.Gameplay.Board;
using GameKit.Gameplay.Input;
using GameKit.Gameplay.UI;
using GameKit.Gameplay.Visuals;
using GameKit.Gameplay.Voxel;
using GameKit.UI.Framework;
using UnityEngine;

namespace GameKit.Gameplay.Level
{
    /// <summary>
    /// Runs one level: builds the board, the sand model, the tray row and the waiting row, turns taps into board
    /// moves and replays each move as animation (sand flying into trays / waiting slots, trays completing).
    /// Taps are locked while a move plays (reference: IsPlayAnming).
    /// End of level: win popup -> next level (progress saved); lose popup -> revive once (+2 waiting slots) or retry.
    /// </summary>
    public sealed class LevelController : MonoBehaviour
    {
        private const string Tag = "Level";

        [SerializeField] private SandModelRenderer model;
        [Tooltip("Pivot parent of the model: rotation, zoom and recentring.")]
        [SerializeField] private SandModelView view;
        [SerializeField] private SandInput input;
        [SerializeField] private SandFlight flight;

        [Header("Rows (world space, not rotated with the model)")]
        [SerializeField] private Transform rowsRoot;
        [Tooltip("Inactive template in the scene.")]
        [SerializeField] private TrayView trayTemplate;
        [Tooltip("Inactive template in the scene.")]
        [SerializeField] private WaitingSlotView waitingTemplate;
        [SerializeField] private float trayRowY = 2.55f;
        [SerializeField] private float traySpacing = 1.15f;
        [SerializeField] private float waitingRowY = 1.7f;
        [SerializeField] private float waitingSpacing = 0.6f;
        [Tooltip("Waiting row never gets wider than this; slots squeeze when revive adds more.")]
        [SerializeField] private float waitingMaxWidth = 2.9f;

        [Header("End of level")]
        [Tooltip("Pause after the last animation before the win / lose popup.")]
        [SerializeField] private float endDelay = 0.6f;

        private readonly List<TrayView> trays = new List<TrayView>();
        private readonly List<WaitingSlotView> slots = new List<WaitingSlotView>();
        private ComponentPool<TrayView> trayPool;
        private ComponentPool<WaitingSlotView> slotPool;
        private CancellationTokenSource levelCts;
        private LevelDatabase database;
        private ISaveService save;
        private PopupManager popups;
        private LevelHud hud;
        private SandLevelData level;
        private Color[] blockColors;
        /// <summary>True once the level has ended (taps stay off until the next level / revive).</summary>
        private bool busy;
        private SandBoardPresenter presenter;
        private int pressedBlock = -1;
        private bool paused;

        public SandBoard Board { get; private set; }
        public int LevelIndex { get; private set; }
        public SandLevelData CurrentLevel => level;
        public bool IsBusy => busy || (presenter != null && presenter.IsAnimating);

        /// <summary>Raised after a level ends (true = won), before the next one starts.</summary>
        public event Action<bool> LevelEnded;

        public void Init(LevelDatabase levels, ISaveService saveService, PopupManager popupManager, LevelHud levelHud)
        {
            database = levels;
            save = saveService;
            popups = popupManager;
            hud = levelHud;
            if (hud != null)
            {
                hud.PauseClicked -= OnPauseClicked;
                hud.PauseClicked += OnPauseClicked;
                hud.RecenterClicked -= ResetView;
                hud.RecenterClicked += ResetView;
                hud.ZoomChanged -= SetZoom;
                hud.ZoomChanged += SetZoom;
            }
            if (input != null)
            {
                input.Tapped -= OnTapped;
                input.Tapped += OnTapped;
                input.HoldStarted -= OnHoldStarted;
                input.HoldStarted += OnHoldStarted;
                input.HoldEnded -= OnHoldEnded;
                input.HoldEnded += OnHoldEnded;
                input.PressDown -= OnPressDown;
                input.PressDown += OnPressDown;
                input.PressUp -= OnPressUp;
                input.PressUp += OnPressUp;
            }

            if (trayTemplate != null)
            {
                trayPool ??= new ComponentPool<TrayView>(trayTemplate, rowsRoot, 3, 10);
            }
            if (waitingTemplate != null)
            {
                slotPool ??= new ComponentPool<WaitingSlotView>(waitingTemplate, rowsRoot, 7, 20);
            }

            presenter ??= new SandBoardPresenter(model, flight, trays, slots, PaletteColor);

            if (database == null || database.Count == 0)
            {
                Log.Error(Tag, "No levels: assign a LevelDatabase in GameConfig.", this);
                return;
            }
            int saved = save != null ? save.Load<LevelProgressData>(LevelProgressData.Key).levelIndex : 0;
            StartLevel(Mathf.Max(0, saved));
        }

        public void Restart()
        {
            if (database != null && database.Count > 0)
            {
                StartLevel(LevelIndex);
            }
        }

        public void StartLevel(int index)
        {
            levelCts?.Cancel();
            levelCts?.Dispose();
            levelCts = CancellationTokenSource.CreateLinkedTokenSource(this.GetCancellationTokenOnDestroy());
            busy = false;
            if (input != null)
            {
                input.TapsLocked = false;
            }
            if (flight != null)
            {
                flight.Clear();
            }

            LevelIndex = index;
            level = database.Get(index);
            Board = new SandBoard(level.ToBoardBlocks(), level.trayColorQueue);

            blockColors = new Color[level.blocks.Count];
            for (int i = 0; i < blockColors.Length; i++)
            {
                blockColors[i] = PaletteColor(level.blocks[i].colorIndex);
            }

            if (hud != null)
            {
                hud.SetZoomWithoutNotify(1f);
            }
            model.Build(level.BuildGrid(), level.grainSize, blockColors, level.hideInnerBlocks ? level.Parents() : null);
            model.SetDecorations(level.decorations);
            if (view != null)
            {
                view.ResetForLevel(Quaternion.Euler(level.modelRotation));
            }

            presenter?.Reset(levelCts.Token, blockColors);
            BuildRows();
            if (hud != null)
            {
                hud.SetLevel(index + 1);
            }
            Log.Info(Tag, $"Level {index + 1} ({level.name}): {level.blocks.Count} blocks.");
            EventBus<LevelStarted>.Raise(new LevelStarted { LevelIndex = index });
        }

        /// <summary>User zoom from the HUD (1 = level default, on top of the automatic zoom).</summary>
        public void SetZoom(float zoom)
        {
            if (view != null)
            {
                view.SetUserZoom(zoom);
            }
        }

        /// <summary>Puts the model back to the level's start rotation and zoom.</summary>
        public void ResetView()
        {
            if (level == null)
            {
                return;
            }
            if (view != null)
            {
                view.ResetView(Quaternion.Euler(level.modelRotation));
            }
            if (hud != null)
            {
                hud.SetZoomWithoutNotify(1f);
            }
        }

        private void OnPauseClicked()
        {
            PauseAsync(levelCts.Token).Forget();
        }

        /// <summary>Freezes the level (time scale 0) and asks: resume or restart. Closing the popup resumes.</summary>
        private async UniTaskVoid PauseAsync(CancellationToken token)
        {
            ConfirmPopup popup = popups != null ? popups.Get<ConfirmPopup>() : null;
            if (paused || popup == null || popup.IsOpen || Board == null || Board.State != BoardState.Playing)
            {
                return;
            }

            paused = true;
            float previousScale = Time.timeScale;
            Time.timeScale = 0f;
            var (canceled, restart) = await popup.AskAsync("Take a break.", "Paused", "Restart", "Resume", token)
                .SuppressCancellationThrow();
            Time.timeScale = previousScale;
            paused = false;
            if (!canceled && restart)
            {
                Restart();
            }
        }

        /// <summary>Cheat / debug: ends the level now and shows the win or lose popup.</summary>
        public void DebugEndLevel(bool won)
        {
            if (Board == null || Board.State != BoardState.Playing)
            {
                return;
            }
            Board.ForceState(won ? BoardState.Won : BoardState.Failed);
            EndLevelAsync(won, levelCts.Token).Forget();
        }

        /// <summary>Opens the locked tray (prototype stand-in for "watch an ad").</summary>
        public void UnlockTray()
        {
            if (busy || Board == null)
            {
                return;
            }
            var steps = new List<BoardStep>(Board.UnlockTray());
            if (steps.Count > 0)
            {
                EventBus<TrayUnlocked>.Raise(new TrayUnlocked { Tray = steps[0].Tray });
                presenter.Play(steps, -1, Vector3Int.zero);
                AfterBoardChanged();
            }
        }

        /// <summary>Revive after a fail: +2 waiting slots, play on.</summary>
        public bool Revive()
        {
            if (Board == null || !Board.Revive())
            {
                return false;
            }
            for (int i = 0; i < SandBoard.ReviveExtraSlots; i++)
            {
                slots.Add(slotPool.Get());
            }
            LayoutWaitingRow();
            RefreshDanger();
            EventBus<ReviveUsed>.Raise(new ReviveUsed { LevelIndex = LevelIndex });
            return true;
        }

        private void OnDestroy()
        {
            if (input != null)
            {
                input.Tapped -= OnTapped;
                input.HoldStarted -= OnHoldStarted;
                input.HoldEnded -= OnHoldEnded;
                input.PressDown -= OnPressDown;
                input.PressUp -= OnPressUp;
            }
            if (hud != null)
            {
                hud.PauseClicked -= OnPauseClicked;
                hud.RecenterClicked -= ResetView;
                hud.ZoomChanged -= SetZoom;
            }
            levelCts?.Cancel();
            levelCts?.Dispose();
            trayPool?.Dispose();
            slotPool?.Dispose();
        }

        /// <summary>Touch feedback: the block under the finger squashes in until the press ends.</summary>
        private void OnPressDown(Vector2 screenPosition)
        {
            pressedBlock = -1;
            if (level == null || input.ViewCamera == null)
            {
                return;
            }
            int cell = model.RaycastCell(input.ViewCamera.ScreenPointToRay(screenPosition));
            if (cell < 0)
            {
                return;
            }
            int block = model.Grid[cell];
            if (block < 0 || model.IsCollected(block))
            {
                return;
            }
            pressedBlock = block;
            model.SetPressed(block, true);
        }

        /// <summary>Press over (lifted or turned into a drag): the block springs back; a tap then punches it.</summary>
        private void OnPressUp()
        {
            if (pressedBlock >= 0)
            {
                model.SetPressed(pressedBlock, false);
                pressedBlock = -1;
            }
        }

        /// <summary>
        /// Hold to see inside: the held block turns see-through, showing only the layer right inside it (that layer
        /// stays solid). Blocks with nothing left inside only keep the press squash.
        /// </summary>
        private void OnHoldStarted(Vector2 screenPosition)
        {
            if (level == null || input.ViewCamera == null)
            {
                return;
            }
            int cell = model.RaycastCell(input.ViewCamera.ScreenPointToRay(screenPosition));
            if (cell < 0)
            {
                return;
            }
            int block = model.Grid[cell];
            if (block < 0 || model.IsCollected(block) || !HasSandInside(block))
            {
                return;
            }
            model.SetReveal(block, true);
        }

        /// <summary>True when a direct child of the block is still on the model.</summary>
        private bool HasSandInside(int block)
        {
            for (int i = 0; i < level.blocks.Count; i++)
            {
                if (level.blocks[i].parent == block && !model.IsCollected(i))
                {
                    return true;
                }
            }
            return false;
        }

        private void OnHoldEnded()
        {
            model.SetReveal(-1, false);
        }

        private void OnTapped(Vector2 screenPosition)
        {
            if (busy || Board == null || Board.State != BoardState.Playing || input.ViewCamera == null)
            {
                return;
            }

            Ray ray = input.ViewCamera.ScreenPointToRay(screenPosition);
            if (TryTapLockedTray(ray))
            {
                return;
            }

            int cell = model.RaycastCell(ray);
            if (cell < 0)
            {
                return;
            }
            int block = model.Grid[cell];
            if (model.IsCollected(block))
            {
                return; // Tapped sand that is already on its way out.
            }
            Vector3Int hitCell = model.Grid.Cell(cell);

            if (!Board.CanTap(block))
            {
                model.Shake(block, input.ViewCamera.transform.right);
                EventBus<BlockBlocked>.Raise(new BlockBlocked { Block = block });
                return;
            }

            // Model first (instant), then the view catches up: no waiting before the next tap.
            var steps = new List<BoardStep>(Board.Collect(block));
            model.SetCollected(block, true, keepVisible: true);
            model.Punch(block);
            if (view != null)
            {
                view.Refit();
            }
            EventBus<BlockCollected>.Raise(new BlockCollected
            {
                Block = block,
                Color = level.blocks[block].colorIndex,
                ToTray = steps.Count > 0 && steps[0].Kind == BoardStepKind.BlockToTray
            });
            presenter.Play(steps, block, hitCell);
            AfterBoardChanged();
        }

        private bool TryTapLockedTray(Ray ray)
        {
            if (!Physics.Raycast(ray, out RaycastHit hit, 100f))
            {
                return false;
            }
            foreach (TrayView tray in trays)
            {
                if (tray.Locked && hit.collider.transform.IsChildOf(tray.transform))
                {
                    UnlockTray();
                    return true;
                }
            }
            return false;
        }

        /// <summary>Board state changed: update the danger hint now; when the level is over, wait for the animation, then end it.</summary>
        private void AfterBoardChanged()
        {
            RefreshDanger();
            if (busy || Board.State == BoardState.Playing)
            {
                return;
            }
            busy = true;
            input.TapsLocked = true;
            EndWhenIdleAsync(levelCts.Token).Forget();
        }

        private async UniTaskVoid EndWhenIdleAsync(CancellationToken token)
        {
            await presenter.WhenIdleAsync();
            if (token.IsCancellationRequested)
            {
                return;
            }
            bool won = Board.State == BoardState.Won;
            Log.Info(Tag, won ? $"Level {LevelIndex + 1} won." : $"Level {LevelIndex + 1} failed: waiting row full.");
            EndLevelAsync(won, token).Forget();
        }

        private async UniTaskVoid EndLevelAsync(bool won, CancellationToken token)
        {
            int index = LevelIndex;
            busy = true;
            input.TapsLocked = true;
            LevelEnded?.Invoke(won);
            if (won)
            {
                EventBus<LevelWon>.Raise(new LevelWon { LevelIndex = index });
                if (save != null)
                {
                    save.Save(LevelProgressData.Key, new LevelProgressData { levelIndex = index + 1 });
                }
            }
            else
            {
                EventBus<LevelFailed>.Raise(new LevelFailed { LevelIndex = index });
            }

            if (await UniTask.Delay(TimeSpan.FromSeconds(endDelay), cancellationToken: token).SuppressCancellationThrow())
            {
                return;
            }

            ConfirmPopup popup = popups != null ? popups.Get<ConfirmPopup>() : null;
            if (won)
            {
                if (popup != null)
                {
                    await popup.AskAsync($"Level {index + 1} complete!", "Well done", "Next", null, token).SuppressCancellationThrow();
                }
                if (!token.IsCancellationRequested)
                {
                    StartLevel(index + 1);
                }
                return;
            }

            // Revive only helps when the waiting row is what ran out.
            bool canRevive = !Board.HasRevived && Board.Waiting.IsFull;
            bool answer = false;
            if (popup != null)
            {
                string message = !Board.Waiting.IsFull ? "No tray can take the sand that is left."
                    : canRevive ? "The waiting row is full.\nRevive adds 2 waiting slots." : "The waiting row is full.";
                var (canceled, result) = await popup.AskAsync(message, "Out of space",
                    canRevive ? "Revive +2" : "Retry", canRevive ? "Retry" : null, token).SuppressCancellationThrow();
                if (canceled)
                {
                    return;
                }
                answer = result;
            }

            if (canRevive && answer && Revive())
            {
                busy = false;
                input.TapsLocked = false;
                return;
            }
            StartLevel(index);
        }

        private void BuildRows()
        {
            foreach (TrayView tray in trays)
            {
                trayPool.Release(tray);
            }
            trays.Clear();
            foreach (WaitingSlotView slot in slots)
            {
                slotPool.Release(slot);
            }
            slots.Clear();

            for (int i = 0; i < Board.Trays.Length; i++)
            {
                TrayView tray = trayPool.Get();
                tray.transform.localPosition = new Vector3((i - (Board.Trays.Length - 1) * 0.5f) * traySpacing, trayRowY, 0f);
                Tray state = Board.Trays[i];
                tray.Show(state.Color, PaletteColor(state.Color), state.Locked);
                trays.Add(tray);
            }

            for (int i = 0; i < Board.Waiting.Capacity; i++)
            {
                slots.Add(slotPool.Get());
            }
            LayoutWaitingRow();
            RefreshDanger();
        }

        private void LayoutWaitingRow()
        {
            int count = slots.Count;
            float spacing = count > 1 ? Mathf.Min(waitingSpacing, waitingMaxWidth / (count - 1)) : 0f;
            for (int i = 0; i < count; i++)
            {
                slots[i].transform.localPosition = new Vector3((i - (count - 1) * 0.5f) * spacing, waitingRowY, 0f);
            }
        }

        private void RefreshDanger()
        {
            int free = 0;
            for (int i = 0; i < Board.Waiting.Capacity; i++)
            {
                if (Board.Waiting[i] < 0)
                {
                    free++;
                }
            }
            bool danger = free <= 1;
            foreach (WaitingSlotView slot in slots)
            {
                slot.SetDanger(danger);
            }
        }

        private Color PaletteColor(int index)
        {
            return level != null && index >= 0 && index < level.palette.Length ? level.palette[index] : Color.magenta;
        }
    }
}
