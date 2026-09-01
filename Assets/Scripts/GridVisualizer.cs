using UnityEngine;
using BlockBlast.Core;

/// <summary>
/// Draws the board and keeps it in sync with <see cref="GridManager"/>.
///
/// It now subscribes to the grid's change events rather than being poked from inside
/// the grid's mutation methods, which is what previously coupled data changes to
/// rendering and made an animated line clear impossible. Its own copies of cellSize and
/// cellSpacing are gone; layout comes from the grid's <see cref="GridGeometry"/> so
/// there is exactly one definition of where a cell sits.
/// </summary>
public class GridVisualizer : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private GridManager gridManager;
    [SerializeField] private GameObject cellPrefab;

    [Tooltip("Block sprites and board colours. Falls back to Resources/BlockSkin.")]
    [SerializeField] private BlockSkin skin;

    [Header("Board Frame")]
    [SerializeField] private int frameSortingOrder = -2;
    [SerializeField] private int emptySortingOrder = 0;
    [SerializeField] private int filledSortingOrder = 1;

    [Header("Animation")]
    [Tooltip("Stagger between the cells of a landing piece, in seconds.")]
    [SerializeField] private float fillStagger = 0.02f;

    private GameManager gameManager;
    private LineClearEffect clearEffect;
    private float danger;
    private float dangerTarget;

    [Header("Danger")]
    [Tooltip("Board fill fraction at which the frame starts warning. Below this the frame " +
             "stays its resting colour, so the warning means something when it arrives.")]
    [SerializeField, Range(0.3f, 0.95f)] private float dangerThreshold = 0.62f;

    [Tooltip("Hue the frame moves toward as the board fills.")]
    [SerializeField] private Color dangerColor = new Color32(255, 64, 104, 255);

    [Tooltip("Pulses per second at full danger. Slow: a fast strobe reads as a bug.")]
    [SerializeField] private float dangerPulseSpeed = 1.6f;

    [Header("Idle Breath")]
    [Tooltip("How far the resting frame brightness drifts. Deliberately tiny: the calm " +
             "state has to stay calm or a clear stops standing out against it.")]
    [SerializeField, Range(0f, 0.2f)] private float breathDepth = 0.06f;

    [Tooltip("Breaths per second. Slow enough to read as alive rather than as flicker.")]
    [SerializeField] private float breathSpeed = 0.28f;

    [Header("Game Over")]
    [Tooltip("Delay added per board row when the lights go out, so the dark travels up.")]
    [SerializeField] private float gameOverDrainPerRow = 0.035f;

    [Tooltip("How far down a settled cell fades when the run ends. Not to zero -- the " +
             "losing board should stay readable under the end screen.")]
    [SerializeField, Range(0f, 1f)] private float gameOverDrainBrightness = 0.22f;

    [Tooltip("Stagger per cell of distance when a line clears, in seconds.")]
    [SerializeField] private float clearStaggerPerCell = 0.028f;

    private GridCell[] cells;

    // Where the last piece landed, so a clear can radiate outward from the move that
    // caused it rather than wiping in a direction the player had no part in.
    private Vector2 lastFillCenter;

    /// <summary>The board's backing sprite, kept so the clear effect can light it up.</summary>
    public SpriteRenderer Frame { get; private set; }

    /// <summary>Resolved skin, so other effects can pull the same sprites and colours.</summary>
    public BlockSkin Skin => skin;

    private void Awake()
    {
        if (gridManager == null) gridManager = FindAnyObjectByType<GridManager>();
        if (skin == null) skin = Resources.Load<BlockSkin>(BlockSkin.ResourcesPath);

        BuildFrame();
        BuildCells();
    }

    /// <summary>
    /// Puts the board's rounded backing behind the cells. Built here rather than placed in
    /// the scene so it always matches the grid's real size -- an authored frame silently
    /// stops lining up the moment the grid dimensions or cell spacing change.
    /// </summary>
    private void BuildFrame()
    {
        if (skin == null || skin.BoardFrame == null || gridManager == null) return;

        GridGeometry geometry = gridManager.Geometry;
        var go = new GameObject("BoardFrame");
        go.transform.SetParent(transform, false);

        Frame = go.AddComponent<SpriteRenderer>();
        Frame.sprite = skin.BoardFrame;
        // Sliced keeps the rounded corners from smearing as the middle stretches.
        Frame.drawMode = SpriteDrawMode.Sliced;
        // Padding comes from the layout config, not a local field: the screen fit has to
        // account for the same number, and two copies would drift apart.
        float padding = BoardLayout.Active.Sanitized().boardFramePadding * geometry.Pitch;
        Frame.size = new Vector2(
            geometry.TotalWidth + padding * 2f,
            geometry.TotalHeight + padding * 2f);
        // The frame sprite carries brightness, the skin carries hue -- same trick as a
        // tile, so the playfield edge lights up in the palette rather than staying grey.
        Frame.color = skin.BoardBorderColor;
        Frame.sortingOrder = frameSortingOrder;
        if (skin.SpriteMaterial != null) Frame.sharedMaterial = skin.SpriteMaterial;

        go.transform.position = new Vector3(geometry.Center.x, geometry.Center.y, 0f);
    }

    private void OnEnable()
    {
        // The board's own appearance is this component's business, so it listens for the
        // end of the run itself rather than having some other system reach in and dim it.
        if (gameManager == null) gameManager = FindAnyObjectByType<GameManager>();
        if (gameManager != null) gameManager.OnGameStateChanged += HandleGameStateChanged;

        if (gridManager == null) return;

        gridManager.CellsFilled += HandleCellsFilled;
        gridManager.CellsCleared += HandleCellsCleared;
        gridManager.BoardReset += RepaintAll;
        gridManager.BoardReset += RefreshDanger;
    }

    private void OnDisable()
    {
        if (gameManager != null) gameManager.OnGameStateChanged -= HandleGameStateChanged;

        if (gridManager == null) return;

        gridManager.CellsFilled -= HandleCellsFilled;
        gridManager.CellsCleared -= HandleCellsCleared;
        gridManager.BoardReset -= RepaintAll;
        gridManager.BoardReset -= RefreshDanger;
    }

    private void HandleGameStateChanged(bool isGameOver)
    {
        if (isGameOver) PlayGameOverDrain();
    }

    /// <summary>
    /// The colour the frame sits at right now: its resting hue, pushed toward the warning
    /// colour as the board fills, with a slow pulse once the warning is on.
    ///
    /// A property rather than a stored field so there is one answer and it cannot go stale.
    /// LineClearEffect reads this as the value to return to after a flare, which is why the
    /// danger tint survives a line clear instead of being reset to the skin's colour.
    /// </summary>
    public Color FrameRestColor
    {
        get
        {
            Color rest = skin != null ? skin.BoardBorderColor : Color.white;

            // A slow drift so an untouched board is not a still image. It is scaled away as
            // danger rises: the warning pulse and the idle breath are the same channel, and
            // two rhythms on one edge read as neither.
            if (breathDepth > 0f)
            {
                float breath = 1f + breathDepth * (1f - danger) *
                               Mathf.Sin(Time.time * breathSpeed * Mathf.PI * 2f);
                float alpha = rest.a;
                rest *= breath;
                rest.a = alpha;
            }

            if (danger <= 0f) return rest;

            // The pulse rides on top of the blend, not instead of it, so a nearly full
            // board stays visibly warm even at the dim end of the pulse.
            float pulse = 0.75f + 0.25f * Mathf.Sin(Time.time * dangerPulseSpeed * Mathf.PI * 2f);

            Color warned = Color.Lerp(rest, dangerColor * pulse, danger);
            warned.a = rest.a;   // the multiply scales alpha too, and a see-through frame is not the effect
            return warned;
        }
    }

    private void RefreshDanger()
    {
        if (gridManager == null) return;

        float fill = BoardState.PopCount(gridManager.Board.Occupancy) /
                     (float)(gridManager.GridWidth * gridManager.GridHeight);

        // Rescaled so the warning ramps across the range above the threshold rather than
        // switching on: the player should feel it building, not see a light come on.
        dangerTarget = Mathf.Clamp01((fill - dangerThreshold) / Mathf.Max(0.01f, 1f - dangerThreshold));
    }

    private void Update()
    {
        if (Frame == null) return;

        danger = Mathf.MoveTowards(danger, dangerTarget, Time.deltaTime * 1.5f);

        // While a clear is flaring the frame, that animation owns the colour; writing here
        // too would fight it and the flare would stutter.
        if (clearEffect == null) clearEffect = FindAnyObjectByType<LineClearEffect>();
        if (clearEffect != null && clearEffect.IsFlaringFrame) return;

        Frame.color = FrameRestColor;
    }

    private void Start() => RepaintAll();

    private void BuildCells()
    {
        if (gridManager == null)
        {
            Debug.LogError("[GridVisualizer] No GridManager; the board cannot be drawn.", this);
            return;
        }

        if (cellPrefab == null)
        {
            Debug.LogError("[GridVisualizer] Cell prefab not assigned.", this);
            return;
        }

        GridGeometry geometry = gridManager.Geometry;
        cells = new GridCell[BoardState.CellCapacity];

        for (int y = 0; y < geometry.Height; y++)
        {
            for (int x = 0; x < geometry.Width; x++)
            {
                GameObject go = Instantiate(cellPrefab, transform);
                go.transform.position = geometry.CellToWorld(x, y);
                go.transform.localScale = Vector3.one * geometry.CellSize;
                go.name = $"Cell ({x},{y})";

                GridCell cell = go.GetComponent<GridCell>();
                if (cell == null) cell = go.AddComponent<GridCell>();

                cell.Initialize(x, y, skin != null ? skin.EmptyCell : null,
                    skin != null ? skin.EmptyCellColor : Color.white,
                    emptySortingOrder, filledSortingOrder);
                cells[BoardState.BitIndex(x, y)] = cell;
            }
        }
    }

    // ---------- event handlers ----------

    private void HandleCellsFilled(ulong mask, Color color)
    {
        RefreshDanger();
        lastFillCenter = MaskCenter(mask);

        Sprite sprite = skin != null ? skin.SpriteFor(color) : null;

        int index = 0;
        ForEachCell(mask, cell => cell.PlayFill(sprite, color, index++ * fillStagger));
    }

    private void HandleCellsCleared(ulong mask)
    {
        RefreshDanger();
        // Radiate from the piece that completed the line. Delay is measured in cells so
        // the sweep reads at the same speed however large the clear is.
        ForEachCell(mask, cell =>
        {
            float distance = Vector2.Distance(new Vector2(cell.GridX, cell.GridY), lastFillCenter);
            cell.PlayClear(distance * clearStaggerPerCell);
        });
    }

    /// <summary>Average position of the set bits in a mask, in cell coordinates.</summary>
    private static Vector2 MaskCenter(ulong mask)
    {
        if (mask == 0UL) return Vector2.zero;

        float x = 0f, y = 0f;
        int count = 0;

        while (mask != 0UL)
        {
            int index = BoardState.TrailingZeroCount(mask);
            mask &= mask - 1UL;

            x += index & 7;
            y += index >> 3;
            count++;
        }

        return new Vector2(x / count, y / count);
    }

    /// <summary>Repaints every cell from board state. Used on new game and after a save loads.</summary>
    /// <summary>
    /// Puts the board's lights out, bottom row first, when the run ends.
    ///
    /// Fills the pause GameOverScreen already takes before it appears. Without it the
    /// board simply stops and a panel slides over a still-glowing grid, which reads as an
    /// interruption rather than as a loss.
    /// </summary>
    public void PlayGameOverDrain()
    {
        if (cells == null || gridManager == null) return;

        BoardState board = gridManager.Board;
        for (int y = 0; y < board.Height; y++)
        {
            for (int x = 0; x < board.Width; x++)
            {
                GridCell cell = cells[BoardState.BitIndex(x, y)];
                // Staggered by row, so the dark travels up the board instead of the whole
                // thing switching off at once.
                if (cell != null) cell.PlayDrain(y * gameOverDrainPerRow, gameOverDrainBrightness);
            }
        }
    }

    public void RepaintAll()
    {
        if (cells == null || gridManager == null) return;

        BoardState board = gridManager.Board;
        for (int y = 0; y < board.Height; y++)
        {
            for (int x = 0; x < board.Width; x++)
            {
                GridCell cell = cells[BoardState.BitIndex(x, y)];
                if (cell == null) continue;

                if (board.IsEmpty(x, y))
                {
                    cell.SetEmpty();
                    continue;
                }

                Color color = ColorPacking.Unpack(board.GetColor(x, y));
                cell.SetFilled(skin != null ? skin.SpriteFor(color) : null, color);
            }
        }
    }

    /// <summary>Walks the set bits of a mask. Iterating bits beats scanning all 64 cells.</summary>
    private void ForEachCell(ulong mask, System.Action<GridCell> action)
    {
        if (cells == null) return;

        while (mask != 0UL)
        {
            int index = BoardState.TrailingZeroCount(mask);
            mask &= mask - 1UL;

            GridCell cell = cells[index];
            if (cell != null) action(cell);
        }
    }

    public GridCell GetCell(int x, int y)
    {
        if (cells == null || gridManager == null || !gridManager.IsInsideGrid(x, y)) return null;
        return cells[BoardState.BitIndex(x, y)];
    }

    public Vector3 GetCellWorldPosition(int x, int y) =>
        gridManager != null ? gridManager.CellToWorld(x, y) : Vector3.zero;
}
