using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using BlockBlast.Core;

namespace BlockBlast.Tests
{
    /// <summary>
    /// Rules coverage. These run without a scene, which is the point of keeping the
    /// board logic free of MonoBehaviour: the cases below used to be reachable only by
    /// playing the game until they happened to occur.
    /// </summary>
    public class BoardRulesTests
    {
        private static readonly List<Vector2Int> Single = new List<Vector2Int> { new Vector2Int(0, 0) };

        private static readonly List<Vector2Int> Horizontal3 = new List<Vector2Int>
        {
            new Vector2Int(0, 0), new Vector2Int(1, 0), new Vector2Int(2, 0)
        };

        private static readonly List<Vector2Int> Square2 = new List<Vector2Int>
        {
            new Vector2Int(0, 0), new Vector2Int(1, 0), new Vector2Int(0, 1), new Vector2Int(1, 1)
        };

        private static BoardState NewBoard() => new BoardState(8, 8);

        private static PlacementTable Table(List<Vector2Int> cells) => new PlacementTable(cells, 8, 8);

        private static void FillRow(BoardState board, int y, int fromX, int toX)
        {
            for (int x = fromX; x <= toX; x++) board.Fill(x, y, 0xFF0000FFu);
        }

        // ---------- placement ----------

        [Test]
        public void Place_MarksExactlyTheShapeCells()
        {
            BoardState board = NewBoard();
            Assert.IsTrue(BoardRules.TryPlace(board, Table(Horizontal3), 2, 3, 0xFF0000FFu));

            Assert.AreEqual(3, board.FilledCount);
            Assert.IsTrue(board.IsOccupied(2, 3));
            Assert.IsTrue(board.IsOccupied(3, 3));
            Assert.IsTrue(board.IsOccupied(4, 3));
            Assert.IsTrue(board.IsEmpty(5, 3), "The cell past the end must stay empty.");
        }

        [Test]
        public void Place_RejectsAnchorThatWouldHangOffTheEdge()
        {
            BoardState board = NewBoard();

            // A 3-wide piece anchored at x=6 needs x=6,7,8; 8 is off-board.
            Assert.IsFalse(BoardRules.CanPlace(board, Table(Horizontal3), 6, 0));
            Assert.IsTrue(BoardRules.CanPlace(board, Table(Horizontal3), 5, 0));
        }

        [Test]
        public void Place_FailingLeavesTheBoardUntouched()
        {
            BoardState board = NewBoard();
            BoardRules.TryPlace(board, Table(Single), 4, 4, 0xFF0000FFu);
            ulong before = board.Occupancy;

            Assert.IsFalse(BoardRules.TryPlace(board, Table(Square2), 3, 3, 0x00FF00FFu),
                "The 2x2 overlaps the occupied cell and must be rejected.");
            Assert.AreEqual(before, board.Occupancy, "A rejected placement must not partially apply.");
        }

        [Test]
        public void Shape_IsNormalizedSoNegativeAuthoringStillAnchorsAtTheCorner()
        {
            var offCentre = new List<Vector2Int>
            {
                new Vector2Int(-1, -1), new Vector2Int(0, -1), new Vector2Int(-1, 0)
            };

            PlacementTable table = Table(offCentre);
            Assert.AreEqual(2, table.Width);
            Assert.AreEqual(2, table.Height);

            BoardState board = NewBoard();
            Assert.IsTrue(BoardRules.TryPlace(board, table, 0, 0, 0xFF0000FFu),
                "Normalization must let the piece sit flush in the corner.");
            Assert.IsTrue(board.IsOccupied(0, 0) && board.IsOccupied(1, 0) && board.IsOccupied(0, 1));
        }

        [Test]
        public void Shape_DuplicateCellsAreCollapsed()
        {
            var duplicated = new List<Vector2Int>
            {
                new Vector2Int(0, 0), new Vector2Int(0, 0), new Vector2Int(1, 0)
            };

            Assert.AreEqual(2, Table(duplicated).CellCount);
        }

        // ---------- line clears ----------

        [Test]
        public void CompletedRow_IsDetectedAndCleared()
        {
            BoardState board = NewBoard();
            FillRow(board, 0, 0, 7);

            LineClearResult result = BoardRules.FindCompletedLines(board);
            Assert.AreEqual(1, result.RowCount);
            Assert.AreEqual(0, result.ColumnCount);
            Assert.AreEqual(8, result.CellCount);

            BoardRules.ApplyClear(board, result);
            Assert.AreEqual(0, board.FilledCount);
        }

        [Test]
        public void FindCompletedLines_DoesNotMutateUntilApplied()
        {
            BoardState board = NewBoard();
            FillRow(board, 0, 0, 7);

            LineClearResult result = BoardRules.FindCompletedLines(board);
            Assert.AreEqual(8, board.FilledCount,
                "Detection must be side-effect free so a clear can be animated before it commits.");

            BoardRules.ApplyClear(board, result);
            Assert.AreEqual(0, board.FilledCount);
        }

        [Test]
        public void IntersectingRowAndColumn_CountsTwoLinesButClearsTheSharedCellOnce()
        {
            BoardState board = NewBoard();
            for (int x = 0; x < 8; x++) board.Fill(x, 0, 0xFF0000FFu);
            for (int y = 1; y < 8; y++) board.Fill(0, y, 0xFF0000FFu);

            LineClearResult result = BoardRules.FindCompletedLines(board);
            Assert.AreEqual(1, result.RowCount);
            Assert.AreEqual(1, result.ColumnCount);
            Assert.AreEqual(2, result.LineCount);

            // 8 + 8 cells sharing the corner: 15 distinct, not 16.
            Assert.AreEqual(15, result.CellCount);
        }

        [Test]
        public void PartialRow_IsNotCleared()
        {
            BoardState board = NewBoard();
            FillRow(board, 0, 0, 6);

            Assert.IsFalse(BoardRules.FindCompletedLines(board).Any);
        }

        // ---------- game over ----------

        [Test]
        public void FullBoard_LeavesNoMoveForAnyPiece()
        {
            BoardState board = NewBoard();
            for (int y = 0; y < 8; y++) FillRow(board, y, 0, 7);

            var tray = new List<PlacementTable> { Table(Single), Table(Horizontal3), Table(Square2) };
            Assert.IsFalse(BoardRules.HasAnyMove(board, tray));
        }

        [Test]
        public void SingleGap_KeepsTheRunAliveForAOneCellPieceOnly()
        {
            BoardState board = NewBoard();
            for (int y = 0; y < 8; y++) FillRow(board, y, 0, 7);
            board.Vacate(4, 4);

            Assert.IsTrue(BoardRules.HasAnyPlacement(board, Table(Single)));
            Assert.IsFalse(BoardRules.HasAnyPlacement(board, Table(Square2)));

            Assert.IsTrue(BoardRules.HasAnyMove(board,
                new List<PlacementTable> { Table(Square2), Table(Single) }),
                "One playable piece anywhere in the tray keeps the run going.");
        }

        [Test]
        public void EmptyTray_IsNotAMove()
        {
            Assert.IsFalse(BoardRules.HasAnyMove(NewBoard(), new List<PlacementTable>()),
                "An empty tray reports no move; the caller must refill before evaluating game over.");
        }

        [Test]
        public void NullTrayEntry_IsIgnoredRatherThanThrowing()
        {
            var tray = new List<PlacementTable> { null, Table(Single) };
            Assert.IsTrue(BoardRules.HasAnyMove(NewBoard(), tray));
        }

        [Test]
        public void ClearingALine_ReopensSpaceForAPieceThatDidNotFit()
        {
            BoardState board = NewBoard();
            for (int y = 0; y < 8; y++) FillRow(board, y, 0, 7);
            for (int x = 0; x < 8; x++) board.Vacate(x, 0);
            FillRow(board, 0, 0, 6);

            PlacementTable square = Table(Square2);
            Assert.IsFalse(BoardRules.HasAnyPlacement(board, square));

            // Drop the last single cell to complete row 0, then clear it.
            Assert.IsTrue(BoardRules.TryPlace(board, Table(Single), 7, 0, 0xFF0000FFu));
            BoardRules.ApplyClear(board, BoardRules.FindCompletedLines(board));

            Assert.IsTrue(BoardRules.HasAnyPlacement(board, square),
                "Scoring must see the completed board, but the move check must see the cleared one.");
        }

        // ---------- bit helpers ----------

        [Test]
        public void PopCountAndTrailingZeroCount_AgreeWithTheMasksTheyDescribe()
        {
            Assert.AreEqual(64, BoardState.PopCount(ulong.MaxValue));
            Assert.AreEqual(0, BoardState.PopCount(0UL));
            Assert.AreEqual(8, BoardState.PopCount(BoardState.RowMask(3, 8)));
            Assert.AreEqual(8, BoardState.PopCount(BoardState.ColumnMask(5, 8)));

            Assert.AreEqual(BoardState.BitIndex(5, 0), BoardState.TrailingZeroCount(BoardState.ColumnMask(5, 8)));
            Assert.AreEqual(BoardState.BitIndex(0, 3), BoardState.TrailingZeroCount(BoardState.RowMask(3, 8)));
        }

        [Test]
        public void ColourSurvivesPlacementAndIsDroppedOnClear()
        {
            BoardState board = NewBoard();
            uint packed = ColorPacking.Pack(new Color32(12, 34, 56, 255));

            BoardRules.TryPlace(board, Table(Single), 2, 2, packed);
            Assert.AreEqual(packed, board.GetColor(2, 2));

            board.Vacate(2, 2);
            Assert.AreEqual(ColorPacking.Empty, board.GetColor(2, 2));
        }
    }
}
