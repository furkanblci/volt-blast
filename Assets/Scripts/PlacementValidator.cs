using UnityEngine;
using BlockBlast.Core;

/// <summary>
/// Bridges authoring assets to the rule engine: given a <see cref="BlockData"/> it
/// resolves the compiled <see cref="PlacementTable"/> for the current board size and
/// answers placement questions against it.
///
/// The rules themselves live in <see cref="BoardRules"/>. This component exists so the
/// scene has one place to ask, and so BlockData-to-PlacementTable resolution is not
/// duplicated across the drag controller, the spawner and the game-over check.
/// </summary>
public class PlacementValidator : MonoBehaviour
{
    [SerializeField] private GridManager gridManager;

    private void Awake() => ResolveGrid();

    private void ResolveGrid()
    {
        if (gridManager == null) gridManager = FindAnyObjectByType<GridManager>();
    }

    private GridManager Grid
    {
        get
        {
            if (gridManager == null) ResolveGrid();
            return gridManager;
        }
    }

    /// <summary>Compiled masks for a piece on the current board, or null if either is missing.</summary>
    public PlacementTable GetTable(BlockData blockData)
    {
        GridManager grid = Grid;
        if (blockData == null || grid == null) return null;

        return blockData.GetPlacementTable(grid.GridWidth, grid.GridHeight);
    }

    public bool CanPlaceBlock(BlockData blockData, int anchorX, int anchorY)
    {
        GridManager grid = Grid;
        if (grid == null) return false;

        return BoardRules.CanPlace(grid.Board, GetTable(blockData), anchorX, anchorY);
    }

    /// <summary>True when this piece still fits somewhere. Backs the game-over check.</summary>
    public bool CanPlaceAnywhere(BlockData blockData)
    {
        GridManager grid = Grid;
        if (grid == null) return false;

        return BoardRules.HasAnyPlacement(grid.Board, GetTable(blockData));
    }

    /// <summary>
    /// Snaps a dragged piece's pivot to the board and reports whether it lands legally.
    /// Takes the piece's own pivot position rather than the pointer position, so what the
    /// player sees under their finger is exactly what gets tested.
    /// </summary>
    public bool TrySnap(BlockData blockData, Vector3 pivotWorldPosition, out Vector2Int anchor)
    {
        anchor = new Vector2Int(-1, -1);

        GridManager grid = Grid;
        if (grid == null || blockData == null) return false;

        Vector2Int candidate = grid.WorldToNearestCell(pivotWorldPosition);
        if (!CanPlaceBlock(blockData, candidate.x, candidate.y))
        {
            // Still report where it snapped so the ghost can show an invalid preview.
            anchor = candidate;
            return false;
        }

        anchor = candidate;
        return true;
    }
}
