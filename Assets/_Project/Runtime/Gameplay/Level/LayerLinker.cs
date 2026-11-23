using System.Collections.Generic;
using GameKit.Gameplay.Voxel;
using UnityEngine;

namespace GameKit.Gameplay.Level
{
    /// <summary>
    /// Finds top-level pieces that sit inside another top-level piece without being linked to it (e.g. a 95% copy
    /// inside a shell, named "Show_1_0.95_*"). Both then show on the surface in interleaved patches. Linking the inner one
    /// as a layer of the shell makes the shell look whole and the inner piece appear once the shell is collected.
    /// A piece counts as inside when most of its cells see the shell before anything else in at least 4 of the 6 axis
    /// directions (shells are often open on one side) and only a small part of it reaches the surface, so a button lying
    /// on a body (sees it on 1–2 sides) or an inlay flush with the surface (mostly surface) is never hidden. Also a
    /// piece covered all round by several pieces (≥ 5 sides, e.g. a ring of cake slices inside a cover) goes under the
    /// most-touched piece that is not itself covered (the cover).
    /// </summary>
    public static class LayerLinker
    {
        public struct Link
        {
            public int Child;
            public int Parent;
            /// <summary>Share of the child's cells that see the parent on enough sides.</summary>
            public float Inside;
        }

        private static readonly Vector3Int[] Directions =
        {
            new Vector3Int(1, 0, 0), new Vector3Int(-1, 0, 0), new Vector3Int(0, 1, 0),
            new Vector3Int(0, -1, 0), new Vector3Int(0, 0, 1), new Vector3Int(0, 0, -1)
        };

        /// <summary>
        /// Links to add: each child is a top-level piece (with its layers) that reaches the surface and lies inside the
        /// top-level piece it touches most. <paramref name="parents"/> is not changed.
        /// </summary>
        /// <param name="canCover">
        /// Which pieces may take in a piece covered all round by several pieces; null = that rule is off. It cannot
        /// tell a piece showing through a hole by mistake from a thick inlay meant to be seen (tiles in a lattice, a
        /// window), so it is only used per level by hand (Link Covered Pieces In Selected Levels).
        /// </param>
        public static List<Link> FindEnclosed(VoxelGrid grid, IReadOnlyList<int> parents, float minInsideShare = 0.6f,
            int minSides = 4, float maxSurfaceShare = 0.25f, System.Func<int, bool> canCover = null)
        {
            int count = parents.Count;
            var root = new int[count];
            for (int b = 0; b < count; b++)
            {
                int r = b;
                for (int guard = 0; parents[r] >= 0 && guard < count; guard++)
                {
                    r = parents[r];
                }
                root[b] = r;
            }

            // Per root: cells, how many reach the air, and which other root its cells touch most.
            var cells = new List<int>[count];
            var surface = new int[count];
            var touch = new Dictionary<int, int>[count];
            for (int i = 0; i < grid.CellCount; i++)
            {
                short owner = grid[i];
                if (owner < 0 || owner >= count)
                {
                    continue;
                }
                int r = root[owner];
                (cells[r] ??= new List<int>()).Add(i);
                Vector3Int c = grid.Cell(i);
                bool onSurface = false;
                foreach (Vector3Int d in Directions)
                {
                    short other = grid[c.x + d.x, c.y + d.y, c.z + d.z]; // Empty outside the grid.
                    if (other < 0)
                    {
                        onSurface = true;
                    }
                    else if (other < count && root[other] != r)
                    {
                        touch[r] ??= new Dictionary<int, int>();
                        touch[r].TryGetValue(root[other], out int n);
                        touch[r][root[other]] = n + 1;
                    }
                }
                if (onSurface)
                {
                    surface[r]++;
                }
            }

            // Pass 1: per candidate, how much of it the most-touched piece covers, and how much any piece covers.
            var candidates = new List<int>();
            var mostTouched = new int[count];
            var shellShare = new float[count];
            var enclosed = new bool[count];
            for (int b = 0; b < count; b++)
            {
                mostTouched[b] = -1;
                if (root[b] != b || cells[b] == null || surface[b] == 0 || touch[b] == null)
                {
                    continue; // Only top-level pieces that show on the surface and touch another piece.
                }
                if (surface[b] > maxSurfaceShare * cells[b].Count)
                {
                    continue; // Mostly on the surface (a skin, an inlay): it is meant to be seen, not a hidden copy.
                }
                int shell = MostTouched(touch[b], null);
                if (shell < 0 || cells[shell] == null)
                {
                    continue;
                }

                int seeShell = 0, covered = 0;
                foreach (int i in cells[b])
                {
                    Sides(grid, root, count, i, b, shell, out int shellSides, out int anySides);
                    if (shellSides >= minSides)
                    {
                        seeShell++;
                    }
                    if (anySides >= minSides + 1)
                    {
                        covered++;
                    }
                }
                candidates.Add(b);
                mostTouched[b] = shell;
                shellShare[b] = seeShell / (float)cells[b].Count;
                enclosed[b] = covered >= minInsideShare * cells[b].Count;
            }

            // Pass 2: inside the piece it touches most (a copy in a shell), or covered all round by several pieces (a
            // ring of pieces inside a cover): then it goes under the most-touched piece that is not itself covered.
            // Pieces that cannot be the cover: covered all round, or not on the surface at all.
            var hidden = new bool[count];
            for (int b = 0; b < count; b++)
            {
                hidden[b] = enclosed[b] || (root[b] == b && cells[b] != null && surface[b] == 0);
            }

            var links = new List<Link>();
            foreach (int b in candidates)
            {
                if (shellShare[b] >= minInsideShare)
                {
                    links.Add(new Link { Child = b, Parent = mostTouched[b], Inside = shellShare[b] });
                }
                else if (canCover != null && enclosed[b])
                {
                    int cover = MostTouched(touch[b], hidden);
                    if (cover >= 0 && cells[cover] != null && canCover(cover))
                    {
                        links.Add(new Link { Child = b, Parent = cover, Inside = minInsideShare });
                    }
                }
            }

            // Never make a cycle (two pieces each "inside" the other): keep the more inside one.
            links.Sort((x, y) => y.Inside.CompareTo(x.Inside));
            var parentOf = new Dictionary<int, int>();
            var kept = new List<Link>();
            foreach (Link link in links)
            {
                if (!LeadsTo(parentOf, link.Parent, link.Child))
                {
                    parentOf[link.Child] = link.Parent;
                    kept.Add(link);
                }
            }
            return kept;
        }

        private static int MostTouched(Dictionary<int, int> touch, bool[] skip)
        {
            int result = -1, best = 0;
            foreach (KeyValuePair<int, int> t in touch)
            {
                if (t.Value > best && (skip == null || !skip[t.Key]))
                {
                    result = t.Key;
                    best = t.Value;
                }
            }
            return result;
        }

        /// <summary>
        /// Of the 6 axis directions from cell <paramref name="index"/>: how many reach <paramref name="shell"/> first, and
        /// how many reach any other piece (not the air or the grid edge) first.
        /// </summary>
        private static void Sides(VoxelGrid grid, int[] root, int count, int index, int self, int shell, out int shellSides,
            out int anySides)
        {
            Vector3Int start = grid.Cell(index);
            shellSides = 0;
            anySides = 0;
            foreach (Vector3Int d in Directions)
            {
                Vector3Int p = start + d;
                while (grid.InBounds(p.x, p.y, p.z))
                {
                    short owner = grid[p.x, p.y, p.z];
                    int r = owner >= 0 && owner < count ? root[owner] : -1;
                    if (r != self)
                    {
                        if (r == shell)
                        {
                            shellSides++;
                        }
                        if (r >= 0)
                        {
                            anySides++;
                        }
                        break;
                    }
                    p += d;
                }
            }
        }

        private static bool LeadsTo(Dictionary<int, int> parentOf, int from, int target)
        {
            for (int guard = 0; guard < 4096 && from >= 0; guard++)
            {
                if (from == target)
                {
                    return true;
                }
                if (!parentOf.TryGetValue(from, out from))
                {
                    return false;
                }
            }
            return false;
        }
    }
}
