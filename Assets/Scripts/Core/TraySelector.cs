using System;
using System.Collections.Generic;
using UnityEngine;

namespace BlockBlast.Core
{
    /// <summary>One shape the selector may draw, with the weight that sets how often it shows up.</summary>
    public sealed class ShapeCandidate
    {
        public int Id { get; }
        public PlacementTable Table { get; }
        public float Weight { get; }

        public ShapeCandidate(int id, PlacementTable table, float weight)
        {
            Id = id;
            Table = table ?? throw new ArgumentNullException(nameof(table));
            Weight = Mathf.Max(0.0001f, weight);
        }

        public int CellCount => Table.CellCount;
    }

    /// <summary>Tuning for how trays are chosen. Serializable so it stays editable in the Inspector.</summary>
    [Serializable]
    public struct TraySelectionConfig
    {
        [Tooltip("How many candidate trays to sample and score before picking one. " +
                 "Higher is fairer and slightly slower; this runs once per refill, not per frame.")]
        public int candidateSets;

        [Tooltip("How hard piece sizes shrink as the board fills. 0 ignores crowding entirely.")]
        public float crowdingBias;

        [Tooltip("Weight of the piece-size-versus-crowding term.")]
        public float sizeFitWeight;

        [Tooltip("Penalty per repeated shape inside one tray.")]
        public float duplicatePenalty;

        [Tooltip("Penalty for a shape drawn again within the recent window.")]
        public float repeatPenalty;

        [Tooltip("How many recently drawn shapes to remember for the repeat penalty.")]
        public int recentWindow;

        [Tooltip("Penalty for a tray with no legal move at all. Should dwarf every other term.")]
        public float unplayablePenalty;

        [Tooltip("Reward per piece in the tray that has a legal anchor.")]
        public float playableBonus;

        [Tooltip("Reward for a tray whose three pieces can all be placed in some order.")]
        public float sequenceBonus;

        [Tooltip("Reward for a tray containing a piece that can complete a line. " +
                 "Only applied once the board is crowded, so early play stays challenging.")]
        public float clearOpportunityBonus;

        [Tooltip("Board fill fraction above which the clear-opportunity reward switches on.")]
        public float clearOpportunityThreshold;

        [Tooltip("Cap on lookahead work per candidate tray, so a crowded board cannot stall a refill.")]
        public int sequenceNodeBudget;

        [Tooltip("How decisively the best-scoring candidate wins. Low is near-deterministic and " +
                 "flattens the authored weights; high is near-random and stops filtering bad trays.")]
        public float selectionTemperature;

        [Tooltip("Target piece size on an empty board, as a multiple of the catalog's weighted average.")]
        public float openSizeFactor;

        [Tooltip("Target piece size on a full board, as a multiple of the catalog's weighted average.")]
        public float crowdedSizeFactor;

        public static TraySelectionConfig Default => new TraySelectionConfig
        {
            candidateSets = 12,
            crowdingBias = 1.4f,
            sizeFitWeight = 3f,
            duplicatePenalty = 2.5f,
            repeatPenalty = 1.2f,
            recentWindow = 4,
            unplayablePenalty = 1000f,
            playableBonus = 6f,
            sequenceBonus = 14f,
            clearOpportunityBonus = 9f,
            clearOpportunityThreshold = 0.55f,
            sequenceNodeBudget = 6000,
            selectionTemperature = 2f,
            // 1.0 means an open board imposes no size preference at all, so the authored
            // weights alone decide what appears. Anything above that competes with them.
            openSizeFactor = 1f,
            crowdedSizeFactor = 0.45f
        };

        /// <summary>Replaces nonsensical Inspector values with the defaults.</summary>
        public TraySelectionConfig Sanitized()
        {
            TraySelectionConfig d = Default;
            return new TraySelectionConfig
            {
                candidateSets = candidateSets > 0 ? Mathf.Min(candidateSets, 64) : d.candidateSets,
                crowdingBias = crowdingBias >= 0f ? crowdingBias : d.crowdingBias,
                sizeFitWeight = sizeFitWeight >= 0f ? sizeFitWeight : d.sizeFitWeight,
                duplicatePenalty = duplicatePenalty >= 0f ? duplicatePenalty : d.duplicatePenalty,
                repeatPenalty = repeatPenalty >= 0f ? repeatPenalty : d.repeatPenalty,
                recentWindow = recentWindow >= 0 ? Mathf.Min(recentWindow, 32) : d.recentWindow,
                unplayablePenalty = unplayablePenalty > 0f ? unplayablePenalty : d.unplayablePenalty,
                playableBonus = playableBonus >= 0f ? playableBonus : d.playableBonus,
                sequenceBonus = sequenceBonus >= 0f ? sequenceBonus : d.sequenceBonus,
                clearOpportunityBonus = clearOpportunityBonus >= 0f ? clearOpportunityBonus : d.clearOpportunityBonus,
                clearOpportunityThreshold = Mathf.Clamp01(clearOpportunityThreshold),
                sequenceNodeBudget = sequenceNodeBudget > 0 ? sequenceNodeBudget : d.sequenceNodeBudget,
                selectionTemperature = selectionTemperature > 0.01f ? selectionTemperature : d.selectionTemperature,
                openSizeFactor = openSizeFactor > 0f ? openSizeFactor : d.openSizeFactor,
                crowdedSizeFactor = crowdedSizeFactor > 0f ? crowdedSizeFactor : d.crowdedSizeFactor
            };
        }
    }

    /// <summary>
    /// Chooses the next tray.
    ///
    /// Uniform random is what makes a block-drop clone feel cheap: it hands you three
    /// 3x3 blocks on a crowded board, or five small pieces in a row when you needed one.
    /// Instead of drawing once and hoping, this samples several candidate trays and keeps
    /// the best-scoring one, judging each on whether it is playable, whether all three
    /// fit in some order, how well the piece sizes suit how full the board is, and how
    /// repetitive it looks.
    ///
    /// Deliberately does not guarantee a survivable tray beyond "at least one piece
    /// fits". Guaranteeing the full sequence would remove the possibility of losing;
    /// rewarding it strongly, as here, removes the unfairness while keeping the tension.
    ///
    /// Pure and seeded, so a tray that felt wrong can be reproduced in a test.
    /// </summary>
    public sealed class TraySelector
    {
        private readonly ShapeCandidate[] catalog;
        private readonly float totalWeight;
        private readonly int minCells;
        private readonly int maxCells;

        /// <summary>
        /// Average piece size the authored weights alone would produce. Size targets are
        /// expressed as multiples of this, so crowding nudges the distribution the weights
        /// describe instead of replacing it.
        /// </summary>
        private readonly float weightedMeanCells;

        private readonly List<int> recent = new List<int>();
        private readonly List<ShapeCandidate> working = new List<ShapeCandidate>();

        private TraySelectionConfig config;
        private DeterministicRandom rng;

        public TraySelector(IReadOnlyList<ShapeCandidate> shapes, TraySelectionConfig selectionConfig, uint seed)
        {
            if (shapes == null) throw new ArgumentNullException(nameof(shapes));

            var usable = new List<ShapeCandidate>(shapes.Count);
            foreach (ShapeCandidate shape in shapes)
            {
                if (shape != null && shape.CellCount > 0) usable.Add(shape);
            }

            if (usable.Count == 0)
                throw new ArgumentException("Catalog contains no usable shapes.", nameof(shapes));

            catalog = usable.ToArray();
            config = selectionConfig.Sanitized();
            rng = new DeterministicRandom(seed);

            minCells = int.MaxValue;
            maxCells = int.MinValue;
            float weightedCells = 0f;

            foreach (ShapeCandidate shape in catalog)
            {
                totalWeight += shape.Weight;
                weightedCells += shape.Weight * shape.CellCount;
                if (shape.CellCount < minCells) minCells = shape.CellCount;
                if (shape.CellCount > maxCells) maxCells = shape.CellCount;
            }

            weightedMeanCells = totalWeight > 0f ? weightedCells / totalWeight : minCells;
        }

        public int CatalogSize => catalog.Length;

        /// <summary>Current RNG state, so a run can be resumed or a bad tray reproduced.</summary>
        public uint RandomState => rng.State;

        public TraySelectionConfig Config
        {
            get => config;
            set => config = value.Sanitized();
        }

        /// <summary>
        /// Picks <paramref name="count"/> shapes for a fresh tray.
        /// <paramref name="keep"/> may hold pieces already on offer that the new ones must
        /// sit alongside; pass null for a full refill.
        /// </summary>
        public ShapeCandidate[] SelectTray(BoardState board, int count, IReadOnlyList<PlacementTable> keep = null)
        {
            if (board == null) throw new ArgumentNullException(nameof(board));
            count = Mathf.Max(0, count);
            if (count == 0) return Array.Empty<ShapeCandidate>();

            int sets = config.candidateSets;
            var pool = new ShapeCandidate[sets][];
            var scores = new float[sets];
            float bestScore = float.NegativeInfinity;

            for (int attempt = 0; attempt < sets; attempt++)
            {
                var candidate = new ShapeCandidate[count];
                for (int i = 0; i < count; i++) candidate[i] = DrawWeighted();

                pool[attempt] = candidate;
                scores[attempt] = ScoreTray(board, candidate, keep);
                if (scores[attempt] > bestScore) bestScore = scores[attempt];
            }

            ShapeCandidate[] chosen = pool[PickBySoftmax(scores, bestScore)];
            foreach (ShapeCandidate shape in chosen) NoteDrawn(shape.Id);
            return chosen;
        }

        /// <summary>
        /// Picks among the sampled trays with probability rising exponentially in score,
        /// rather than always taking the highest.
        ///
        /// Taking the maximum makes selection deterministic given the sample, so whichever
        /// term happens to separate trays decides every draw and the authored weights stop
        /// showing up in the output. Softmax keeps the filtering that matters -- a tray
        /// carrying the unplayable penalty underflows to zero probability -- while trays
        /// that differ only slightly stay close to equally likely, so the catalog's
        /// weights still drive the distribution.
        /// </summary>
        private int PickBySoftmax(float[] scores, float bestScore)
        {
            float temperature = Mathf.Max(0.01f, config.selectionTemperature);
            float total = 0f;

            // Offset by the best score before exponentiating so large penalties cannot overflow.
            for (int i = 0; i < scores.Length; i++)
            {
                scores[i] = Mathf.Exp((scores[i] - bestScore) / temperature);
                total += scores[i];
            }

            if (total <= 0f || float.IsNaN(total)) return 0;

            float roll = rng.NextFloat() * total;
            for (int i = 0; i < scores.Length; i++)
            {
                roll -= scores[i];
                if (roll <= 0f) return i;
            }

            return scores.Length - 1;
        }

        /// <summary>Clears the anti-repeat history. Call when a new run starts.</summary>
        public void ResetHistory() => recent.Clear();

        // ---------- drawing ----------

        private ShapeCandidate DrawWeighted()
        {
            float roll = rng.NextFloat() * totalWeight;
            for (int i = 0; i < catalog.Length; i++)
            {
                roll -= catalog[i].Weight;
                if (roll <= 0f) return catalog[i];
            }

            return catalog[catalog.Length - 1];
        }

        private void NoteDrawn(int id)
        {
            if (config.recentWindow <= 0) return;

            recent.Add(id);
            while (recent.Count > config.recentWindow) recent.RemoveAt(0);
        }

        // ---------- scoring ----------

        private float ScoreTray(BoardState board, ShapeCandidate[] tray, IReadOnlyList<PlacementTable> keep)
        {
            working.Clear();
            foreach (ShapeCandidate shape in tray) working.Add(shape);

            float score = 0f;
            int playable = 0;

            foreach (ShapeCandidate shape in working)
            {
                if (BoardRules.HasAnyPlacement(board, shape.Table)) playable++;
            }

            // Pieces already on offer count towards survivability: a refill that tops up a
            // partly used tray must be judged alongside what is still sitting there.
            bool keepPlayable = false;
            if (keep != null)
            {
                foreach (PlacementTable table in keep)
                {
                    if (BoardRules.HasAnyPlacement(board, table)) { keepPlayable = true; break; }
                }
            }

            if (playable == 0 && !keepPlayable) score -= config.unplayablePenalty;
            else score += playable * config.playableBonus;

            score -= DuplicateCount(working) * config.duplicatePenalty;
            score -= RecentCount(working) * config.repeatPenalty;
            score -= SizeMismatch(board, working) * config.sizeFitWeight;

            if (CanPlaceAllInSomeOrder(board, working)) score += config.sequenceBonus;

            if (Crowding(board) >= config.clearOpportunityThreshold && OffersAClear(board, working))
                score += config.clearOpportunityBonus;

            return score;
        }

        private static int DuplicateCount(List<ShapeCandidate> tray)
        {
            int duplicates = 0;
            for (int i = 0; i < tray.Count; i++)
            {
                for (int j = i + 1; j < tray.Count; j++)
                {
                    if (tray[i].Id == tray[j].Id) duplicates++;
                }
            }

            return duplicates;
        }

        private int RecentCount(List<ShapeCandidate> tray)
        {
            if (recent.Count == 0) return 0;

            int count = 0;
            foreach (ShapeCandidate shape in tray)
            {
                if (recent.Contains(shape.Id)) count++;
            }

            return count;
        }

        private static float Crowding(BoardState board) =>
            board.FilledCount / (float)(board.Width * board.Height);

        /// <summary>
        /// How far the tray's average piece size sits from what the board can take. A full
        /// board wants small pieces; an open one wants big ones, or the run never builds
        /// enough pressure to be interesting.
        /// </summary>
        private float SizeMismatch(BoardState board, List<ShapeCandidate> tray)
        {
            if (maxCells == minCells) return 0f;

            // The target is a multiple of the catalog's weighted average rather than a
            // slide between the smallest and largest shapes. Aiming at the extremes made
            // this the only term that distinguished trays on an open board, so the biggest
            // piece always won and the authored weights stopped mattering -- a 0.35-weight
            // 3x3 was drawn seven times as often as a 1.0-weight single.
            float pressure = Mathf.Pow(Crowding(board), Mathf.Max(0.0001f, config.crowdingBias));
            float factor = Mathf.Lerp(config.openSizeFactor, config.crowdedSizeFactor, pressure);
            float target = Mathf.Clamp(weightedMeanCells * factor, minCells, maxCells);

            float total = 0f;
            foreach (ShapeCandidate shape in tray) total += shape.CellCount;
            float average = total / tray.Count;

            // Normalized so the term stays comparable across different catalogs.
            return Mathf.Abs(average - target) / (maxCells - minCells);
        }

        private bool OffersAClear(BoardState board, List<ShapeCandidate> tray)
        {
            foreach (ShapeCandidate shape in tray)
            {
                if (CanCompleteALine(board, shape.Table)) return true;
            }

            return false;
        }

        private static bool CanCompleteALine(BoardState board, PlacementTable shape)
        {
            for (int y = 0; y <= board.Height - shape.Height; y++)
            {
                for (int x = 0; x <= board.Width - shape.Width; x++)
                {
                    ulong mask = shape.MaskAt(x, y);
                    if (mask == 0UL || (board.Occupancy & mask) != 0UL) continue;
                    if (BoardRules.CompletesLine(board.Occupancy | mask, board.Width, board.Height)) return true;
                }
            }

            return false;
        }

        // ---------- lookahead ----------

        /// <summary>
        /// Whether every piece in the tray can be placed, in some order. Models the line
        /// clears that happen between placements, because ignoring them would wrongly
        /// reject trays that only fit once a line goes away.
        ///
        /// Depth-first with a node budget: on a crowded board the search space is large,
        /// and a refill must never stall the game. Running out of budget reports false,
        /// which only costs the tray a bonus -- it is never treated as a loss condition.
        /// </summary>
        private bool CanPlaceAllInSomeOrder(BoardState board, List<ShapeCandidate> tray)
        {
            if (tray.Count == 0) return true;
            if (tray.Count > 16) return false; // the remaining-piece bitmask is an int

            int budget = config.sequenceNodeBudget;
            int allPieces = (1 << tray.Count) - 1;
            return Search(board.Occupancy, tray, allPieces, board.Width, board.Height, ref budget);
        }

        private static bool Search(
            ulong occupancy, List<ShapeCandidate> tray, int remaining, int width, int height, ref int budget)
        {
            if (remaining == 0) return true;

            for (int i = 0; i < tray.Count; i++)
            {
                int bit = 1 << i;
                if ((remaining & bit) == 0) continue;

                PlacementTable shape = tray[i].Table;
                for (int y = 0; y <= height - shape.Height; y++)
                {
                    for (int x = 0; x <= width - shape.Width; x++)
                    {
                        if (--budget <= 0) return false;

                        ulong mask = shape.MaskAt(x, y);
                        if (mask == 0UL || (occupancy & mask) != 0UL) continue;

                        ulong next = BoardRules.ClearCompletedLines(occupancy | mask, width, height);
                        if (Search(next, tray, remaining & ~bit, width, height, ref budget)) return true;
                    }
                }
            }

            return false;
        }
    }
}
