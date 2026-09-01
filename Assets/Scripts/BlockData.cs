using System.Collections.Generic;
using UnityEngine;
using BlockBlast.Core;

/// <summary>
/// Authoring asset for one piece shape.
/// Create via: Create > Block Blast > Block Data
///
/// The asset only holds authored data. Everything the rules need is derived into a
/// <see cref="PlacementTable"/> the first time a board size asks for one, and cached
/// on the asset -- building those masks is not something to redo per placement test.
/// </summary>
[CreateAssetMenu(fileName = "New Block", menuName = "Block Blast/Block Data", order = 1)]
public class BlockData : ScriptableObject
{
    [Header("Block Identity")]
    [SerializeField] private string blockName = "New Block";
    [SerializeField] private int blockId;

    [Header("Shape Definition")]
    [Tooltip("Cells that make up this shape. Coordinates are normalized on load, so the " +
             "bounding box corner becomes the pivot regardless of how they are authored.")]
    [SerializeField] private List<Vector2Int> shape = new List<Vector2Int>();

    [Header("Visual")]
    [SerializeField] private Color blockColor = Color.white;

    // Cached per board size. In practice only one size is ever requested, but keying on
    // it keeps the cache honest if the board is ever resized at runtime.
    private PlacementTable cachedTable;
    private int cachedBoardWidth;
    private int cachedBoardHeight;

    public string BlockName => string.IsNullOrEmpty(blockName) ? name : blockName;
    public int BlockId => blockId;
    public IReadOnlyList<Vector2Int> Shape => shape;
    public Color BlockColor => blockColor;

    /// <summary>Stable identity for save files. The asset name survives renames of the display name.</summary>
    public string SaveId => name;

    public int CellCount => shape?.Count ?? 0;

    /// <summary>
    /// Placement masks for this shape on a board of the given size. Built once and
    /// reused; the returned table also exposes the normalized cells the visuals draw.
    /// </summary>
    public PlacementTable GetPlacementTable(int boardWidth, int boardHeight)
    {
        if (cachedTable == null || cachedBoardWidth != boardWidth || cachedBoardHeight != boardHeight)
        {
            cachedTable = new PlacementTable(shape, boardWidth, boardHeight);
            cachedBoardWidth = boardWidth;
            cachedBoardHeight = boardHeight;
        }

        return cachedTable;
    }

    /// <summary>Packed board colour for cells this piece fills.</summary>
    public uint PackedColor => ColorPacking.Pack(blockColor);

    private void OnDisable()
    {
        // Domain reload and asset unload both land here; drop the cache so a rebuilt
        // table never outlives an edit to the shape.
        cachedTable = null;
    }

#if UNITY_EDITOR
    /// <summary>Validation only -- never mutates the authored shape.</summary>
    private void OnValidate()
    {
        cachedTable = null;

        if (shape == null || shape.Count == 0)
        {
            Debug.LogWarning($"[{name}] Shape is empty; this piece can never be placed.", this);
            return;
        }

        var seen = new HashSet<Vector2Int>();
        var duplicates = new List<Vector2Int>();
        foreach (Vector2Int cell in shape)
        {
            if (!seen.Add(cell)) duplicates.Add(cell);
        }

        if (duplicates.Count > 0)
        {
            Debug.LogWarning($"[{name}] Duplicate cells {string.Join(", ", duplicates)} are ignored at runtime; " +
                             "remove them to keep the asset honest.", this);
        }
    }
#endif
}
