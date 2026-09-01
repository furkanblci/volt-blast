using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using BlockBlast.Core;

namespace BlockBlast.Tests
{
    /// <summary>
    /// Spawn selection is the system whose feel is hardest to judge by eye and easiest to
    /// break by tweaking a weight. These tests pin the properties that make it fair:
    /// reproducibility, never dealing an avoidable dead tray, and sizing pieces to how
    /// full the board is.
    /// </summary>
    public class TraySelectorTests
    {
        private const int Single = 0;
        private const int Square2 = 1;
        private const int Line3H = 2;
        private const int Line3V = 3;
        private const int Square3 = 4;

        private static PlacementTable Table(params (int x, int y)[] cells)
        {
            var list = new List<Vector2Int>(cells.Length);
            foreach ((int x, int y) in cells) list.Add(new Vector2Int(x, y));
            return new PlacementTable(list, 8, 8);
        }

        private static PlacementTable Rect(int w, int h)
        {
            var list = new List<Vector2Int>(w * h);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++) list.Add(new Vector2Int(x, y));
            return new PlacementTable(list, 8, 8);
        }

        /// <summary>Small fixed catalog: one cell, 2x2, both 3-lines, and a 3x3.</summary>
        private static List<ShapeCandidate> Catalog() => new List<ShapeCandidate>
        {
            new ShapeCandidate(Single, Rect(1, 1), 1f),
            new ShapeCandidate(Square2, Rect(2, 2), 1f),
            new ShapeCandidate(Line3H, Rect(3, 1), 1f),
            new ShapeCandidate(Line3V, Rect(1, 3), 1f),
            new ShapeCandidate(Square3, Rect(3, 3), 1f)
        };

        private static BoardState Board() => new BoardState(8, 8);

        private static void Fill(BoardState board, Func<int, int, bool> predicate)
        {
            for (int y = 0; y < board.Height; y++)
                for (int x = 0; x < board.Width; x++)
                    if (predicate(x, y)) board.Fill(x, y, 0xFF0000FFu);
        }

        private static List<PlacementTable> Tables(ShapeCandidate[] tray)
        {
            var tables = new List<PlacementTable>(tray.Length);
            foreach (ShapeCandidate shape in tray) tables.Add(shape.Table);
            return tables;
        }

        // ---------- reproducibility ----------

        [Test]
        public void SameSeedProducesTheSameTrays()
        {
            var a = new TraySelector(Catalog(), TraySelectionConfig.Default, 12345u);
            var b = new TraySelector(Catalog(), TraySelectionConfig.Default, 12345u);

            BoardState boardA = Board();
            BoardState boardB = Board();

            for (int turn = 0; turn < 20; turn++)
            {
                ShapeCandidate[] trayA = a.SelectTray(boardA, 3);
                ShapeCandidate[] trayB = b.SelectTray(boardB, 3);

                for (int i = 0; i < 3; i++)
                    Assert.AreEqual(trayA[i].Id, trayB[i].Id, $"Turn {turn} slot {i} diverged.");
            }
        }

        [Test]
        public void DifferentSeedsDiverge()
        {
            var a = new TraySelector(Catalog(), TraySelectionConfig.Default, 1u);
            var b = new TraySelector(Catalog(), TraySelectionConfig.Default, 999u);

            bool anyDifference = false;
            for (int turn = 0; turn < 20 && !anyDifference; turn++)
            {
                ShapeCandidate[] trayA = a.SelectTray(Board(), 3);
                ShapeCandidate[] trayB = b.SelectTray(Board(), 3);

                for (int i = 0; i < 3; i++)
                    if (trayA[i].Id != trayB[i].Id) anyDifference = true;
            }

            Assert.IsTrue(anyDifference, "Two seeds producing identical output means the seed is ignored.");
        }

        // ---------- fairness ----------

        [Test]
        public void NeverDealsADeadTrayWhileAPlayablePieceExists()
        {
            // One free cell in the middle: only the 1x1 fits anywhere.
            for (int trial = 0; trial < 40; trial++)
            {
                BoardState board = Board();
                Fill(board, (x, y) => !(x == 4 && y == 4));

                var selector = new TraySelector(Catalog(), TraySelectionConfig.Default, (uint)(trial + 1));
                ShapeCandidate[] tray = selector.SelectTray(board, 3);

                Assert.IsTrue(BoardRules.HasAnyMove(board, Tables(tray)),
                    $"Trial {trial} dealt a tray with no legal move while the 1x1 was available.");
            }
        }

        [Test]
        public void AGenuinelyFullBoardStillEndsTheRun()
        {
            BoardState board = Board();
            Fill(board, (x, y) => true);

            var selector = new TraySelector(Catalog(), TraySelectionConfig.Default, 7u);
            ShapeCandidate[] tray = selector.SelectTray(board, 3);

            Assert.AreEqual(3, tray.Length, "A tray is still produced; the game-over check decides the outcome.");
            Assert.IsFalse(BoardRules.HasAnyMove(board, Tables(tray)),
                "Selection must not invent a move on a full board.");
        }

        [Test]
        public void CrowdedBoardsGetSmallerPiecesThanEmptyOnes()
        {
            float openAverage = AverageTrayCells(Board(), 42u, 30);

            BoardState crowded = Board();
            // Leave the top two rows free so mid-sized pieces are still legal; the
            // difference must come from the crowding term, not just from what fits.
            Fill(crowded, (x, y) => y < 6);

            float crowdedAverage = AverageTrayCells(crowded, 42u, 30);

            Assert.Less(crowdedAverage, openAverage,
                $"Crowded average {crowdedAverage} should be below open average {openAverage}.");
        }

        private static float AverageTrayCells(BoardState board, uint seed, int trays)
        {
            var selector = new TraySelector(Catalog(), TraySelectionConfig.Default, seed);
            int total = 0;

            for (int i = 0; i < trays; i++)
            {
                foreach (ShapeCandidate shape in selector.SelectTray(board, 3)) total += shape.CellCount;
            }

            return total / (float)(trays * 3);
        }

        [Test]
        public void WeightsBiasTheDraw()
        {
            // Two shapes of identical size so playability and size-fit cannot skew the
            // result, and duplicates unpenalised so only the weight is under test.
            var catalog = new List<ShapeCandidate>
            {
                new ShapeCandidate(Line3H, Rect(3, 1), 5f),
                new ShapeCandidate(Line3V, Rect(1, 3), 1f)
            };

            TraySelectionConfig config = TraySelectionConfig.Default;
            config.duplicatePenalty = 0f;
            config.repeatPenalty = 0f;

            var selector = new TraySelector(catalog, config, 2024u);
            int heavy = 0, light = 0;

            for (int i = 0; i < 200; i++)
            {
                foreach (ShapeCandidate shape in selector.SelectTray(Board(), 3))
                {
                    if (shape.Id == Line3H) heavy++;
                    else light++;
                }
            }

            Assert.Greater(heavy, light * 2,
                $"A 5:1 weight should show clearly; got {heavy} heavy vs {light} light.");
        }

        [Test]
        public void DuplicatesInOneTrayAreAvoidedWhenTheCatalogAllowsIt()
        {
            var selector = new TraySelector(Catalog(), TraySelectionConfig.Default, 555u);
            int traysWithDuplicates = 0;

            for (int i = 0; i < 60; i++)
            {
                ShapeCandidate[] tray = selector.SelectTray(Board(), 3);
                if (tray[0].Id == tray[1].Id || tray[1].Id == tray[2].Id || tray[0].Id == tray[2].Id)
                    traysWithDuplicates++;
            }

            // Uniform draws from five shapes would repeat in roughly half of all trays.
            Assert.Less(traysWithDuplicates, 20,
                $"{traysWithDuplicates}/60 trays repeated a shape; the duplicate penalty is not biting.");
        }

        // ---------- robustness ----------

        [Test]
        public void PiecesAlreadyOnOfferCountTowardsSurvivability()
        {
            BoardState board = Board();
            Fill(board, (x, y) => !(x == 0 && y == 0));

            // The surviving 1x1 keeps the tray alive, so topping up with pieces that do
            // not fit must not be treated as a dead deal.
            var keep = new List<PlacementTable> { Rect(1, 1) };
            var selector = new TraySelector(Catalog(), TraySelectionConfig.Default, 3u);

            Assert.DoesNotThrow(() => selector.SelectTray(board, 2, keep));
        }

        [Test]
        public void ReturnsPromptlyOnAPathologicalBoard()
        {
            // A checkerboard is the worst case for the sequence lookahead: many legal
            // single-cell anchors, almost no legal multi-cell ones.
            BoardState board = Board();
            Fill(board, (x, y) => (x + y) % 2 == 0);

            var selector = new TraySelector(Catalog(), TraySelectionConfig.Default, 11u);
            var watch = System.Diagnostics.Stopwatch.StartNew();

            for (int i = 0; i < 20; i++) selector.SelectTray(board, 3);
            watch.Stop();

            Assert.Less(watch.ElapsedMilliseconds, 2000L,
                $"20 refills took {watch.ElapsedMilliseconds}ms; the node budget is not bounding the search.");
        }

        [Test]
        public void EmptyCatalogIsRejectedAtConstruction()
        {
            Assert.Throws<ArgumentException>(
                () => new TraySelector(new List<ShapeCandidate>(), TraySelectionConfig.Default, 1u));
        }

        [Test]
        public void RequestingZeroPiecesReturnsAnEmptyTray()
        {
            var selector = new TraySelector(Catalog(), TraySelectionConfig.Default, 1u);
            Assert.AreEqual(0, selector.SelectTray(Board(), 0).Length);
        }

        [Test]
        public void ZeroedConfigFallsBackToUsableDefaults()
        {
            TraySelectionConfig sane = new TraySelectionConfig().Sanitized();

            Assert.Greater(sane.candidateSets, 0);
            Assert.Greater(sane.unplayablePenalty, 0f);
            Assert.Greater(sane.sequenceNodeBudget, 0);
        }
    }

    /// <summary>Raw-bitboard helpers the lookahead depends on.</summary>
    public class LookaheadHelperTests
    {
        [Test]
        public void CompletesLine_SeesFullRowsAndColumns()
        {
            ulong row = BoardState.RowMask(2, 8);
            Assert.IsTrue(BoardRules.CompletesLine(row, 8, 8));

            ulong column = BoardState.ColumnMask(5, 8);
            Assert.IsTrue(BoardRules.CompletesLine(column, 8, 8));

            Assert.IsFalse(BoardRules.CompletesLine(row & ~BoardState.BitAt(0, 2), 8, 8));
        }

        [Test]
        public void ClearCompletedLines_RemovesOnlyTheCompletedOnes()
        {
            ulong occupancy = BoardState.RowMask(0, 8) | BoardState.BitAt(3, 4);
            ulong cleared = BoardRules.ClearCompletedLines(occupancy, 8, 8);

            Assert.AreEqual(BoardState.BitAt(3, 4), cleared,
                "The full row goes; the stray cell above it stays.");
        }

        [Test]
        public void ClearCompletedLines_IsANoOpWhenNothingIsComplete()
        {
            ulong occupancy = BoardState.BitAt(1, 1) | BoardState.BitAt(2, 2);
            Assert.AreEqual(occupancy, BoardRules.ClearCompletedLines(occupancy, 8, 8));
        }
    }
}
