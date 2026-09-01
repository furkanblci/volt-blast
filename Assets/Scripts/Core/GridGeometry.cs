using UnityEngine;

namespace BlockBlast.Core
{
    /// <summary>
    /// The single source of truth for where a grid cell sits in world space.
    ///
    /// This used to be four independent copies of cellSize/cellSpacing (GridManager,
    /// GridVisualizer, BlockInstance, SpawnManager); any one of them drifting in the
    /// Inspector silently broke the world-to-grid mapping. Everything now reads this
    /// struct from GridManager instead of holding its own numbers.
    ///
    /// Cells advance by <see cref="Pitch"/> (size plus gap), which is why loose block
    /// visuals must also be laid out on Pitch rather than on cell size alone -- laying
    /// them out on size alone makes a 3-wide piece narrower than 3 grid cells.
    /// </summary>
    public readonly struct GridGeometry
    {
        public int Width { get; }
        public int Height { get; }
        public float CellSize { get; }
        public float CellSpacing { get; }

        /// <summary>World position the grid's bounding box is centred on.</summary>
        public Vector2 Center { get; }

        public GridGeometry(int width, int height, float cellSize, float cellSpacing, Vector2 center)
        {
            Width = Mathf.Max(1, width);
            Height = Mathf.Max(1, height);
            CellSize = Mathf.Max(0.0001f, cellSize);
            CellSpacing = Mathf.Max(0f, cellSpacing);
            Center = center;
        }

        /// <summary>Distance between the centres of two neighbouring cells.</summary>
        public float Pitch => CellSize + CellSpacing;

        public float TotalWidth => Width * CellSize + (Width - 1) * CellSpacing;
        public float TotalHeight => Height * CellSize + (Height - 1) * CellSpacing;

        /// <summary>Bottom-left corner of the grid's bounding box.</summary>
        public Vector2 Origin => Center - new Vector2(TotalWidth, TotalHeight) * 0.5f;

        /// <summary>Bottom-left corner of cell (0,0)'s centre -- the anchor all conversions hang off.</summary>
        private Vector2 FirstCellCenter => Origin + new Vector2(CellSize, CellSize) * 0.5f;

        public Vector3 CellToWorld(int x, int y)
        {
            Vector2 p = FirstCellCenter + new Vector2(x * Pitch, y * Pitch);
            return new Vector3(p.x, p.y, 0f);
        }

        public Vector3 CellToWorld(Vector2Int cell) => CellToWorld(cell.x, cell.y);

        /// <summary>
        /// Nearest cell to a point that is itself a cell centre -- the conversion to use
        /// when snapping a dragged piece, whose pivot sits on a centre rather than inside
        /// an arbitrary cell. Rounds, so a piece half a cell past a boundary still snaps
        /// to the cell it visually covers. May return an out-of-range cell; check
        /// <see cref="IsInside"/>.
        /// </summary>
        public Vector2Int WorldToNearestCell(Vector3 world)
        {
            Vector2 local = (Vector2)world - FirstCellCenter;
            return new Vector2Int(
                Mathf.RoundToInt(local.x / Pitch),
                Mathf.RoundToInt(local.y / Pitch));
        }

        /// <summary>
        /// The cell whose bounds contain a point -- the conversion to use for a raw
        /// pointer position. Returns (-1,-1) when the point is outside the grid.
        /// </summary>
        public Vector2Int WorldToContainingCell(Vector3 world)
        {
            Vector2 local = (Vector2)world - Origin;
            var cell = new Vector2Int(
                Mathf.FloorToInt(local.x / Pitch),
                Mathf.FloorToInt(local.y / Pitch));

            return IsInside(cell) ? cell : new Vector2Int(-1, -1);
        }

        public bool IsInside(int x, int y) => (uint)x < (uint)Width && (uint)y < (uint)Height;

        public bool IsInside(Vector2Int cell) => IsInside(cell.x, cell.y);

        /// <summary>
        /// Local offset of a loose piece's cell from that piece's pivot. Uses Pitch so a
        /// piece overlays the grid exactly once it is snapped.
        /// </summary>
        public Vector3 ShapeCellOffset(Vector2Int cell) =>
            new Vector3(cell.x * Pitch, cell.y * Pitch, 0f);

        /// <summary>World-space bounds of the whole board, for camera framing and layout.</summary>
        public Rect Bounds => new Rect(Origin.x, Origin.y, TotalWidth, TotalHeight);
    }
}
