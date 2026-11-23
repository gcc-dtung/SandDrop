using System.Collections.Generic;
using System.Linq;
using GameKit.Gameplay.Board;
using GameKit.Gameplay.Level;
using NUnit.Framework;

namespace GameKit.Tests.Gameplay
{
    public class TrayQueueBuilderTests
    {
        [Test]
        public void Build_OneTrayPerThreeBlocksOfEachColour()
        {
            int[] colors = { 0, 0, 0, 0, 0, 0, 1, 1, 1 };
            int[] parents = Enumerable.Repeat(-1, colors.Length).ToArray();

            List<int> queue = TrayQueueBuilder.Build(colors, parents);

            Assert.AreEqual(2, queue.Count(c => c == 0));
            Assert.AreEqual(1, queue.Count(c => c == 1));
        }

        [Test]
        public void Build_OuterColourWithInnerSetsDoesNotTakeEveryTrayFirst()
        {
            // Six onion stacks with red (0) outside and red again deep inside; blue (1) and green (2) in between.
            // Queuing every red tray first would leave the inner reds stuck behind blue / green with no tray for them.
            var colors = new List<int>();
            var parents = new List<int>();
            for (int stack = 0; stack < 6; stack++)
            {
                int outer = colors.Count;
                colors.Add(0); parents.Add(-1);
                colors.Add(1 + stack % 2); parents.Add(outer);
                colors.Add(0); parents.Add(outer + 1);
            }

            TrayQueueBuilder.Result result = TrayQueueBuilder.BuildWithPlay(colors, parents);

            Assert.AreNotEqual(new[] { 0, 0, 0, 0 }, result.Queue.Take(4).ToArray());
            AssertPlayWins(colors, parents, result);
        }

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void BuildWithPlay_PlayWinsOnTheBoard(int seed)
        {
            // Random layered model: stacks of 1–5 layers, 5 colours, 9 blocks of each colour.
            var rng = new System.Random(seed);
            var pool = Enumerable.Range(0, 45).Select(i => i % 5).OrderBy(_ => rng.Next()).ToList();
            var colors = new List<int>();
            var parents = new List<int>();
            while (colors.Count < pool.Count)
            {
                int depth = rng.Next(1, 6), previous = -1;
                for (int layer = 0; layer < depth && colors.Count < pool.Count; layer++)
                {
                    colors.Add(pool[colors.Count]);
                    parents.Add(previous);
                    previous = colors.Count - 1;
                }
            }

            TrayQueueBuilder.Result result = TrayQueueBuilder.BuildWithPlay(colors, parents);

            Assert.LessOrEqual(result.PeakWaiting, SandBoard.DefaultWaitingSlots);
            AssertPlayWins(colors, parents, result);
        }

        private static void AssertPlayWins(List<int> colors, List<int> parents, TrayQueueBuilder.Result result)
        {
            var blocks = colors.Select((c, i) => new BoardBlock(c, parents[i])).ToArray();
            var board = new SandBoard(blocks, result.Queue, SandBoard.DefaultOpenTrays, 0);
            Assert.AreEqual(colors.Count, result.Order.Count);
            foreach (int block in result.Order)
            {
                Assert.IsTrue(board.CanTap(block), $"Block {block} cannot be tapped (state {board.State}).");
                board.Collect(block);
            }
            Assert.AreEqual(BoardState.Won, board.State);
        }
    }
}
