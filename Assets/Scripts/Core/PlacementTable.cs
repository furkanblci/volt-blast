using System;
using System.Collections.Generic;
using UnityEngine;

namespace BlockBlast.Core
{
    /// <summary>
    /// A shape compiled against one board size: for every anchor cell we precompute
    /// the exact bitboard the piece would occupy, or 0 when the piece would hang off
    /// the edge. Fit-testing then costs one array read and one AND, which is what
    /// makes the "is any move still possible" sweep cheap enough to run every turn.
    ///
    /// Anchors are the bottom-left corner of the shape's bounding box, so the cells
    /// are normalized on build and the pivot is unambiguous for both logic and visuals.
    /// </summary>
    public sealed class PlacementTable
    {
        private readonly ulong[] anchorMasks = new ulong[BoardState.CellCapacity];

        /// <summary>Cells relative to the bounding box's bottom-left corner.</summary>
        public Vector2Int[] Cells { get; }

        public int CellCount => Cells.Length;
        public int Width { get; }
        public int Height { get; }
        public int BoardWidth { get; }
        public int BoardHeight { get; }

        /// <summary>Union of every legal placement. A piece with no legal anchor has 0 here.</summary>
        public ulong ReachableMask { get; }

        public PlacementTable(IReadOnlyList<Vector2Int> shape, int boardWidth, int boardHeight)
        {
            if (shape == null) throw new ArgumentNullException(nameof(shape));

            BoardWidth = boardWidth;
            BoardHeight = boardHeight;
            Cells = Normalize(shape, out int width, out int height);
            Width = width;
            Height = height;

            if (Cells.Length == 0) return;

            ulong reachable = 0UL;
            for (int ay = 0; ay <= boardHeight - height; ay++)
            {
                for (int ax = 0; ax <= boardWidth - width; ax++)
                {
                    ulong mask = 0UL;
                    for (int i = 0; i < Cells.Length; i++)
                        mask |= BoardState.BitAt(ax + Cells[i].x, ay + Cells[i].y);

                    anchorMasks[BoardState.BitIndex(ax, ay)] = mask;
                    reachable |= mask;
                }
            }

            ReachableMask = reachable;
        }

        /// <summary>
        /// Cells the piece would occupy anchored at (x, y), or 0 when that anchor is
        /// off-board. Callers must treat 0 as "cannot place" rather than "empty".
        /// </summary>
        public ulong MaskAt(int x, int y) =>
            (uint)x < (uint)BoardState.Stride && (uint)y < (uint)BoardState.Stride
                ? anchorMasks[BoardState.BitIndex(x, y)]
                : 0UL;

        /// <summary>Strips duplicates and shifts the shape so its bounding box starts at (0,0).</summary>
        private static Vector2Int[] Normalize(IReadOnlyList<Vector2Int> shape, out int width, out int height)
        {
            width = 0;
            height = 0;
            if (shape.Count == 0) return Array.Empty<Vector2Int>();

            int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
            for (int i = 0; i < shape.Count; i++)
            {
                Vector2Int c = shape[i];
                if (c.x < minX) minX = c.x;
                if (c.x > maxX) maxX = c.x;
                if (c.y < minY) minY = c.y;
                if (c.y > maxY) maxY = c.y;
            }

            width = maxX - minX + 1;
            height = maxY - minY + 1;

            var seen = new HashSet<Vector2Int>();
            var normalized = new List<Vector2Int>(shape.Count);
            for (int i = 0; i < shape.Count; i++)
            {
                var cell = new Vector2Int(shape[i].x - minX, shape[i].y - minY);
                if (seen.Add(cell)) normalized.Add(cell);
            }

            return normalized.ToArray();
        }
    }
}
