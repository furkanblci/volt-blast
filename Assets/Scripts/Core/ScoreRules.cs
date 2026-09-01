using System;
using UnityEngine;

namespace BlockBlast.Core
{
    /// <summary>Tunable scoring numbers. Serializable so they stay editable in the Inspector.</summary>
    [Serializable]
    public struct ScoringConfig
    {
        [Tooltip("Points for each cell of a placed piece.")]
        public int pointsPerPlacedCell;

        [Tooltip("Base points for the first line of a clear.")]
        public int pointsPerLine;

        [Tooltip("Extra multiplier per additional combo step, e.g. 0.5 means combo 3 pays 2x.")]
        public float comboStep;

        [Tooltip("Ceiling on the combo multiplier so long runs cannot blow the score up.")]
        public float maxComboMultiplier;

        public static ScoringConfig Default => new ScoringConfig
        {
            pointsPerPlacedCell = 1,
            pointsPerLine = 10,
            comboStep = 0.5f,
            maxComboMultiplier = 8f
        };

        /// <summary>Replaces zeroed-out Inspector values with the defaults.</summary>
        public ScoringConfig Sanitized()
        {
            ScoringConfig fallback = Default;
            return new ScoringConfig
            {
                pointsPerPlacedCell = pointsPerPlacedCell > 0 ? pointsPerPlacedCell : fallback.pointsPerPlacedCell,
                pointsPerLine = pointsPerLine > 0 ? pointsPerLine : fallback.pointsPerLine,
                comboStep = comboStep > 0f ? comboStep : fallback.comboStep,
                maxComboMultiplier = maxComboMultiplier >= 1f ? maxComboMultiplier : fallback.maxComboMultiplier
            };
        }
    }

    /// <summary>What one completed turn was worth.</summary>
    public readonly struct TurnScore
    {
        public int PlacementPoints { get; }
        public int ClearPoints { get; }
        public int Combo { get; }
        public float ComboMultiplier { get; }

        public TurnScore(int placementPoints, int clearPoints, int combo, float comboMultiplier)
        {
            PlacementPoints = placementPoints;
            ClearPoints = clearPoints;
            Combo = combo;
            ComboMultiplier = comboMultiplier;
        }

        public int Total => PlacementPoints + ClearPoints;
    }

    /// <summary>
    /// Scoring maths, kept pure so the numbers can be tuned and tested without pressing Play.
    ///
    /// Two deliberate changes from the previous formula. Multi-line clears are
    /// triangular rather than linear (a 3-line clear pays 6x a single, not 3x), which is
    /// what makes setting up a double or triple worth the risk. And the combo is a
    /// bounded multiplier instead of raw <c>base * combo</c>, which previously grew
    /// without limit and let one lucky chain dwarf the rest of a run.
    /// </summary>
    public static class ScoreRules
    {
        public static int PlacementPoints(int cellCount, ScoringConfig config) =>
            Mathf.Max(0, cellCount) * config.pointsPerPlacedCell;

        /// <summary>Combo multiplier for a given streak. Streak 0 or 1 pays 1x.</summary>
        public static float ComboMultiplier(int combo, ScoringConfig config) =>
            Mathf.Min(1f + Mathf.Max(0, combo - 1) * config.comboStep, config.maxComboMultiplier);

        /// <summary>
        /// Value of the lines cleared this turn. <paramref name="combo"/> is the streak
        /// including this turn, so the first clear of a run passes 1.
        /// </summary>
        public static int ClearPoints(int lineCount, int combo, ScoringConfig config)
        {
            if (lineCount <= 0) return 0;

            // Triangular growth: 1, 3, 6, 10 ... lines are worth progressively more together.
            int baseValue = lineCount * (lineCount + 1) / 2 * config.pointsPerLine;
            return Mathf.RoundToInt(baseValue * ComboMultiplier(combo, config));
        }

        /// <summary>
        /// Scores one turn and advances the combo. The streak grows on any clear and
        /// resets the moment a placement clears nothing.
        /// </summary>
        public static TurnScore ScoreTurn(int placedCellCount, int lineCount, ref int combo, ScoringConfig config)
        {
            config = config.Sanitized();
            combo = lineCount > 0 ? combo + 1 : 0;

            return new TurnScore(
                PlacementPoints(placedCellCount, config),
                ClearPoints(lineCount, combo, config),
                combo,
                ComboMultiplier(combo, config));
        }
    }
}
