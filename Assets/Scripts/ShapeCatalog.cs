using System;
using System.Collections.Generic;
using UnityEngine;
using BlockBlast.Core;

/// <summary>
/// The set of shapes the game can draw from, their relative frequencies, the colour
/// palette pieces are tinted with, and the tuning for how trays are chosen.
///
/// Keeping all four in one asset means the whole feel of spawning can be tuned in a
/// single Inspector window, and swapped wholesale for a different mode or difficulty
/// without touching the scene.
///
/// Colour lives here rather than on each shape because in play a piece's colour is
/// decoration, not identity -- tying red to "the 3x3" makes the board read as a shape
/// chart instead of a puzzle.
/// </summary>
[CreateAssetMenu(fileName = "ShapeCatalog", menuName = "Block Blast/Shape Catalog", order = 0)]
public class ShapeCatalog : ScriptableObject
{
    /// <summary>Where SpawnManager looks when no catalog is wired up in the Inspector.</summary>
    public const string ResourcesPath = "ShapeCatalog";

    [Serializable]
    public struct Entry
    {
        public BlockData shape;

        [Tooltip("Relative draw frequency. 2 is twice as common as 1; 0 disables the shape.")]
        [Range(0f, 10f)] public float weight;
    }

    [Header("Shapes")]
    [SerializeField] private Entry[] entries = Array.Empty<Entry>();

    [Header("Palette")]
    [Tooltip("Where block colours come from. Falls back to Resources/BlockSkin, then to " +
             "the raw palette below.")]
    [SerializeField] private BlockSkin skin;

    [Tooltip("Raw colours, used only when no skin is available.")]
    [SerializeField] private Color[] palette = Array.Empty<Color>();

    private BlockSkin resolvedSkin;
    private bool skinLookupDone;

    [Header("Selection")]
    [SerializeField] private TraySelectionConfig selection = TraySelectionConfig.Default;

    public IReadOnlyList<Entry> Entries => entries;
    public TraySelectionConfig Selection => selection.Sanitized();

    /// <summary>
    /// The skin in use, resolved once. Looked up lazily rather than in a constructor
    /// because ScriptableObjects are deserialized before Resources is safe to touch.
    /// </summary>
    public BlockSkin Skin
    {
        get
        {
            if (skinLookupDone) return resolvedSkin;

            skinLookupDone = true;
            resolvedSkin = skin != null ? skin : Resources.Load<BlockSkin>(BlockSkin.ResourcesPath);
            return resolvedSkin;
        }
    }

    public int PaletteSize
    {
        get
        {
            BlockSkin active = Skin;
            if (active != null && active.BlockCount > 0) return active.BlockCount;
            return palette?.Length ?? 0;
        }
    }

    public Color ColorAt(int index)
    {
        BlockSkin active = Skin;
        if (active != null && active.BlockCount > 0) return active.ColorAt(index);
        if (palette == null || palette.Length == 0) return Color.white;

        return palette[((index % palette.Length) + palette.Length) % palette.Length];
    }

    /// <summary>
    /// Compiles the catalog for one board size. Candidate ids index into
    /// <see cref="Entries"/>, so the caller can map a chosen candidate back to its asset.
    /// Entries with no shape or zero weight are skipped, which is how a shape is
    /// temporarily disabled without deleting it.
    /// </summary>
    public List<ShapeCandidate> BuildCandidates(int boardWidth, int boardHeight)
    {
        var candidates = new List<ShapeCandidate>(entries.Length);

        for (int i = 0; i < entries.Length; i++)
        {
            Entry entry = entries[i];
            if (entry.shape == null || entry.weight <= 0f || entry.shape.CellCount == 0) continue;

            PlacementTable table = entry.shape.GetPlacementTable(boardWidth, boardHeight);
            if (table.CellCount == 0) continue;

            candidates.Add(new ShapeCandidate(i, table, entry.weight));
        }

        return candidates;
    }

    /// <summary>Maps a candidate id back to the asset it came from.</summary>
    public BlockData ShapeById(int id) =>
        (uint)id < (uint)entries.Length ? entries[id].shape : null;

#if UNITY_EDITOR
    private void OnValidate()
    {
        selection = selection.Sanitized();

        if (entries == null) return;

        int usable = 0;
        for (int i = 0; i < entries.Length; i++)
        {
            if (entries[i].shape != null && entries[i].weight > 0f) usable++;
        }

        if (usable == 0)
            Debug.LogWarning($"[{name}] No shape has both an asset and a non-zero weight; " +
                             "the tray cannot be filled from this catalog.", this);
    }
#endif
}
