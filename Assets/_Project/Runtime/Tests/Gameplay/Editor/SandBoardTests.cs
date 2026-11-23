using System.Collections.Generic;
using System.Linq;
using GameKit.Gameplay.Board;
using NUnit.Framework;

namespace GameKit.Tests.Gameplay
{
    public class SandBoardTests
    {
        private const int Red = 0;
        private const int Blue = 1;
        private const int Green = 2;

        private static BoardBlock B(int color, int parent = -1) => new BoardBlock(color, parent);

        private static List<BoardStepKind> Kinds(IReadOnlyList<BoardStep> steps) => steps.Select(s => s.Kind).ToList();

        [Test]
        public void NewBoard_OpensTwoTraysFromQueue_TwoLocked()
        {
            var board = new SandBoard(new[] { B(Red) }, new[] { Red, Blue, Green });

            Assert.AreEqual(Red, board.Trays[0].Color);
            Assert.AreEqual(Blue, board.Trays[1].Color);
            Assert.AreEqual(4, board.Trays.Length);
            Assert.IsTrue(board.Trays[2].Locked);
            Assert.IsTrue(board.Trays[3].Locked);
            Assert.AreEqual(5, board.Waiting.Capacity);
        }

        [Test]
        public void Collect_MatchingTray_FillsTray()
        {
            var board = new SandBoard(new[] { B(Red), B(Red) }, new[] { Red, Blue });

            var steps = board.Collect(0);

            Assert.AreEqual(BoardStepKind.BlockToTray, steps[0].Kind);
            Assert.AreEqual(0, steps[0].Tray);
            Assert.AreEqual(1, board.Trays[0].Count);
        }

        [Test]
        public void Collect_ThreeSameColour_TrayCompletesAndTakesNextColour()
        {
            var board = new SandBoard(new[] { B(Red), B(Red), B(Red), B(Green) }, new[] { Red, Blue, Green });
            board.Collect(0);
            board.Collect(1);

            var steps = board.Collect(2);

            CollectionAssert.AreEqual(
                new[] { BoardStepKind.BlockToTray, BoardStepKind.TrayCompleted, BoardStepKind.TrayAssigned },
                Kinds(steps));
            Assert.AreEqual(Green, board.Trays[0].Color);
            Assert.AreEqual(0, board.Trays[0].Count);
        }

        [Test]
        public void Collect_NoMatchingTray_GoesToWaiting()
        {
            var board = new SandBoard(new[] { B(Green), B(Red) }, new[] { Red, Blue });

            var steps = board.Collect(0);

            Assert.AreEqual(BoardStepKind.BlockToWaiting, steps[0].Kind);
            Assert.AreEqual(0, steps[0].Slot);
            Assert.AreEqual(Green, board.Waiting[0]);
            Assert.AreEqual(BoardState.Playing, board.State);
        }

        [Test]
        public void TrayReplaced_WaitingSandOfNewColourMovesIn()
        {
            var blocks = new[] { B(Green), B(Red), B(Red), B(Red), B(Green), B(Green) };
            var board = new SandBoard(blocks, new[] { Red, Blue, Green });
            board.Collect(0); // green → waiting
            board.Collect(1);
            board.Collect(2);

            var steps = board.Collect(3); // red tray done → becomes green → waiting green moves in

            Assert.Contains(BoardStepKind.WaitingToTray, Kinds(steps));
            Assert.IsTrue(board.Waiting.IsEmpty);
            Assert.AreEqual(1, board.Trays[0].Count);
        }

        [Test]
        public void InnerBlock_CannotTapUntilOuterCollected()
        {
            var board = new SandBoard(new[] { B(Red), B(Blue, parent: 0) }, new[] { Red, Blue });

            Assert.IsFalse(board.CanTap(1));
            Assert.IsEmpty(board.Collect(1));

            board.Collect(0);

            Assert.IsTrue(board.CanTap(1));
        }

        [Test]
        public void WaitingFull_NoMove_Fails()
        {
            var blocks = Enumerable.Range(0, 6).Select(_ => B(Green)).ToArray();
            var board = new SandBoard(blocks, new[] { Red, Blue });

            for (int i = 0; i < 4; i++)
            {
                board.Collect(i);
            }
            Assert.AreEqual(BoardState.Playing, board.State);

            var steps = board.Collect(4);

            Assert.AreEqual(BoardStepKind.Failed, steps[steps.Count - 1].Kind);
            Assert.AreEqual(BoardState.Failed, board.State);
            Assert.IsFalse(board.CanTap(5));
        }

        [Test]
        public void Revive_AddsTwoSlots_OnlyOnce()
        {
            var blocks = Enumerable.Range(0, 7).Select(_ => B(Green)).ToArray();
            var board = new SandBoard(blocks, new[] { Red, Blue });
            for (int i = 0; i < 5; i++)
            {
                board.Collect(i);
            }

            Assert.IsTrue(board.Revive());
            Assert.AreEqual(BoardState.Playing, board.State);
            Assert.AreEqual(7, board.Waiting.Capacity);
            Assert.IsTrue(board.CanTap(5));

            board.Collect(5);
            board.Collect(6);
            Assert.AreEqual(BoardState.Failed, board.State);
            Assert.IsFalse(board.Revive());
        }

        [Test]
        public void UnlockTray_TakesNextColour_AndPullsFromWaiting()
        {
            var board = new SandBoard(new[] { B(Green), B(Green), B(Green) }, new[] { Red, Blue, Green });
            board.Collect(0);

            var steps = board.UnlockTray();

            Assert.AreEqual(BoardStepKind.TrayAssigned, steps[0].Kind);
            Assert.AreEqual(2, steps[0].Tray);
            Assert.AreEqual(Green, board.Trays[2].Color);
            Assert.Contains(BoardStepKind.WaitingToTray, Kinds(steps));
            Assert.AreEqual(1, board.Trays[2].Count);
            board.UnlockTray(); // fourth tray
            Assert.IsFalse(board.Trays[3].Locked);
            Assert.IsEmpty(board.UnlockTray());
        }

        [Test]
        public void AllCollected_WaitingEmpty_Wins()
        {
            var board = new SandBoard(new[] { B(Red), B(Red), B(Red) }, new[] { Red });

            board.Collect(0);
            board.Collect(1);
            var steps = board.Collect(2);

            Assert.AreEqual(BoardStepKind.Won, steps[steps.Count - 1].Kind);
            Assert.AreEqual(BoardState.Won, board.State);
        }

        [Test]
        public void AllCollected_SandLeftWithLockedTrayAvailable_KeepsPlaying()
        {
            var board = new SandBoard(new[] { B(Green) }, new[] { Red, Blue, Green });

            board.Collect(0);

            Assert.AreEqual(BoardState.Playing, board.State);
            board.UnlockTray();
            Assert.AreEqual(1, board.Trays[2].Count);
        }

        [Test]
        public void AllCollected_SandLeftNoTrayPossible_Fails()
        {
            var board = new SandBoard(new[] { B(Green) }, new[] { Red, Blue }, lockedTrays: 0);

            board.Collect(0);

            Assert.AreEqual(BoardState.Failed, board.State);
        }
    }
}
