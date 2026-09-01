using NUnit.Framework;
using UnityEngine;
using BlockBlast.Core;

namespace BlockBlast.Tests
{
    /// <summary>
    /// Guards the two things a player feels most directly: that a piece lands where it
    /// looks like it will, and that the score reflects what they actually pulled off.
    /// </summary>
    public class GridGeometryTests
    {
        private static GridGeometry Geometry() =>
            new GridGeometry(8, 8, 1f, 0.1f, Vector2.zero);

        [Test]
        public void CellToWorld_AndBack_RoundTripsForEveryCell()
        {
            GridGeometry g = Geometry();

            for (int y = 0; y < 8; y++)
            {
                for (int x = 0; x < 8; x++)
                {
                    Vector2Int roundTripped = g.WorldToNearestCell(g.CellToWorld(x, y));
                    Assert.AreEqual(new Vector2Int(x, y), roundTripped,
                        $"Cell ({x},{y}) must survive a world-space round trip.");
                }
            }
        }

        [Test]
        public void WorldToNearestCell_SnapsToTheCellAPieceVisuallyCovers()
        {
            GridGeometry g = Geometry();
            Vector3 centre = g.CellToWorld(3, 4);

            // Just under half a pitch in any direction must still resolve to the same cell.
            float nudge = g.Pitch * 0.45f;
            Assert.AreEqual(new Vector2Int(3, 4), g.WorldToNearestCell(centre + new Vector3(nudge, 0f, 0f)));
            Assert.AreEqual(new Vector2Int(3, 4), g.WorldToNearestCell(centre + new Vector3(-nudge, 0f, 0f)));
            Assert.AreEqual(new Vector2Int(3, 4), g.WorldToNearestCell(centre + new Vector3(0f, nudge, 0f)));

            // Past the halfway point it belongs to the neighbour.
            Assert.AreEqual(new Vector2Int(4, 4),
                g.WorldToNearestCell(centre + new Vector3(g.Pitch * 0.55f, 0f, 0f)));
        }

        [Test]
        public void ShapeCellOffset_UsesPitchSoAPieceOverlaysTheGridExactly()
        {
            GridGeometry g = Geometry();

            // A piece anchored at (2,2) must have its cell (1,0) land exactly on grid (3,2).
            Vector3 anchorWorld = g.CellToWorld(2, 2);
            Vector3 secondCellWorld = anchorWorld + g.ShapeCellOffset(new Vector2Int(1, 0));

            Assert.That(Vector3.Distance(secondCellWorld, g.CellToWorld(3, 2)), Is.LessThan(1e-4f),
                "Laying piece cells out on CellSize instead of Pitch makes pieces narrower than the grid.");
        }

        [Test]
        public void WorldToContainingCell_ReportsOffBoardPointsAsMinusOne()
        {
            GridGeometry g = Geometry();
            Assert.AreEqual(new Vector2Int(-1, -1), g.WorldToContainingCell(new Vector3(100f, 100f, 0f)));
            Assert.AreEqual(new Vector2Int(0, 0), g.WorldToContainingCell(g.CellToWorld(0, 0)));
        }

        [Test]
        public void OffsetCenter_ShiftsTheWholeBoardWithoutBreakingConversions()
        {
            var shifted = new GridGeometry(8, 8, 1f, 0.1f, new Vector2(3f, -2.5f));

            Assert.AreEqual(new Vector2Int(6, 1), shifted.WorldToNearestCell(shifted.CellToWorld(6, 1)));
            Assert.That(shifted.Bounds.center.x, Is.EqualTo(3f).Within(1e-4f));
        }
    }

    public class ScoreRulesTests
    {
        private static ScoringConfig Config() => ScoringConfig.Default;

        [Test]
        public void PlacementPays_PerCell()
        {
            Assert.AreEqual(5 * Config().pointsPerPlacedCell, ScoreRules.PlacementPoints(5, Config()));
        }

        [Test]
        public void MultiLineClears_GrowFasterThanLinearly()
        {
            ScoringConfig config = Config();
            int one = ScoreRules.ClearPoints(1, 1, config);
            int two = ScoreRules.ClearPoints(2, 1, config);
            int three = ScoreRules.ClearPoints(3, 1, config);

            Assert.Greater(two, one * 2, "A double must be worth more than two singles, or setups never pay off.");
            Assert.Greater(three, one * 3);
        }

        [Test]
        public void Combo_AdvancesOnAClearAndResetsWhenAPlacementClearsNothing()
        {
            ScoringConfig config = Config();
            int combo = 0;

            ScoreRules.ScoreTurn(4, 1, ref combo, config);
            Assert.AreEqual(1, combo);

            ScoreRules.ScoreTurn(4, 2, ref combo, config);
            Assert.AreEqual(2, combo);

            ScoreRules.ScoreTurn(4, 0, ref combo, config);
            Assert.AreEqual(0, combo, "A turn that clears nothing must break the streak.");
        }

        [Test]
        public void ComboMultiplier_IsCappedSoALongChainCannotRunAway()
        {
            ScoringConfig config = Config();
            Assert.AreEqual(1f, ScoreRules.ComboMultiplier(1, config), 1e-4f);
            Assert.AreEqual(config.maxComboMultiplier, ScoreRules.ComboMultiplier(500, config), 1e-4f);
        }

        [Test]
        public void TurnWithNoClear_StillPaysForThePlacement()
        {
            int combo = 3;
            TurnScore turn = ScoreRules.ScoreTurn(4, 0, ref combo, Config());

            Assert.AreEqual(0, turn.ClearPoints);
            Assert.Greater(turn.PlacementPoints, 0);
            Assert.AreEqual(turn.PlacementPoints, turn.Total);
        }

        [Test]
        public void ZeroedConfig_FallsBackToDefaultsInsteadOfScoringNothing()
        {
            var empty = new ScoringConfig();
            ScoringConfig sane = empty.Sanitized();

            Assert.AreEqual(ScoringConfig.Default.pointsPerPlacedCell, sane.pointsPerPlacedCell);
            Assert.AreEqual(ScoringConfig.Default.pointsPerLine, sane.pointsPerLine);
            Assert.GreaterOrEqual(sane.maxComboMultiplier, 1f);
        }
    }

    public class GameSaveTests
    {
        [Test]
        public void BoardSurvivesAJsonRoundTripWithColoursIntact()
        {
            var board = new BoardState(8, 8);
            uint red = ColorPacking.Pack(Color.red);
            uint blue = ColorPacking.Pack(Color.blue);

            board.Fill(0, 0, red);
            board.Fill(7, 7, blue);
            board.Fill(3, 5, red);

            GameSave save = GameSave.Capture(board, 1234, 3, new[] { "Block_L" }, new[] { 1 });
            var revived = JsonUtility.FromJson<GameSave>(JsonUtility.ToJson(save));

            var restored = new BoardState(8, 8);
            Assert.IsTrue(revived.TryApplyTo(restored));

            Assert.AreEqual(board.Occupancy, restored.Occupancy);
            Assert.AreEqual(red, restored.GetColor(0, 0));
            Assert.AreEqual(blue, restored.GetColor(7, 7));
            Assert.AreEqual(ColorPacking.Empty, restored.GetColor(1, 1));
            Assert.AreEqual(1234, revived.score);
            Assert.AreEqual(3, revived.combo);
            Assert.AreEqual("Block_L", revived.trayShapeIds[0]);
        }

        [Test]
        public void SaveFromADifferentBoardSizeIsRejectedRatherThanPartiallyApplied()
        {
            var small = new BoardState(6, 6);
            small.Fill(0, 0, ColorPacking.Pack(Color.green));

            GameSave save = GameSave.Capture(small, 10, 0, new string[0], new int[0]);

            var full = new BoardState(8, 8);
            Assert.IsFalse(save.TryApplyTo(full), "Resuming into a different board size must fail loudly.");
            Assert.AreEqual(0, full.FilledCount);
        }

        [Test]
        public void PackedColourIsNeverZeroForAVisibleCell()
        {
            // Zero doubles as "empty", so a fully transparent authored colour must not collide with it.
            Assert.AreNotEqual(ColorPacking.Empty, ColorPacking.Pack(new Color(0f, 0f, 0f, 0f)));
        }

        [Test]
        public void UnpackReversesPack()
        {
            var original = new Color32(200, 100, 50, 255);
            Color roundTripped = ColorPacking.Unpack(ColorPacking.Pack(original));
            Color32 back = roundTripped;

            Assert.AreEqual(original.r, back.r);
            Assert.AreEqual(original.g, back.g);
            Assert.AreEqual(original.b, back.b);
            Assert.AreEqual(original.a, back.a);
        }
    }
}
