using NUnit.Framework;
using UnityEngine;
using BlockBlast.Core;

namespace BlockBlast.Tests
{
    /// <summary>
    /// Easing is shared by every effect in the game, so a curve that does not start at 0,
    /// end at 1, or that blows up outside its domain would corrupt several animations at
    /// once in ways that are hard to trace back from the symptom.
    /// </summary>
    public class EasingTests
    {
        private delegate float Curve(float t);

        private static readonly (string name, Curve curve)[] NormalizedCurves =
        {
            ("OutQuad", Easing.OutQuad),
            ("InQuad", Easing.InQuad),
            ("OutCubic", Easing.OutCubic),
            ("InOutQuad", Easing.InOutQuad),
            ("OutBack", t => Easing.OutBack(t)),
            ("OutElastic", Easing.OutElastic)
        };

        [Test]
        public void EveryCurveStartsAtZeroAndEndsAtOne()
        {
            foreach ((string name, Curve curve) in NormalizedCurves)
            {
                Assert.AreEqual(0f, curve(0f), 1e-4f, $"{name} must start at 0.");
                Assert.AreEqual(1f, curve(1f), 1e-4f, $"{name} must end at 1.");
            }
        }

        [Test]
        public void InputIsClampedSoOvershootingCallersCannotProduceWildValues()
        {
            foreach ((string name, Curve curve) in NormalizedCurves)
            {
                Assert.AreEqual(0f, curve(-5f), 1e-4f, $"{name} must clamp below 0.");
                Assert.AreEqual(1f, curve(5f), 1e-4f, $"{name} must clamp above 1.");
            }
        }

        [Test]
        public void EveryCurveStaysFinite()
        {
            foreach ((string name, Curve curve) in NormalizedCurves)
            {
                for (int i = 0; i <= 100; i++)
                {
                    float value = curve(i / 100f);
                    Assert.IsFalse(float.IsNaN(value) || float.IsInfinity(value),
                        $"{name} produced {value} at t={i / 100f}.");
                }
            }
        }

        [Test]
        public void MonotonicCurvesNeverGoBackwards()
        {
            Curve[] monotonic = { Easing.OutQuad, Easing.InQuad, Easing.OutCubic, Easing.InOutQuad };

            foreach (Curve curve in monotonic)
            {
                float previous = curve(0f);
                for (int i = 1; i <= 100; i++)
                {
                    float value = curve(i / 100f);
                    Assert.GreaterOrEqual(value, previous - 1e-5f, $"Curve dipped at t={i / 100f}.");
                    previous = value;
                }
            }
        }

        [Test]
        public void OutBackActuallyOvershoots()
        {
            float peak = 0f;
            for (int i = 0; i <= 100; i++) peak = Mathf.Max(peak, Easing.OutBack(i / 100f));

            Assert.Greater(peak, 1.05f, "OutBack that never passes 1 gives no sense of weight.");
        }

        [Test]
        public void LargerOvershootPushesFurtherPastOne()
        {
            float gentle = 0f, strong = 0f;
            for (int i = 0; i <= 100; i++)
            {
                gentle = Mathf.Max(gentle, Easing.OutBack(i / 100f, 1f));
                strong = Mathf.Max(strong, Easing.OutBack(i / 100f, 4f));
            }

            Assert.Greater(strong, gentle);
        }

        [Test]
        public void PulseReturnsToZeroAtBothEnds()
        {
            Assert.AreEqual(0f, Easing.Pulse(0f), 1e-4f);
            Assert.AreEqual(0f, Easing.Pulse(1f), 1e-4f);
        }

        [Test]
        public void PulsePeaksAtOneWhereItIsToldTo()
        {
            Assert.AreEqual(1f, Easing.Pulse(0.35f, 0.35f), 1e-4f);
            Assert.AreEqual(1f, Easing.Pulse(0.7f, 0.7f), 1e-4f);
        }

        [Test]
        public void PulseNeverExceedsOne()
        {
            for (int i = 0; i <= 200; i++)
            {
                float value = Easing.Pulse(i / 200f);
                Assert.LessOrEqual(value, 1.0001f, $"Pulse exceeded 1 at t={i / 200f}.");
                Assert.GreaterOrEqual(value, -0.0001f);
            }
        }

        [Test]
        public void DegeneratePulsePeaksAreClampedRatherThanDividingByZero()
        {
            Assert.IsFalse(float.IsNaN(Easing.Pulse(0.5f, 0f)));
            Assert.IsFalse(float.IsNaN(Easing.Pulse(0.5f, 1f)));
        }
    }

    /// <summary>
    /// The ghost preview's promise -- "this drop clears these lines" -- has to be exactly
    /// what the turn then does, or it teaches the player something false.
    /// </summary>
    public class ClearPreviewTests
    {
        private static PlacementTable Single() =>
            new PlacementTable(new[] { new Vector2Int(0, 0) }, 8, 8);

        [Test]
        public void PreviewMatchesWhatThePlacementActuallyClears()
        {
            var board = new BoardState(8, 8);
            for (int x = 0; x < 7; x++) board.Fill(x, 0, 0xFF0000FFu);

            PlacementTable piece = Single();
            ulong mask = piece.MaskAt(7, 0);

            LineClearResult predicted = BoardRules.FindCompletedLines(
                board.Occupancy | mask, board.Width, board.Height);

            BoardRules.TryPlace(board, piece, 7, 0, 0xFF0000FFu);
            LineClearResult actual = BoardRules.FindCompletedLines(board);

            Assert.AreEqual(predicted.ClearedMask, actual.ClearedMask);
            Assert.AreEqual(predicted.LineCount, actual.LineCount);
        }

        [Test]
        public void PreviewIsEmptyForADropThatCompletesNothing()
        {
            var board = new BoardState(8, 8);
            for (int x = 0; x < 5; x++) board.Fill(x, 0, 0xFF0000FFu);

            LineClearResult predicted = BoardRules.FindCompletedLines(
                board.Occupancy | Single().MaskAt(6, 0), board.Width, board.Height);

            Assert.IsFalse(predicted.Any);
            Assert.AreEqual(0, predicted.LineCount);
        }

        [Test]
        public void PreviewSeesADoubleWhenARowAndColumnCompleteTogether()
        {
            var board = new BoardState(8, 8);
            for (int x = 1; x < 8; x++) board.Fill(x, 0, 0xFF0000FFu);
            for (int y = 1; y < 8; y++) board.Fill(0, y, 0xFF0000FFu);

            // The corner completes both the bottom row and the left column at once.
            LineClearResult predicted = BoardRules.FindCompletedLines(
                board.Occupancy | Single().MaskAt(0, 0), board.Width, board.Height);

            Assert.AreEqual(2, predicted.LineCount);
            Assert.AreEqual(1, predicted.RowCount);
            Assert.AreEqual(1, predicted.ColumnCount);
            Assert.AreEqual(15, predicted.CellCount);
        }

        [Test]
        public void PreviewDoesNotMutateTheBoard()
        {
            var board = new BoardState(8, 8);
            for (int x = 0; x < 7; x++) board.Fill(x, 0, 0xFF0000FFu);
            ulong before = board.Occupancy;

            BoardRules.FindCompletedLines(board.Occupancy | Single().MaskAt(7, 0), board.Width, board.Height);

            Assert.AreEqual(before, board.Occupancy, "Previewing must never touch the real board.");
        }
    }
}
