using System;
using System.Collections.Generic;

namespace GameKit.Gameplay.Board
{
    /// <summary>One tray position. Color is -1 when the position is empty (queue ran out).</summary>
    public struct Tray
    {
        public int Color;
        public int Count;
        public bool Locked;

        public bool IsOpen => !Locked && Color >= 0;
    }

    /// <summary>Fixed tray positions; each takes its colour from a shared queue when it opens or completes.</summary>
    public sealed class TrayRow
    {
        public const int SlotsPerTray = 3;

        private readonly Tray[] trays;
        private readonly int[] colorQueue;
        private int queueIndex;

        public TrayRow(int openTrays, int lockedTrays, IReadOnlyList<int> colorQueue)
        {
            if (openTrays < 0 || lockedTrays < 0 || openTrays + lockedTrays == 0)
            {
                throw new ArgumentException("Need at least one tray.");
            }

            this.colorQueue = new int[colorQueue?.Count ?? 0];
            for (int i = 0; i < this.colorQueue.Length; i++)
            {
                this.colorQueue[i] = colorQueue[i];
            }

            trays = new Tray[openTrays + lockedTrays];
            for (int i = 0; i < trays.Length; i++)
            {
                trays[i] = new Tray { Color = -1, Locked = i >= openTrays };
                if (!trays[i].Locked)
                {
                    trays[i].Color = NextColor();
                }
            }
        }

        public int Length => trays.Length;
        public int RemainingQueue => colorQueue.Length - queueIndex;

        public Tray this[int index] => trays[index];

        /// <summary>First open tray of this colour that still has room, or -1.</summary>
        public int FindTrayFor(int color)
        {
            for (int i = 0; i < trays.Length; i++)
            {
                if (trays[i].IsOpen && trays[i].Color == color && trays[i].Count < SlotsPerTray)
                {
                    return i;
                }
            }
            return -1;
        }

        /// <summary>Adds one block to the tray and returns true when the tray became full.</summary>
        public bool Fill(int index)
        {
            trays[index].Count++;
            return trays[index].Count >= SlotsPerTray;
        }

        /// <summary>Replaces a full tray with the next colour from the queue (or empties it). Returns the new colour.</summary>
        public int Replace(int index)
        {
            trays[index].Count = 0;
            trays[index].Color = NextColor();
            return trays[index].Color;
        }

        /// <summary>First locked tray index, or -1.</summary>
        public int FirstLocked()
        {
            for (int i = 0; i < trays.Length; i++)
            {
                if (trays[i].Locked)
                {
                    return i;
                }
            }
            return -1;
        }

        /// <summary>Unlocks the tray and gives it the next colour. Returns the new colour.</summary>
        public int Unlock(int index)
        {
            trays[index].Locked = false;
            trays[index].Count = 0;
            trays[index].Color = NextColor();
            return trays[index].Color;
        }

        private int NextColor()
        {
            return queueIndex < colorQueue.Length ? colorQueue[queueIndex++] : -1;
        }
    }
}
