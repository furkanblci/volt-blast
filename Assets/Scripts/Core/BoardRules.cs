using System.Collections.Generic;

namespace BlockBlast.Core
{
    /// <summary>Rows and columns completed by a single placement.</summary>
    public readonly struct LineClearResult
    {
        /// <summary>Every cell the clear removes. Zero when nothing completed.</summary>
        public ulong ClearedMask { get; }
        public int RowCount { get; }
        public int ColumnCount { get; }

        public LineClearResult(ulong clearedMask, int rowCount, int columnCount)
        {
            ClearedMask = clearedMask;
            RowCount = rowCount;
            ColumnCount = columnCount;
        }

        public int LineCount => RowCount + ColumnCount;
        public int CellCount => BoardState.PopCount(ClearedMask);
        public bool Any => ClearedMask != 0UL;
    }

    /// <summary>
    /// Every rule that decides what a move does. Stateless and MonoBehaviour-free on
    /// purpose: the same calls that drive the game also drive the spawn solvability
    /// check and any future hint or auto-play feature, with no scene in the way.
    /// </summary>
    public static class BoardRules
    {
        public static bool CanPlace(BoardState board, PlacementTable shape, int anchorX, int anchorY)
        {
            if (shape == null) return false;
            ulong mask = shape.MaskAt(anchorX, anchorY);
            return mask != 0UL && (board.Occupancy & mask) == 0UL;
        }

        /// <summary>
        /// Writes the piece onto the board. Returns false and leaves the board untouched
        /// when the anchor is illegal, so callers can attempt a placement optimistically.
        /// </summary>
        public static bool TryPlace(BoardState board, PlacementTable shape, int anchorX, int anchorY, uint packedColor)
        {
            if (shape == null) return false;
            ulong mask = shape.MaskAt(anchorX, anchorY);
            if (mask == 0UL || (board.Occupancy & mask) != 0UL) return false;

            board.FillMask(mask, packedColor);
            return true;
        }

        /// <summary>
        /// Finds completed lines without mutating anything, so the presentation layer can
        /// play a clear animation first and commit via <see cref="ApplyClear"/> afterwards.
        /// </summary>
        public static LineClearResult FindCompletedLines(BoardState board) =>
            FindCompletedLines(board.Occupancy, board.Width, board.Height);

        public static void ApplyClear(BoardState board, LineClearResult result)
        {
            if (result.Any) board.ClearMask(result.ClearedMask);
        }

        /// <summary>True when the piece fits anywhere. Bails at the first legal anchor.</summary>
        public static bool HasAnyPlacement(BoardState board, PlacementTable shape)
        {
            if (shape == null || shape.CellCount == 0) return false;

            for (int y = 0; y <= board.Height - shape.Height; y++)
            {
                for (int x = 0; x <= board.Width - shape.Width; x++)
                {
                    ulong mask = shape.MaskAt(x, y);
                    if (mask != 0UL && (board.Occupancy & mask) == 0UL) return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Game-over test: the run continues while at least one piece still in the tray
        /// has a legal anchor. Checking pieces independently is sound here because a
        /// placement only ever fills cells, so a piece that does not fit the current
        /// board cannot start fitting because another piece was placed first.
        /// </summary>
        public static bool HasAnyMove(BoardState board, IReadOnlyList<PlacementTable> tray)
        {
            for (int i = 0; i < tray.Count; i++)
            {
                if (HasAnyPlacement(board, tray[i])) return true;
            }

            return false;
        }

        // ---------- raw-bitboard helpers ----------
        // These work on a bare occupancy value rather than a BoardState so lookahead can
        // explore hypothetical boards without allocating or disturbing the real one.

        /// <summary>True when the given occupancy has at least one completed row or column.</summary>
        public static bool CompletesLine(ulong occupancy, int width, int height)
        {
            for (int y = 0; y < height; y++)
            {
                ulong row = BoardState.RowMask(y, width);
                if ((occupancy & row) == row) return true;
            }

            for (int x = 0; x < width; x++)
            {
                ulong column = BoardState.ColumnMask(x, height);
                if ((occupancy & column) == column) return true;
            }

            return false;
        }

        /// <summary>
        /// Completed lines in a hypothetical occupancy. Lets the ghost preview show which
        /// rows and columns a placement would clear before the player commits to it --
        /// the single most useful thing the preview can tell them.
        /// </summary>
        public static LineClearResult FindCompletedLines(ulong occupancy, int width, int height)
        {
            ulong cleared = 0UL;
            int rows = 0, columns = 0;

            for (int y = 0; y < height; y++)
            {
                ulong row = BoardState.RowMask(y, width);
                if ((occupancy & row) == row)
                {
                    cleared |= row;
                    rows++;
                }
            }

            for (int x = 0; x < width; x++)
            {
                ulong column = BoardState.ColumnMask(x, height);
                if ((occupancy & column) == column)
                {
                    cleared |= column;
                    columns++;
                }
            }

            return new LineClearResult(cleared, rows, columns);
        }

        /// <summary>
        /// Occupancy with all completed lines removed. Used by lookahead, which must model
        /// clears to avoid judging a tray unplayable when the run would actually free space
        /// partway through it.
        /// </summary>
        public static ulong ClearCompletedLines(ulong occupancy, int width, int height)
        {
            ulong cleared = 0UL;

            for (int y = 0; y < height; y++)
            {
                ulong row = BoardState.RowMask(y, width);
                if ((occupancy & row) == row) cleared |= row;
            }

            for (int x = 0; x < width; x++)
            {
                ulong column = BoardState.ColumnMask(x, height);
                if ((occupancy & column) == column) cleared |= column;
            }

            return occupancy & ~cleared;
        }

        /// <summary>Collects every legal anchor for a piece. Allocates, so keep it off the hot path.</summary>
        public static List<int> FindAllPlacements(BoardState board, PlacementTable shape)
        {
            var anchors = new List<int>();
            if (shape == null || shape.CellCount == 0) return anchors;

            for (int y = 0; y <= board.Height - shape.Height; y++)
            {
                for (int x = 0; x <= board.Width - shape.Width; x++)
                {
                    ulong mask = shape.MaskAt(x, y);
                    if (mask != 0UL && (board.Occupancy & mask) == 0UL)
                        anchors.Add(BoardState.BitIndex(x, y));
                }
            }

            return anchors;
        }
    }
}
