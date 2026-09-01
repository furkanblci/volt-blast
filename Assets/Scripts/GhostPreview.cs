using System.Collections.Generic;
using UnityEngine;
using BlockBlast.Core;

/// <summary>
/// Shows where a dragged piece would land, and -- the part that actually changes how the
/// game plays -- which rows and columns that drop would clear.
///
/// Without the clear preview the player is doing the line arithmetic in their head on
/// every drag. With it, setting up a double becomes something you can see rather than
/// something you hope for.
///
/// Cells are pooled and only rebuilt when the drag controller reports that the snapped
/// anchor actually changed. The previous version destroyed and re-instantiated them every
/// frame of every drag, and -- because no ghost prefab is assigned in the scene -- leaked
/// a Texture2D per cell per frame on top of the churn.
/// </summary>
public class GhostPreview : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private GridManager gridManager;
    [SerializeField] private GameObject ghostCellPrefab;
    [SerializeField] private BlockSkin skin;

    [Header("Colors")]
    [SerializeField] private Color validColor = new Color32(190, 255, 255, 210);
    [SerializeField] private Color invalidColor = new Color32(255, 70, 110, 150);

    [Tooltip("Wash laid over rows and columns this drop would clear.")]
    [SerializeField] private Color clearHighlightColor = new Color32(120, 245, 255, 130);

    [Header("Draw Order")]
    [SerializeField] private int highlightSortingOrder = 40;
    [SerializeField] private int footprintSortingOrder = 50;

    [Header("Pulse")]
    [Tooltip("How much the clear highlight breathes, so it reads as a promise rather than board state.")]
    [SerializeField, Range(0f, 0.6f)] private float highlightPulse = 0.22f;

    [SerializeField] private float highlightPulseSpeed = 5.5f;

    private readonly List<SpriteRenderer> pool = new List<SpriteRenderer>();

    // Created once and shared by every fallback cell, instead of once per cell per frame.
    private Sprite fallbackSprite;
    private Texture2D fallbackTexture;

    private int activeCount;
    private int highlightCount;

    /// <summary>Rows and columns the currently previewed drop would clear.</summary>
    public int PreviewedLineCount { get; private set; }

    private void Awake()
    {
        if (gridManager == null) gridManager = FindAnyObjectByType<GridManager>();
        if (skin == null) skin = Resources.Load<BlockSkin>(BlockSkin.ResourcesPath);
    }

    /// <summary>
    /// Draws the piece's footprint at an anchor. When the drop is legal, any line it
    /// would complete is washed in underneath. Cells outside the board are skipped.
    /// </summary>
    public void Show(PlacementTable table, Vector2Int anchor, bool valid)
    {
        if (table == null || gridManager == null)
        {
            Hide();
            return;
        }

        GridGeometry geometry = gridManager.Geometry;
        BoardState board = gridManager.Board;
        int used = 0;

        // Highlights go down first so the footprint reads on top of them.
        ulong clearedMask = 0UL;
        if (valid)
        {
            ulong wouldOccupy = board.Occupancy | table.MaskAt(anchor.x, anchor.y);
            LineClearResult preview = BoardRules.FindCompletedLines(wouldOccupy, board.Width, board.Height);
            clearedMask = preview.ClearedMask;
            PreviewedLineCount = preview.LineCount;
        }
        else
        {
            PreviewedLineCount = 0;
        }

        ulong remaining = clearedMask;
        while (remaining != 0UL)
        {
            int index = BoardState.TrailingZeroCount(remaining);
            remaining &= remaining - 1UL;

            SpriteRenderer renderer = Rent(used++);
            Place(renderer, geometry, index & 7, index >> 3, clearHighlightColor, highlightSortingOrder);
        }

        highlightCount = used;

        Color color = valid ? validColor : invalidColor;
        foreach (Vector2Int cell in table.Cells)
        {
            int x = anchor.x + cell.x;
            int y = anchor.y + cell.y;
            if (!geometry.IsInside(x, y)) continue;

            SpriteRenderer renderer = Rent(used++);
            Place(renderer, geometry, x, y, color, footprintSortingOrder);
        }

        Retire(used);
    }

    /// <summary>Parks every cell without destroying it, so the next drag reuses them.</summary>
    public void Hide()
    {
        Retire(0);
        highlightCount = 0;
        PreviewedLineCount = 0;
    }

    private void Update()
    {
        if (highlightCount == 0 || highlightPulse <= 0f) return;

        // Breathe the wash so a pending clear catches the eye without a second effect.
        float wave = (Mathf.Sin(Time.time * highlightPulseSpeed) + 1f) * 0.5f;
        float alpha = clearHighlightColor.a * (1f - highlightPulse + highlightPulse * wave);

        for (int i = 0; i < highlightCount && i < pool.Count; i++)
        {
            if (pool[i] == null) continue;
            Color c = pool[i].color;
            c.a = alpha;
            pool[i].color = c;
        }
    }

    private void Place(SpriteRenderer renderer, GridGeometry geometry, int x, int y, Color color, int order)
    {
        renderer.transform.position = geometry.CellToWorld(x, y);
        renderer.transform.localScale = Vector3.one * geometry.CellSize;
        renderer.color = color;
        renderer.sortingOrder = order;
    }

    private SpriteRenderer Rent(int index)
    {
        while (pool.Count <= index) pool.Add(CreateCell(pool.Count));

        SpriteRenderer renderer = pool[index];
        if (!renderer.gameObject.activeSelf) renderer.gameObject.SetActive(true);
        return renderer;
    }

    private void Retire(int keepCount)
    {
        for (int i = keepCount; i < pool.Count; i++)
        {
            if (pool[i] != null && pool[i].gameObject.activeSelf) pool[i].gameObject.SetActive(false);
        }

        activeCount = keepCount;
    }

    private SpriteRenderer CreateCell(int index)
    {
        GameObject go = ghostCellPrefab != null
            ? Instantiate(ghostCellPrefab, transform)
            : new GameObject($"GhostCell_{index}");

        if (ghostCellPrefab == null) go.transform.SetParent(transform, false);
        go.name = $"GhostCell_{index}";

        SpriteRenderer renderer = go.GetComponent<SpriteRenderer>();
        if (renderer == null) renderer = go.AddComponent<SpriteRenderer>();

        // The loud ring, so a legal drop reads as the board lighting up ahead of the
        // move rather than as a grey stencil laid over it.
        if (skin != null && skin.BlockOutline != null) renderer.sprite = skin.BlockOutline;
        if (renderer.sprite == null) renderer.sprite = GetFallbackSprite();
        if (skin != null && skin.SpriteMaterial != null) renderer.sharedMaterial = skin.SpriteMaterial;

        return renderer;
    }

    private Sprite GetFallbackSprite()
    {
        if (fallbackSprite != null) return fallbackSprite;

        fallbackTexture = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
        fallbackTexture.SetPixel(0, 0, Color.white);
        fallbackTexture.Apply();

        fallbackSprite = Sprite.Create(
            fallbackTexture, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), 1f);
        fallbackSprite.hideFlags = HideFlags.HideAndDontSave;

        return fallbackSprite;
    }

    private void OnDestroy()
    {
        // These are created outside the asset database, so nothing else will collect them.
        if (fallbackSprite != null) Destroy(fallbackSprite);
        if (fallbackTexture != null) Destroy(fallbackTexture);
    }

    /// <summary>How many ghost cells are currently visible. Useful in tests and debugging.</summary>
    public int ActiveCellCount => activeCount;
}
