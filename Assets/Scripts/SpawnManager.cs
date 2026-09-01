using System;
using System.Collections.Generic;
using UnityEngine;
using BlockBlast.Core;

/// <summary>
/// Owns the tray: which pieces are on offer, where they sit, and when a fresh set
/// appears.
///
/// It no longer decides anything about the run. Previously consuming the last piece
/// re-spawned immediately and asked the GameManager to check for game over from inside
/// that call, so the check could run against a tray that was mid-rebuild. The spawner
/// now only reports that the tray emptied; <see cref="GameManager"/> decides when to
/// refill and when to test for game over, in that order.
///
/// Which pieces appear is decided by <see cref="TraySelector"/> against a
/// <see cref="ShapeCatalog"/>. Uniform random draws are what make a block-drop clone
/// feel arbitrary; the selector samples several candidate trays and keeps the one that
/// best suits the board.
/// </summary>
public class SpawnManager : MonoBehaviour
{
    [Header("Shapes")]
    [Tooltip("Shapes, weights, palette and selection tuning. Falls back to Resources/ShapeCatalog, " +
             "then to the legacy Available Blocks list below.")]
    [SerializeField] private ShapeCatalog catalog;

    [Tooltip("Legacy flat list, used only when no catalog is available. Every shape is equally likely.")]
    [SerializeField] private List<BlockData> availableBlocks = new List<BlockData>();

    [SerializeField] private GameObject blockPrefab;
    [SerializeField] private GameObject cellPrefab;

    [Header("Tray Layout")]
    [SerializeField] private int numberOfSpawnSlots = 3;
    [SerializeField] private Vector3 spawnAreaCenter = new Vector3(0f, -7f, 0f);
    [SerializeField] private float spacingBetweenBlocks = 3f;

    [Tooltip("Largest size a tray piece may take, relative to board cells. Pieces shrink " +
             "further if their slot is too narrow, and grow to 1:1 when picked up.")]
    [SerializeField, Range(0.2f, 1f)] private float traySlotScale = 0.55f;

    [Header("Randomness")]
    [Tooltip("Fixed seed for reproducible runs. 0 seeds from the clock.")]
    [SerializeField] private int randomSeed;

    [Header("References")]
    [SerializeField] private GridManager gridManager;

    private BlockInstance[] slots;
    private Vector3[] slotCenters;

    private TraySelector selector;
    private BlockData[] shapeById;
    private ShapeCatalog activeCatalog;

    private float slotExtent;
    private int lastPaletteIndex = -1;
    private DeterministicRandom paletteRng;

    /// <summary>Raised whenever the tray contents change, so UI can react.</summary>
    public event Action TrayChanged;

    /// <summary>
    /// Raised as a piece leaves its slot, with where it sat and what colour it was.
    /// Lets the slot puff without the spawner owning any particles itself.
    /// </summary>
    public event Action<Vector3, Color> PieceConsumed;

    /// <summary>Slots in order. A null entry is a slot whose piece has been played.</summary>
    public IReadOnlyList<BlockInstance> Slots
    {
        get
        {
            EnsureSlots();
            return slots;
        }
    }

    public bool IsTrayEmpty
    {
        get
        {
            EnsureSlots();
            foreach (BlockInstance piece in slots)
            {
                if (piece != null) return false;
            }

            return true;
        }
    }

    /// <summary>How many pieces the tray holds. Read by layout before slots are built.</summary>
    public int SlotCount => Mathf.Max(1, numberOfSpawnSlots);

    /// <summary>
    /// Repositions the tray. Called by <see cref="BoardLayout"/> so slot positions follow
    /// the screen rather than the hand-placed values the scene was authored with.
    /// </summary>
    public void SetTrayLayout(Vector3 center, float spacing)
    {
        spawnAreaCenter = center;
        spacingBetweenBlocks = Mathf.Max(0.01f, spacing);

        // Force a recompute; slot centres are cached and would otherwise keep the old row.
        slotCenters = null;
        EnsureSlots();

        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i] != null) slots[i].SetHomeSlot(slotCenters[i]);
        }
    }

    private void Awake()
    {
        if (gridManager == null) gridManager = FindAnyObjectByType<GridManager>();
        EnsureSlots();
    }

    private void OnEnable()
    {
        if (gridManager == null) gridManager = FindAnyObjectByType<GridManager>();
        if (gridManager == null) return;

        // Playability depends on the board *and* the tray, so both have to re-trigger it.
        gridManager.CellsFilled += HandleCellsFilled;
        gridManager.CellsCleared += HandleCellsCleared;
        gridManager.BoardReset += RefreshPlayability;

        // Subscribing to our own event rather than calling the refresh after each of the
        // four places that raise it: a fifth one added later would silently skip it.
        TrayChanged += RefreshPlayability;
    }

    private void OnDisable()
    {
        TrayChanged -= RefreshPlayability;
        if (gridManager == null) return;

        gridManager.CellsFilled -= HandleCellsFilled;
        gridManager.CellsCleared -= HandleCellsCleared;
        gridManager.BoardReset -= RefreshPlayability;
    }

    private void HandleCellsFilled(ulong mask, Color color) => RefreshPlayability();

    private void HandleCellsCleared(ulong mask) => RefreshPlayability();

    /// <summary>
    /// Dims the pieces that no longer fit anywhere.
    ///
    /// Without this the player has to mentally test every piece against every square to
    /// find out which ones are dead, which is work the game can just do -- and the dimming
    /// is what makes a filling board feel like it is closing in rather than merely getting
    /// busier.
    /// </summary>
    private void RefreshPlayability()
    {
        if (gridManager == null || slots == null) return;

        BoardState board = gridManager.Board;
        foreach (BlockInstance piece in slots)
        {
            if (piece == null || piece.IsConsumed) continue;
            piece.SetPlayable(BoardRules.HasAnyPlacement(board, piece.Table));
        }
    }

    private void EnsureSlots()
    {
        numberOfSpawnSlots = Mathf.Max(1, numberOfSpawnSlots);

        if (slots == null || slots.Length != numberOfSpawnSlots)
        {
            slots = new BlockInstance[numberOfSpawnSlots];
        }

        if (slotCenters == null || slotCenters.Length != numberOfSpawnSlots)
        {
            // Same reason as the grid: derive the tray's place on screen here rather than
            // relying on a layout component having run first.
            slotCenters = new Vector3[numberOfSpawnSlots];

            if (Application.isPlaying && gridManager != null)
            {
                GridGeometry geometry = gridManager.Geometry;
                ScreenLayout fit = ScreenLayout.ForCurrentScreen(geometry, BoardLayout.Active);

                spawnAreaCenter = new Vector3(fit.TrayCenter.x, fit.TrayCenter.y, 0f);
                spacingBetweenBlocks = Mathf.Max(0.01f, fit.SlotSpacing(numberOfSpawnSlots));

                // Slots divide the screen evenly, so the outermost piece has a full slot
                // of room however wide it is. The extent is also capped by the tray band's
                // height, or a tall piece on a wide screen spills out of the band.
                slotExtent = fit.SlotExtent(numberOfSpawnSlots);
                for (int i = 0; i < numberOfSpawnSlots; i++)
                {
                    Vector2 c = fit.SlotCenter(i, numberOfSpawnSlots);
                    slotCenters[i] = new Vector3(c.x, c.y, 0f);
                }
            }
            else
            {
                float span = (numberOfSpawnSlots - 1) * spacingBetweenBlocks;
                for (int i = 0; i < numberOfSpawnSlots; i++)
                {
                    slotCenters[i] = new Vector3(
                        spawnAreaCenter.x - span * 0.5f + i * spacingBetweenBlocks,
                        spawnAreaCenter.y,
                        0f);
                }
            }
        }
    }

    // ---------- catalog / selector ----------

    /// <summary>
    /// Builds the selector on first use. Deferred rather than done in Awake because it
    /// needs the board size, and Unity gives no ordering guarantee between our Awake and
    /// the grid's.
    /// </summary>
    private bool EnsureSelector()
    {
        if (selector != null) return true;
        if (gridManager == null) gridManager = FindAnyObjectByType<GridManager>();
        if (gridManager == null) return false;

        activeCatalog = catalog != null ? catalog : Resources.Load<ShapeCatalog>(ShapeCatalog.ResourcesPath);

        int width = gridManager.GridWidth;
        int height = gridManager.GridHeight;

        List<ShapeCandidate> candidates;
        TraySelectionConfig config;

        if (activeCatalog != null)
        {
            candidates = activeCatalog.BuildCandidates(width, height);
            config = activeCatalog.Selection;

            shapeById = new BlockData[activeCatalog.Entries.Count];
            for (int i = 0; i < shapeById.Length; i++) shapeById[i] = activeCatalog.Entries[i].shape;
        }
        else
        {
            // No catalog authored yet: treat the legacy list as an unweighted one.
            candidates = new List<ShapeCandidate>(availableBlocks.Count);
            shapeById = new BlockData[availableBlocks.Count];

            for (int i = 0; i < availableBlocks.Count; i++)
            {
                BlockData data = availableBlocks[i];
                shapeById[i] = data;
                if (data == null || data.CellCount == 0) continue;

                candidates.Add(new ShapeCandidate(i, data.GetPlacementTable(width, height), 1f));
            }

            config = TraySelectionConfig.Default;
        }

        if (candidates.Count == 0)
        {
            Debug.LogError("[SpawnManager] No usable shapes: assign a ShapeCatalog, place one at " +
                           $"Resources/{ShapeCatalog.ResourcesPath}, or fill Available Blocks.", this);
            return false;
        }

        uint seed = randomSeed != 0 ? unchecked((uint)randomSeed) : unchecked((uint)Environment.TickCount);
        selector = new TraySelector(candidates, config, seed);
        paletteRng = new DeterministicRandom(seed ^ 0x5BF03635u);

        return true;
    }

    // ---------- tray lifecycle ----------

    /// <summary>Fills every empty slot with a fresh piece chosen against the current board.</summary>
    public void RefillTray()
    {
        EnsureSlots();
        if (!EnsureSelector()) return;

        var emptySlots = new List<int>(slots.Length);
        var keep = new List<PlacementTable>(slots.Length);

        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i] == null) emptySlots.Add(i);
            else if (slots[i].Table != null) keep.Add(slots[i].Table);
        }

        if (emptySlots.Count == 0) return;

        ShapeCandidate[] picks = selector.SelectTray(gridManager.Board, emptySlots.Count, keep);

        // Colours are chosen across the whole refill so one tray does not come up as
        // three of the same colour, which reads as a bug even though it is not.
        var usedColors = new List<int>(emptySlots.Count);

        for (int i = 0; i < emptySlots.Count; i++)
        {
            BlockData data = shapeById[picks[i].Id];
            if (data == null) continue;

            int slot = emptySlots[i];
            (Color color, Sprite sprite) = Appearance(data, PickColorIndex(usedColors));
            slots[slot] = CreatePiece(data, slotCenters[slot], color, sprite);
        }

        TrayChanged?.Invoke();
    }

    /// <summary>Removes a played piece from its slot. Does not refill; that is the caller's decision.</summary>
    public void Consume(BlockInstance piece)
    {
        EnsureSlots();
        if (piece == null) return;

        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i] != piece) continue;

            Vector3 where = slotCenters[i];
            Color tint = piece.TintColor;

            slots[i] = null;
            piece.ConsumeAndDestroy();

            PieceConsumed?.Invoke(where, tint);
            TrayChanged?.Invoke();
            return;
        }
    }

    public void ClearTray()
    {
        EnsureSlots();
        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i] == null) continue;
            Destroy(slots[i].gameObject);
            slots[i] = null;
        }

        // Anti-repeat history describes one run, so it must not carry into the next.
        selector?.ResetHistory();
        lastPaletteIndex = -1;

        TrayChanged?.Invoke();
    }

    /// <summary>Compiled tables for the pieces still on offer. Backs the game-over check.</summary>
    public List<PlacementTable> GetTrayTables()
    {
        EnsureSlots();
        var tables = new List<PlacementTable>(slots.Length);
        foreach (BlockInstance piece in slots)
        {
            if (piece != null && piece.Table != null) tables.Add(piece.Table);
        }

        return tables;
    }

    // ---------- colour ----------

    /// <summary>
    /// Picks a palette colour, avoiding both the other pieces in this refill and the
    /// previous one. Falls back to the shape's authored colour when no palette exists.
    /// </summary>
    private int PickColorIndex(List<int> usedThisRefill)
    {
        int paletteSize = activeCatalog != null ? activeCatalog.PaletteSize : 0;
        if (paletteSize == 0) return -1;

        int index = paletteRng.NextInt(paletteSize);

        // Bounded retries: with a small palette a clash may be unavoidable, and looping
        // until it is not would hang.
        for (int attempt = 0; attempt < paletteSize * 2; attempt++)
        {
            if (!usedThisRefill.Contains(index) && index != lastPaletteIndex) break;
            index = paletteRng.NextInt(paletteSize);
        }

        usedThisRefill.Add(index);
        lastPaletteIndex = index;
        return index;
    }

    /// <summary>Colour and sprite for a freshly drawn piece. Index -1 means no palette.</summary>
    private (Color color, Sprite sprite) Appearance(BlockData data, int paletteIndex)
    {
        if (paletteIndex < 0 || activeCatalog == null) return (data.BlockColor, null);

        BlockSkin skin = activeCatalog.Skin;
        return (activeCatalog.ColorAt(paletteIndex), skin != null ? skin.SpriteAt(paletteIndex) : null);
    }

    // ---------- construction ----------

    private BlockInstance CreatePiece(BlockData data, Vector3 slotCenter, Color tint, Sprite sprite)
    {
        if (data == null || gridManager == null) return null;

        GameObject go = blockPrefab != null
            ? Instantiate(blockPrefab, transform)
            : new GameObject($"Block_{data.BlockName}");

        if (blockPrefab == null) go.transform.SetParent(transform, false);
        go.name = $"Block_{data.BlockName}";

        BlockInstance piece = go.GetComponent<BlockInstance>();
        if (piece == null) piece = go.AddComponent<BlockInstance>();

        piece.Initialize(data, gridManager.Geometry, cellPrefab, traySlotScale, tint, sprite, slotExtent);
        piece.SetHomeSlot(slotCenter);
        return piece;
    }

    // ---------- save / restore ----------

    /// <summary>Snapshots which pieces are still on offer, where, and in what colour.</summary>
    public void CaptureTray(out string[] shapeIds, out int[] occupiedSlots, out uint[] colors)
    {
        EnsureSlots();

        var ids = new List<string>(slots.Length);
        var indices = new List<int>(slots.Length);
        var tints = new List<uint>(slots.Length);

        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i] == null || slots[i].BlockData == null) continue;
            ids.Add(slots[i].BlockData.SaveId);
            indices.Add(i);
            tints.Add(ColorPacking.Pack(slots[i].TintColor));
        }

        shapeIds = ids.ToArray();
        occupiedSlots = indices.ToArray();
        colors = tints.ToArray();
    }

    /// <summary>
    /// Rebuilds the tray from a save. Returns false when the save names pieces this
    /// build no longer ships, so the caller can fall back to a fresh tray rather than
    /// resuming with holes in it.
    /// </summary>
    public bool TryRestoreTray(GameSave save)
    {
        EnsureSlots();
        if (save == null || save.trayShapeIds == null || save.traySlots == null) return false;
        if (save.trayShapeIds.Length != save.traySlots.Length) return false;

        // A run saved between consuming the last piece and refilling has an empty tray.
        // That is recoverable -- the caller tops it up -- so it must not cost the board.
        if (save.trayShapeIds.Length == 0)
        {
            ClearTray();
            return true;
        }

        EnsureSelector();

        var resolved = new List<(int slot, BlockData data, Color tint)>(save.trayShapeIds.Length);
        for (int i = 0; i < save.trayShapeIds.Length; i++)
        {
            int slot = save.traySlots[i];
            if ((uint)slot >= (uint)slots.Length) return false;

            BlockData data = FindBlockById(save.trayShapeIds[i]);
            if (data == null) return false;

            resolved.Add((slot, data, ColorPacking.Unpack(save.TrayColorAt(i))));
        }

        ClearTray();
        foreach ((int slot, BlockData data, Color tint) in resolved)
        {
            BlockSkin skin = activeCatalog != null ? activeCatalog.Skin : null;
            slots[slot] = CreatePiece(data, slotCenters[slot], tint,
                skin != null ? skin.SpriteFor(tint) : null);
        }

        TrayChanged?.Invoke();
        return true;
    }

    private BlockData FindBlockById(string saveId)
    {
        if (string.IsNullOrEmpty(saveId)) return null;

        if (shapeById != null)
        {
            foreach (BlockData data in shapeById)
            {
                if (data != null && data.SaveId == saveId) return data;
            }
        }

        foreach (BlockData data in availableBlocks)
        {
            if (data != null && data.SaveId == saveId) return data;
        }

        return null;
    }

    private void OnDestroy()
    {
        TrayChanged = null;
        PieceConsumed = null;
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        int count = Mathf.Max(1, numberOfSpawnSlots);
        float span = (count - 1) * spacingBetweenBlocks;

        Gizmos.color = Color.yellow;
        for (int i = 0; i < count; i++)
        {
            Gizmos.DrawWireCube(
                new Vector3(spawnAreaCenter.x - span * 0.5f + i * spacingBetweenBlocks, spawnAreaCenter.y, 0f),
                Vector3.one * 0.5f);
        }
    }
#endif
}
