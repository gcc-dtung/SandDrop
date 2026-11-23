using System.Collections.Generic;
using System.Text;
using GameKit.Gameplay.Board;
using GameKit.Gameplay.Voxel;

namespace GameKit.Gameplay.Level
{
    /// <summary>Checks a level's data before it ships. Returns readable error lines (empty = valid).</summary>
    public static class LevelValidator
    {
        public static List<string> Validate(SandLevelData level)
        {
            var errors = new List<string>();
            if (level == null)
            {
                errors.Add("Level is null.");
                return errors;
            }

            int paletteCount = level.palette?.Length ?? 0;
            if (paletteCount == 0)
            {
                errors.Add("Palette is empty.");
            }
            if (level.blocks.Count == 0)
            {
                errors.Add("Level has no blocks.");
            }

            errors.AddRange(ValidateStructure(level.ToBoardBlocks(), level.trayColorQueue, paletteCount));

            if (errors.Count == 0)
            {
                VoxelGrid grid = level.BuildGrid();
                var cells = grid.CellsByBlock(level.blocks.Count);
                for (int i = 0; i < cells.Length; i++)
                {
                    if (cells[i].Count == 0)
                    {
                        errors.Add($"Block {i} ({level.blocks[i].name}) has no grains (outside the grid or fully covered by inner blocks).");
                    }
                }
            }
            return errors;
        }

        /// <summary>Rules that do not need the grid: colours, parents, tray counts.</summary>
        public static List<string> ValidateStructure(IReadOnlyList<BoardBlock> blocks, IReadOnlyList<int> trayQueue, int paletteCount)
        {
            var errors = new List<string>();
            var blockCountByColor = new Dictionary<int, int>();
            var trayCountByColor = new Dictionary<int, int>();

            for (int i = 0; i < blocks.Count; i++)
            {
                BoardBlock b = blocks[i];
                if (b.Color < 0 || b.Color >= paletteCount)
                {
                    errors.Add($"Block {i}: colour {b.Color} is not in the palette.");
                }
                if (b.Parent == i || b.Parent >= blocks.Count || b.Parent < -1)
                {
                    errors.Add($"Block {i}: invalid parent {b.Parent}.");
                }
                else if (HasCycle(blocks, i))
                {
                    errors.Add($"Block {i}: parent chain loops.");
                }
                blockCountByColor[b.Color] = blockCountByColor.TryGetValue(b.Color, out int n) ? n + 1 : 1;
            }

            foreach (int color in trayQueue)
            {
                trayCountByColor[color] = trayCountByColor.TryGetValue(color, out int n) ? n + 1 : 1;
            }

            foreach (var pair in blockCountByColor)
            {
                if (pair.Value % TrayRow.SlotsPerTray != 0)
                {
                    errors.Add($"Colour {pair.Key}: {pair.Value} blocks, must be a multiple of {TrayRow.SlotsPerTray}.");
                }
                trayCountByColor.TryGetValue(pair.Key, out int trays);
                if (trays * TrayRow.SlotsPerTray != pair.Value)
                {
                    errors.Add($"Colour {pair.Key}: {pair.Value} blocks but {trays} trays in the queue.");
                }
            }

            foreach (var pair in trayCountByColor)
            {
                if (!blockCountByColor.ContainsKey(pair.Key))
                {
                    errors.Add($"Colour {pair.Key}: in the tray queue but no block uses it.");
                }
            }
            return errors;
        }

        private static bool HasCycle(IReadOnlyList<BoardBlock> blocks, int start)
        {
            int p = blocks[start].Parent;
            for (int steps = 0; p >= 0 && p < blocks.Count; steps++)
            {
                if (p == start || steps > blocks.Count)
                {
                    return true;
                }
                p = blocks[p].Parent;
            }
            return false;
        }
    }

    /// <summary>
    /// Searches for a tap order that wins using only the nesting rule (ignores which side of the model is visible).
    /// Used by the editor check; the locked third tray is not counted.
    /// </summary>
    public static class LevelSolver
    {
        public enum Result
        {
            Solvable,
            Unsolvable,
            GaveUp
        }

        public static Result Solve(IReadOnlyList<BoardBlock> blocks, IReadOnlyList<int> trayQueue, out List<int> order,
            int lockedTrays = 0, int maxNodes = 200000)
        {
            order = new List<int>();
            var seen = new HashSet<string>();
            int nodes = 0;
            bool gaveUp = false;
            bool found = Search(blocks, trayQueue, lockedTrays, order, seen, ref nodes, maxNodes, ref gaveUp);
            if (found)
            {
                return Result.Solvable;
            }
            order.Clear();
            return gaveUp ? Result.GaveUp : Result.Unsolvable;
        }

        private static bool Search(IReadOnlyList<BoardBlock> blocks, IReadOnlyList<int> queue, int lockedTrays,
            List<int> path, HashSet<string> seen, ref int nodes, int maxNodes, ref bool gaveUp)
        {
            if (++nodes > maxNodes)
            {
                gaveUp = true;
                return false;
            }

            SandBoard board = Replay(blocks, queue, lockedTrays, path);
            if (board.State == BoardState.Won)
            {
                return true;
            }
            if (board.State == BoardState.Failed || !seen.Add(Key(board)))
            {
                return false;
            }

            for (int i = 0; i < blocks.Count; i++)
            {
                if (!board.CanTap(i))
                {
                    continue;
                }
                path.Add(i);
                if (Search(blocks, queue, lockedTrays, path, seen, ref nodes, maxNodes, ref gaveUp))
                {
                    return true;
                }
                path.RemoveAt(path.Count - 1);
                if (gaveUp)
                {
                    return false;
                }
            }
            return false;
        }

        private static SandBoard Replay(IReadOnlyList<BoardBlock> blocks, IReadOnlyList<int> queue, int lockedTrays, List<int> path)
        {
            var board = new SandBoard(blocks, queue, SandBoard.DefaultOpenTrays, lockedTrays);
            foreach (int i in path)
            {
                board.Collect(i);
            }
            return board;
        }

        private static string Key(SandBoard board)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < board.BlockCount; i++)
            {
                sb.Append(board.IsCollected(i) ? '1' : '0');
            }
            sb.Append('|');
            for (int t = 0; t < board.Trays.Length; t++)
            {
                sb.Append(board.Trays[t].Color).Append(':').Append(board.Trays[t].Count).Append(',');
            }
            sb.Append('|');
            var waiting = new List<int>();
            for (int s = 0; s < board.Waiting.Capacity; s++)
            {
                if (board.Waiting[s] >= 0)
                {
                    waiting.Add(board.Waiting[s]);
                }
            }
            waiting.Sort();
            foreach (int c in waiting)
            {
                sb.Append(c).Append(',');
            }
            return sb.ToString();
        }
    }
}
