using System;
using UnityEngine;
using BlockBlast.Core;

/// <summary>
/// Scene-facing owner of the board.
///
/// This type deliberately does one job now: hold the <see cref="BoardState"/> and the
/// <see cref="GridGeometry"/>, and announce what changed. It used to also award score,
/// clear lines and trigger game over from inside PlaceBlock, which meant a single
/// placement fanned out into three subsystems before the board had finished changing --
/// and made it impossible to animate a clear without the game-over check firing first.
/// Turn sequencing now lives in <see cref="GameManager"/>.
/// </summary>
public class GridManager : MonoBehaviour
{
    [Header("Grid Settings")]
    [SerializeField, Range(1, 8)] private int gridWidth = 8;
    [SerializeField, Range(1, 8)] private int gridHeight = 8;

    [Header("Layout")]
    [Tooltip("Size of one cell in world units.")]
    [SerializeField] private float cellSize = 1f;
    [Tooltip("Gap between cells. Visuals and hit-testing both derive from this, so it only lives here.")]
    [SerializeField] private float cellSpacing = 0.1f;
    [Tooltip("World position the board is centred on.")]
    [SerializeField] private Vector2 gridCenter = Vector2.zero;

    private BoardState board;
    private GridGeometry geometry;

    /// <summary>Cells that just became filled, with the colour to paint them.</summary>
    public event Action<ulong, Color> CellsFilled;

    /// <summary>Cells that just became empty.</summary>
    public event Action<ulong> CellsCleared;

    /// <summary>The whole board was replaced (new game, or a save was loaded).</summary>
    public event Action BoardReset;

    public BoardState Board
    {
        get
        {
            EnsureInitialized();
            return board;
        }
    }

    public GridGeometry Geometry
    {
        get
        {
            EnsureInitialized();
            return geometry;
        }
    }

    public int GridWidth => gridWidth;
    public int GridHeight => gridHeight;
    public float CellSize => cellSize;
    public float CellSpacing => cellSpacing;

    /// <summary>
    /// Moves the board. Only <see cref="BoardLayout"/> should call this, and only before
    /// the visuals build themselves -- cells read their world position once, so moving the
    /// board afterwards would leave the drawn grid behind the logical one.
    /// </summary>
    public void SetCenter(Vector2 center)
    {
        EnsureInitialized();
        gridCenter = center;
        geometry = new GridGeometry(gridWidth, gridHeight, cellSize, cellSpacing, gridCenter);
    }

    private void Awake() => EnsureInitialized();

    /// <summary>
    /// Builds the board on first touch rather than only in Awake, because other
    /// components legitimately query the grid from their own Awake and Unity does not
    /// promise an order between them.
    /// </summary>
    private void EnsureInitialized()
    {
        if (board != null) return;

        gridWidth = Mathf.Clamp(gridWidth, 1, BoardState.Stride);
        gridHeight = Mathf.Clamp(gridHeight, 1, BoardState.Stride);

        board = new BoardState(gridWidth, gridHeight);
        geometry = new GridGeometry(gridWidth, gridHeight, cellSize, cellSpacing, gridCenter);

        // Place the board for the screen we are actually on. Done here rather than pushed
        // in by a layout component because Unity does not define an Awake order, and the
        // visualizer reads this geometry from its own Awake.
        if (Application.isPlaying)
        {
            ScreenLayout fit = ScreenLayout.ForCurrentScreen(geometry, BoardLayout.Active);
            gridCenter = fit.BoardCenter;
            geometry = new GridGeometry(gridWidth, gridHeight, cellSize, cellSpacing, gridCenter);
        }
    }

    // ---------- queries ----------

    public bool IsInsideGrid(int x, int y) => Board.IsInside(x, y);

    public bool IsCellEmpty(int x, int y) => Board.IsEmpty(x, y);

    public Color GetCellColor(int x, int y) => ColorPacking.Unpack(Board.GetColor(x, y));

    public bool CanPlace(PlacementTable shape, int anchorX, int anchorY) =>
        BoardRules.CanPlace(Board, shape, anchorX, anchorY);

    public Vector3 CellToWorld(int x, int y) => Geometry.CellToWorld(x, y);

    /// <summary>Nearest cell to a snapped pivot. May be outside the board; check with <see cref="IsInsideGrid"/>.</summary>
    public Vector2Int WorldToNearestCell(Vector3 world) => Geometry.WorldToNearestCell(world);

    /// <summary>Cell containing a raw pointer position, or (-1,-1) when off-board.</summary>
    public Vector2Int WorldToContainingCell(Vector3 world) => Geometry.WorldToContainingCell(world);

    // ---------- mutation ----------

    /// <summary>
    /// Writes a piece onto the board and reports the cells it filled. Returns false
    /// without touching anything when the anchor is illegal.
    /// </summary>
    public bool TryPlace(PlacementTable shape, int anchorX, int anchorY, Color color, out ulong filledMask)
    {
        filledMask = shape != null ? shape.MaskAt(anchorX, anchorY) : 0UL;

        if (!BoardRules.TryPlace(Board, shape, anchorX, anchorY, ColorPacking.Pack(color)))
        {
            filledMask = 0UL;
            return false;
        }

        CellsFilled?.Invoke(filledMask, color);
        return true;
    }

    /// <summary>Reports completed lines without changing the board, so a clear can be animated first.</summary>
    public LineClearResult FindCompletedLines() => BoardRules.FindCompletedLines(Board);

    /// <summary>Commits a clear that <see cref="FindCompletedLines"/> found.</summary>
    public void ApplyClear(LineClearResult result)
    {
        if (!result.Any) return;

        BoardRules.ApplyClear(Board, result);
        CellsCleared?.Invoke(result.ClearedMask);
    }

    public void ClearGrid()
    {
        Board.Clear();
        BoardReset?.Invoke();
    }

    /// <summary>Replaces the board from a save. Fires <see cref="BoardReset"/> so visuals repaint wholesale.</summary>
    public bool TryRestore(GameSave save)
    {
        if (save == null || !save.TryApplyTo(Board)) return false;

        BoardReset?.Invoke();
        return true;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        // Re-derive geometry so layout tweaks show up without entering Play mode.
        gridWidth = Mathf.Clamp(gridWidth, 1, BoardState.Stride);
        gridHeight = Mathf.Clamp(gridHeight, 1, BoardState.Stride);
        cellSize = Mathf.Max(0.01f, cellSize);
        cellSpacing = Mathf.Max(0f, cellSpacing);

        if (board != null && (board.Width != gridWidth || board.Height != gridHeight))
        {
            board = new BoardState(gridWidth, gridHeight);
        }

        geometry = new GridGeometry(gridWidth, gridHeight, cellSize, cellSpacing, gridCenter);
    }

    private void OnDrawGizmosSelected()
    {
        var g = new GridGeometry(gridWidth, gridHeight, cellSize, cellSpacing, gridCenter);
        Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.6f);
        Gizmos.DrawWireCube(g.Bounds.center, new Vector3(g.TotalWidth, g.TotalHeight, 0f));
    }
#endif
}
