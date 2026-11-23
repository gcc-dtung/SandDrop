using System;
using System.Collections.Generic;

namespace GameKit.Gameplay.Board
{
    public enum BoardState
    {
        Playing,
        Won,
        Failed
    }

    public enum BoardStepKind
    {
        /// <summary>Block sand flies into a tray. Tray, Slot = slot inside the tray.</summary>
        BlockToTray,
        /// <summary>Block sand goes to a waiting slot. Slot = waiting index.</summary>
        BlockToWaiting,
        /// <summary>Waiting sand moves to a tray. Slot = waiting index it left.</summary>
        WaitingToTray,
        /// <summary>Tray is full and disappears.</summary>
        TrayCompleted,
        /// <summary>Tray position gets a new colour (Color -1 = no more trays here).</summary>
        TrayAssigned,
        Won,
        Failed
    }

    /// <summary>One thing that happened, in order, so visuals can replay it.</summary>
    public readonly struct BoardStep
    {
        public readonly BoardStepKind Kind;
        public readonly int Block;
        public readonly int Tray;
        public readonly int Slot;
        public readonly int Color;

        public BoardStep(BoardStepKind kind, int block = -1, int tray = -1, int slot = -1, int color = -1)
        {
            Kind = kind;
            Block = block;
            Tray = tray;
            Slot = slot;
            Color = color;
        }

        public override string ToString() => $"{Kind} b{Block} t{Tray} s{Slot} c{Color}";
    }

    /// <summary>Block definition for the board: colour and the block that encloses it (-1 = none).</summary>
    public readonly struct BoardBlock
    {
        public readonly int Color;
        public readonly int Parent;

        public BoardBlock(int color, int parent = -1)
        {
            Color = color;
            Parent = parent;
        }
    }

    /// <summary>
    /// Core rules (SandSort_Reference.md §1): tap a block → its sand goes to a matching open tray (3 slots),
    /// otherwise to the waiting row; waiting sand moves to trays when one matches; full waiting row with
    /// no possible move = fail; every block collected and waiting row empty = win.
    /// </summary>
    public sealed class SandBoard
    {
        public const int DefaultOpenTrays = 2;
        public const int DefaultLockedTrays = 2;
        public const int DefaultWaitingSlots = 5;
        public const int ReviveExtraSlots = 2;

        private readonly BoardBlock[] blocks;
        private readonly bool[] collected;
        private readonly List<BoardStep> steps = new List<BoardStep>();
        private int collectedCount;

        public SandBoard(IReadOnlyList<BoardBlock> blocks, IReadOnlyList<int> trayColorQueue,
            int openTrays = DefaultOpenTrays, int lockedTrays = DefaultLockedTrays, int waitingSlots = DefaultWaitingSlots)
        {
            if (blocks == null)
            {
                throw new ArgumentNullException(nameof(blocks));
            }

            this.blocks = new BoardBlock[blocks.Count];
            for (int i = 0; i < blocks.Count; i++)
            {
                this.blocks[i] = blocks[i];
            }

            collected = new bool[blocks.Count];
            Trays = new TrayRow(openTrays, lockedTrays, trayColorQueue);
            Waiting = new WaitingRow(waitingSlots);
        }

        public TrayRow Trays { get; }
        public WaitingRow Waiting { get; }
        public BoardState State { get; private set; } = BoardState.Playing;
        public bool HasRevived { get; private set; }
        public int BlockCount => blocks.Length;

        public BoardBlock GetBlock(int index) => blocks[index];
        public bool IsCollected(int index) => collected[index];

        /// <summary>A block can be tapped when it is not collected and the block around it is gone.</summary>
        public bool CanTap(int index)
        {
            if (State != BoardState.Playing || index < 0 || index >= blocks.Length || collected[index])
            {
                return false;
            }

            int parent = blocks[index].Parent;
            return parent < 0 || collected[parent];
        }

        /// <summary>Collects the block. Returns the steps (empty when the tap is not allowed).</summary>
        public IReadOnlyList<BoardStep> Collect(int index)
        {
            steps.Clear();
            if (!CanTap(index))
            {
                return steps;
            }

            collected[index] = true;
            collectedCount++;
            int color = blocks[index].Color;

            int tray = Trays.FindTrayFor(color);
            if (tray >= 0)
            {
                steps.Add(new BoardStep(BoardStepKind.BlockToTray, index, tray, Trays[tray].Count, color));
                FillTray(tray);
            }
            else
            {
                int slot = Waiting.Put(color);
                if (slot < 0)
                {
                    // Only reachable when the fail check was skipped; treat as fail.
                    Fail();
                    return steps;
                }
                steps.Add(new BoardStep(BoardStepKind.BlockToWaiting, index, slot: slot, color: color));
            }

            ResolveWaiting();
            CheckEnd();
            return steps;
        }

        /// <summary>Opens the first locked tray (the "+" button). Returns the steps (empty when none is locked).</summary>
        public IReadOnlyList<BoardStep> UnlockTray()
        {
            steps.Clear();
            int index = Trays.FirstLocked();
            if (State != BoardState.Playing || index < 0)
            {
                return steps;
            }

            int color = Trays.Unlock(index);
            steps.Add(new BoardStep(BoardStepKind.TrayAssigned, tray: index, color: color));
            ResolveWaiting();
            CheckEnd();
            return steps;
        }

        /// <summary>Revive once after failing: +2 waiting slots and play on.</summary>
        public bool Revive()
        {
            if (State != BoardState.Failed || HasRevived)
            {
                return false;
            }

            HasRevived = true;
            Waiting.AddSlots(ReviveExtraSlots);
            State = BoardState.Playing;
            return true;
        }

        /// <summary>Cheat / debug: ends the level.</summary>
        public void ForceState(BoardState state)
        {
            State = state;
        }

        private void FillTray(int tray)
        {
            if (!Trays.Fill(tray))
            {
                return;
            }

            steps.Add(new BoardStep(BoardStepKind.TrayCompleted, tray: tray, color: Trays[tray].Color));
            int newColor = Trays.Replace(tray);
            steps.Add(new BoardStep(BoardStepKind.TrayAssigned, tray: tray, color: newColor));
        }

        private void ResolveWaiting()
        {
            bool moved = true;
            while (moved)
            {
                moved = false;
                for (int slot = 0; slot < Waiting.Capacity; slot++)
                {
                    int color = Waiting[slot];
                    if (color < 0)
                    {
                        continue;
                    }

                    int tray = Trays.FindTrayFor(color);
                    if (tray < 0)
                    {
                        continue;
                    }

                    Waiting.Clear(slot);
                    steps.Add(new BoardStep(BoardStepKind.WaitingToTray, tray: tray, slot: slot, color: color));
                    FillTray(tray);
                    moved = true;
                }
            }
        }

        private void CheckEnd()
        {
            if (collectedCount == blocks.Length)
            {
                if (Waiting.IsEmpty)
                {
                    State = BoardState.Won;
                    steps.Add(new BoardStep(BoardStepKind.Won));
                }
                else if (Trays.FirstLocked() < 0 || Trays.RemainingQueue == 0)
                {
                    // Sand left and no locked tray could ever take it.
                    Fail();
                }
                return;
            }

            if (Waiting.IsFull)
            {
                Fail();
            }
        }

        private void Fail()
        {
            State = BoardState.Failed;
            steps.Add(new BoardStep(BoardStepKind.Failed));
        }
    }
}
