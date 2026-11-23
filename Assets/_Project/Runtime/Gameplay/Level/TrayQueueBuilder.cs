using System.Collections.Generic;
using GameKit.Gameplay.Board;

namespace GameKit.Gameplay.Level
{
    /// <summary>
    /// Builds a tray colour queue together with a winning play: it plays the board with the real rules (open trays,
    /// waiting row, waiting sand moving into a matching tray) and picks the colour of each new tray at the moment a
    /// tray frees up: the colour with the most sand waiting, then the most blocks ready to tap. Trays then follow the
    /// colours the model reveals instead of every tray of one colour first.
    /// </summary>
    public static class TrayQueueBuilder
    {
        public sealed class Result
        {
            public readonly List<int> Queue = new List<int>();
            /// <summary>Block order of the play the queue was built from.</summary>
            public readonly List<int> Order = new List<int>();
            /// <summary>Most waiting slots used at once by that play; above the slot count the level needs a revive.</summary>
            public int PeakWaiting;
        }

        public static List<int> Build(IReadOnlyList<int> colors, IReadOnlyList<int> parents) => BuildWithPlay(colors, parents).Queue;

        /// <param name="colors">Colour of each block (negative = ignored).</param>
        /// <param name="parents">Parent of each block (-1 = none); a block can be tapped once its parent is taken.</param>
        public static Result BuildWithPlay(IReadOnlyList<int> colors, IReadOnlyList<int> parents,
            int openTrays = SandBoard.DefaultOpenTrays)
        {
            var sim = new Sim(colors, parents, openTrays);
            sim.Run();
            return sim.Result;
        }

        private sealed class Sim
        {
            private readonly IReadOnlyList<int> colors;
            private readonly IReadOnlyList<int> parents;
            private readonly bool[] taken;
            private readonly int[] trayColor;
            private readonly int[] trayCount;
            private readonly List<int> waiting = new List<int>();
            /// <summary>Blocks of each colour not in a tray yet (on the model or waiting).</summary>
            private readonly Dictionary<int, int> owed = new Dictionary<int, int>();
            private readonly List<int>[] children;
            public readonly Result Result = new Result();

            public Sim(IReadOnlyList<int> colors, IReadOnlyList<int> parents, int openTrays)
            {
                this.colors = colors;
                this.parents = parents;
                int count = colors.Count;
                taken = new bool[count];
                children = new List<int>[count];
                for (int i = 0; i < count; i++)
                {
                    children[i] = new List<int>();
                }
                for (int i = 0; i < count; i++)
                {
                    if (colors[i] < 0)
                    {
                        taken[i] = true;
                        continue;
                    }
                    owed.TryGetValue(colors[i], out int n);
                    owed[colors[i]] = n + 1;
                    int p = parents[i];
                    if (p >= 0 && p < count && p != i)
                    {
                        children[p].Add(i);
                    }
                }
                trayColor = new int[System.Math.Max(1, openTrays)];
                trayCount = new int[trayColor.Length];
                for (int t = 0; t < trayColor.Length; t++)
                {
                    trayColor[t] = -1;
                }
            }

            public void Run()
            {
                for (int t = 0; t < trayColor.Length; t++)
                {
                    OpenTray(t);
                }

                while (true)
                {
                    int block = PickForTray();
                    if (block < 0)
                    {
                        block = PickForWaiting();
                    }
                    if (block < 0)
                    {
                        break; // Model is empty (or only parent loops are left).
                    }
                    Take(block);
                }
            }

            private void Take(int block)
            {
                taken[block] = true;
                Result.Order.Add(block);
                int color = colors[block];
                int tray = TrayFor(color);
                if (tray >= 0)
                {
                    Fill(tray);
                }
                else
                {
                    waiting.Add(color);
                    Result.PeakWaiting = System.Math.Max(Result.PeakWaiting, waiting.Count);
                }
            }

            private void Fill(int tray)
            {
                owed[trayColor[tray]]--;
                trayCount[tray]++;
                if (trayCount[tray] >= TrayRow.SlotsPerTray)
                {
                    OpenTray(tray);
                }
            }

            /// <summary>Gives the tray the next colour (and appends it to the queue), then pulls waiting sand into it.</summary>
            private void OpenTray(int tray)
            {
                trayCount[tray] = 0;
                trayColor[tray] = ChooseColor();
                if (trayColor[tray] < 0)
                {
                    return;
                }
                Result.Queue.Add(trayColor[tray]);

                int color = trayColor[tray];
                for (int w = 0; w < waiting.Count && trayColor[tray] == color && trayCount[tray] < TrayRow.SlotsPerTray;)
                {
                    if (waiting[w] == color)
                    {
                        waiting.RemoveAt(w);
                        Fill(tray); // May complete the tray and open the next one.
                    }
                    else
                    {
                        w++;
                    }
                }
            }

            /// <summary>Colour still owed that is not already being filled: most waiting first, then most ready to tap.</summary>
            private int ChooseColor()
            {
                int best = -1;
                long bestScore = long.MinValue;
                foreach (var pair in owed)
                {
                    int c = pair.Key;
                    int stillFree = pair.Value - OpenSlotsFor(c);
                    if (stillFree <= 0)
                    {
                        continue;
                    }
                    long score = (long)CountWaiting(c) * 100000 + CountReady(c) * 100 + stillFree;
                    if (score > bestScore || (score == bestScore && c < best))
                    {
                        bestScore = score;
                        best = c;
                    }
                }
                return best;
            }

            /// <summary>Ready block that goes straight into an open tray, preferring one that frees blocks for the trays.</summary>
            private int PickForTray()
            {
                int best = -1, bestScore = -1;
                for (int i = 0; i < taken.Length; i++)
                {
                    if (!IsReady(i) || TrayFor(colors[i]) < 0)
                    {
                        continue;
                    }
                    int score = OpensForTrays(i);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = i;
                    }
                }
                return best;
            }

            /// <summary>
            /// No ready block fits a tray: send one to the waiting row, the one that frees the most blocks for the open
            /// trays, then one whose colour is already waiting (so the next tray of that colour fills at once).
            /// </summary>
            private int PickForWaiting()
            {
                int best = -1;
                long bestScore = long.MinValue;
                for (int i = 0; i < taken.Length; i++)
                {
                    if (!IsReady(i))
                    {
                        continue;
                    }
                    long score = (long)OpensForTrays(i) * 1000 + CountWaiting(colors[i]) * 10 + children[i].Count;
                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = i;
                    }
                }
                return best;
            }

            private int OpensForTrays(int block)
            {
                int n = 0;
                foreach (int child in children[block])
                {
                    if (!taken[child] && TrayFor(colors[child]) >= 0)
                    {
                        n++;
                    }
                }
                return n;
            }

            private int TrayFor(int color)
            {
                for (int t = 0; t < trayColor.Length; t++)
                {
                    if (trayColor[t] == color && trayCount[t] < TrayRow.SlotsPerTray)
                    {
                        return t;
                    }
                }
                return -1;
            }

            private int OpenSlotsFor(int color)
            {
                int n = 0;
                for (int t = 0; t < trayColor.Length; t++)
                {
                    if (trayColor[t] == color)
                    {
                        n += TrayRow.SlotsPerTray - trayCount[t];
                    }
                }
                return n;
            }

            private int CountWaiting(int color)
            {
                int n = 0;
                foreach (int c in waiting)
                {
                    if (c == color)
                    {
                        n++;
                    }
                }
                return n;
            }

            private int CountReady(int color)
            {
                int n = 0;
                for (int i = 0; i < taken.Length; i++)
                {
                    if (colors[i] == color && IsReady(i))
                    {
                        n++;
                    }
                }
                return n;
            }

            private bool IsReady(int i)
            {
                if (taken[i])
                {
                    return false;
                }
                int parent = parents[i];
                return parent < 0 || parent >= taken.Length || parent == i || taken[parent];
            }
        }
    }
}
