using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using BlockBlast.Core;

/// <summary>
/// One loose piece sitting in the tray or under the player's finger.
///
/// This type is now presentation only. It used to run its own Update loop polling
/// Input and raycasting -- three tray pieces meant three independent input handlers
/// racing for the same tap. Input is centralised in <see cref="BlockDragController"/>;
/// this component just knows how to draw itself, where its home slot is, and how to
/// report which world points it covers.
///
/// The transform's position is the world position of the piece's cell (0,0) centre.
/// Holding that invariant is what lets placement snap the piece itself rather than the
/// pointer, so the ghost, the visuals and the committed cells always agree.
/// </summary>
public class BlockInstance : MonoBehaviour
{
    [Header("Configuration")]
    [SerializeField] private BlockData blockData;
    [SerializeField] private GameObject cellPrefab;

    [Header("Drag Feel")]
    [Tooltip("Brightness while held. Above 1 the rim crosses the bloom threshold, so the " +
             "piece in hand is lit rather than merely large. It used to be faded to 0.9 " +
             "alpha, which made the one thing the player is touching the dimmest thing on " +
             "the board.")]
    [SerializeField, Range(1f, 2f)] private float dragGlow = 1.28f;

    [Tooltip("Brightness for a piece that has nowhere left to go. A neon tile that dims " +
             "reads as switched off, which is exactly what a dead piece is -- and it saves " +
             "the player scanning the whole board against every piece to find that out.")]
    [SerializeField, Range(0.15f, 1f)] private float unplayableDim = 0.38f;

    [Header("Drag Trail")]
    [Tooltip("Ghosts left behind a moving piece. Zero disables the trail.")]
    [SerializeField, Range(0, 8)] private int trailLength = 4;

    [Tooltip("Distance the piece must travel before it drops another ghost, in cells. " +
             "Spacing by distance and not by time means a slow, careful placement leaves " +
             "no trail at all -- only fast movement smears.")]
    [SerializeField] private float trailSpacingInCells = 0.42f;

    [Tooltip("How long one ghost takes to fade.")]
    [SerializeField] private float trailFade = 0.22f;

    [Tooltip("Opacity of the freshest ghost.")]
    [SerializeField, Range(0f, 1f)] private float trailAlpha = 0.34f;

    [Tooltip("How far above the pointer the piece floats, in cells, so a finger does not cover it.")]
    [SerializeField] private float liftInCells = 1.2f;

    [Tooltip("How quickly the piece catches up to the pointer. Higher is stiffer; " +
             "0 disables smoothing and the piece tracks exactly.")]
    [SerializeField] private float dragResponsiveness = 26f;

    [Tooltip("Time the piece takes to grow from tray size to board size when picked up.")]
    [SerializeField] private float pickupDuration = 0.11f;

    [Header("Animation")]
    [SerializeField] private float returnDuration = 0.18f;
    [SerializeField] private AnimationCurve returnEase = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    private const int TraySortingOrder = 10;
    private const int DragSortingOrder = 200;

    private readonly List<SpriteRenderer> cellRenderers = new List<SpriteRenderer>();

    // The colours the cells were built with. Alpha and brightness are applied on top of
    // these rather than to whatever the renderer happens to hold, so a drag interrupted
    // mid-flare cannot bake its overdriven tint in as the piece's new resting colour.
    private readonly List<Color> cellBaseColors = new List<Color>();

    private readonly List<Transform> trailPool = new List<Transform>();
    private Transform trailRoot;
    private Vector3 lastTrailPosition;
    private float tintAlpha = 1f;
    private bool playable = true;

    private GridGeometry geometry;
    private PlacementTable table;
    private Vector3 homePosition;
    private float trayScale = 1f;
    private Sprite blockSprite;
    private Coroutine returnRoutine;
    private Coroutine pickupRoutine;

    public BlockData BlockData => blockData;

    /// <summary>
    /// Colour this piece paints onto the board. Comes from the catalog palette rather
    /// than the shape asset, so colour stays decoration instead of labelling each shape.
    /// </summary>
    public Color TintColor { get; private set; } = Color.white;

    /// <summary>Compiled placement masks for this piece on the current board.</summary>
    public PlacementTable Table => table;

    /// <summary>Slot position the piece springs back to when a drop is rejected.</summary>
    public Vector3 HomePosition => homePosition;

    public bool IsDragging { get; private set; }

    /// <summary>Set once the piece has been committed to the board and is on its way out.</summary>
    public bool IsConsumed { get; private set; }

    /// <summary>
    /// Builds the piece for a board. Called by the spawner instead of Start, so a piece
    /// is never briefly alive with no data -- which is what forced the old code to
    /// re-resolve references and regenerate visuals from several places.
    /// </summary>
    public void Initialize(
        BlockData data, GridGeometry gridGeometry, GameObject cellVisualPrefab, float slotScale,
        Color tint, Sprite sprite, float maxSlotExtent = 0f)
    {
        blockData = data;
        geometry = gridGeometry;
        TintColor = tint;
        blockSprite = sprite;
        if (cellVisualPrefab != null) cellPrefab = cellVisualPrefab;

        table = data != null ? data.GetPlacementTable(gridGeometry.Width, gridGeometry.Height) : null;
        trayScale = FitScale(slotScale, maxSlotExtent);

        BuildVisuals();
        SetScale(trayScale);
        SetSortingOrder(TraySortingOrder);
    }

    /// <summary>
    /// Shrinks the piece until it fits its slot. A single fixed tray scale only suits the
    /// shape it was chosen for: at a scale that flatters a 2x2, a five-cell line runs off
    /// the side of the screen. Zero extent keeps the preferred scale, for callers that do
    /// not care about slot width.
    /// </summary>
    private float FitScale(float preferred, float maxSlotExtent)
    {
        preferred = Mathf.Max(0.01f, preferred);
        if (maxSlotExtent <= 0f || table == null || table.CellCount == 0) return preferred;

        float widest = Mathf.Max(table.Width, table.Height) * geometry.Pitch;
        if (widest <= 0f) return preferred;

        return Mathf.Min(preferred, maxSlotExtent / widest);
    }

    /// <summary>
    /// Places the piece in its tray slot, centred on the slot rather than hung off its
    /// pivot -- otherwise a 4-wide piece spills to the right of its slot and overlaps
    /// the next one.
    /// </summary>
    public void SetHomeSlot(Vector3 slotCenter)
    {
        homePosition = slotCenter - LocalBoundsCenter * trayScale;
        transform.position = homePosition;
    }

    // ---------- geometry ----------

    /// <summary>Offset from the pivot to the middle of the piece's bounding box, unscaled.</summary>
    private Vector3 LocalBoundsCenter
    {
        get
        {
            if (table == null || table.CellCount == 0) return Vector3.zero;
            return new Vector3(
                (table.Width - 1) * 0.5f * geometry.Pitch,
                (table.Height - 1) * 0.5f * geometry.Pitch,
                0f);
        }
    }

    /// <summary>
    /// Board cell the piece would anchor to right now. The pivot is the piece's own
    /// transform, so this is exactly what the player sees.
    /// </summary>
    public Vector2Int CurrentAnchor => geometry.WorldToNearestCell(transform.position);

    /// <summary>
    /// Cell-accurate hit test with a small margin, so a tap just outside the piece still
    /// grabs it without the margin bleeding far enough to steal the neighbouring slot.
    /// </summary>
    public bool ContainsWorldPoint(Vector3 world, float marginInCells = 0.35f)
    {
        if (table == null || table.CellCount == 0) return false;

        float scaledPitch = geometry.Pitch * transform.localScale.x;
        if (scaledPitch <= 0f) return false;

        Vector3 local = world - transform.position;
        float half = geometry.CellSize * transform.localScale.x * 0.5f + marginInCells * scaledPitch;

        for (int i = 0; i < table.Cells.Length; i++)
        {
            Vector2Int cell = table.Cells[i];
            float dx = Mathf.Abs(local.x - cell.x * scaledPitch);
            float dy = Mathf.Abs(local.y - cell.y * scaledPitch);
            if (dx <= half && dy <= half) return true;
        }

        return false;
    }

    // ---------- drag lifecycle ----------

    /// <summary>
    /// Lifts the piece to board scale and returns the offset to keep between the pointer
    /// and the pivot for the rest of the drag.
    /// </summary>
    public Vector3 BeginDrag(Vector3 pointerWorld)
    {
        StopReturn();

        IsDragging = true;
        SetAlpha(1f);
        ApplyTint();
        SetSortingOrder(DragSortingOrder);

        // Anchor the piece's centre above the pointer rather than preserving the exact
        // grab point: the piece grows from tray scale to board scale on pick-up, so the
        // original contact point is no longer where the player put their finger.
        Vector3 lift = new Vector3(0f, liftInCells * geometry.Pitch, 0f);
        Vector3 pivotTarget = pointerWorld + lift - LocalBoundsCenter;

        transform.position = pivotTarget;
        lastTrailPosition = pivotTarget;
        StartPickup();

        return pivotTarget - pointerWorld;
    }

    /// <summary>
    /// Moves the piece toward the pointer. The follow is smoothed rather than exact so the
    /// piece has some weight; because the ghost and the drop anchor both read the piece's
    /// own transform, a lagging piece still lands exactly where it is drawn.
    /// </summary>
    public void DragTo(Vector3 pointerWorld, Vector3 grabOffset)
    {
        Vector3 target = pointerWorld + grabOffset;
        target.z = 0f;

        EmitTrail();

        if (dragResponsiveness <= 0f)
        {
            transform.position = target;
            return;
        }

        // Exponential smoothing, so the feel does not change with frame rate.
        float t = 1f - Mathf.Exp(-dragResponsiveness * Time.deltaTime);
        transform.position = Vector3.Lerp(transform.position, target, t);
    }

    private void StartPickup()
    {
        if (pickupRoutine != null) StopCoroutine(pickupRoutine);

        if (!isActiveAndEnabled || pickupDuration <= 0f)
        {
            SetScale(1f);
            return;
        }

        pickupRoutine = StartCoroutine(PickupRoutine());
    }

    private IEnumerator PickupRoutine()
    {
        float from = transform.localScale.x;
        float elapsed = 0f;

        while (elapsed < pickupDuration)
        {
            elapsed += Time.deltaTime;
            SetScale(Mathf.LerpUnclamped(from, 1f, Easing.OutBack(elapsed / pickupDuration)));
            yield return null;
        }

        SetScale(1f);
        pickupRoutine = null;
    }

    /// <summary>Ends a drag without committing. The piece stays where it is until told to go home.</summary>
    public void EndDrag()
    {
        IsDragging = false;
        SetAlpha(1f);
        ApplyTint();
        ClearTrail();
        SetSortingOrder(TraySortingOrder);
    }

    /// <summary>Springs back to the tray slot after a rejected drop.</summary>
    public void ReturnHome()
    {
        StopReturn();
        // The pick-up growth is still running if the drop was quick; leaving it would
        // fight the shrink back to tray size.
        StopPickup();
        SetScale(trayScale);

        if (!isActiveAndEnabled)
        {
            transform.position = homePosition;
            return;
        }

        returnRoutine = StartCoroutine(ReturnRoutine());
    }

    private IEnumerator ReturnRoutine()
    {
        Vector3 start = transform.position;
        float elapsed = 0f;

        while (elapsed < returnDuration)
        {
            elapsed += Time.deltaTime;
            transform.position = Vector3.LerpUnclamped(
                start, homePosition, returnEase.Evaluate(Mathf.Clamp01(elapsed / returnDuration)));
            yield return null;
        }

        transform.position = homePosition;
        returnRoutine = null;
    }

    private void StopReturn()
    {
        if (returnRoutine == null) return;
        StopCoroutine(returnRoutine);
        returnRoutine = null;
    }

    private void StopPickup()
    {
        if (pickupRoutine == null) return;
        StopCoroutine(pickupRoutine);
        pickupRoutine = null;
    }

    /// <summary>Marks the piece as spent and removes it. The board owns those cells now.</summary>
    public void ConsumeAndDestroy()
    {
        IsConsumed = true;
        StopReturn();
        Destroy(gameObject);
    }

    // ---------- visuals ----------

    private void BuildVisuals()
    {
        ClearVisuals();

        if (table == null || table.CellCount == 0)
        {
            Debug.LogWarning($"[{name}] No shape to draw.", this);
            return;
        }

        if (cellPrefab == null)
        {
            Debug.LogError($"[{name}] Cell prefab missing; piece will be invisible.", this);
            return;
        }

        // White when the sprite is pre-shaded; the raw colour only for the untextured
        // fallback. Tinting a finished sprite with its own colour squares it.
        Color color = blockSprite != null ? Color.white : TintColor;

        foreach (Vector2Int cell in table.Cells)
        {
            GameObject visual = Instantiate(cellPrefab, transform);
            visual.name = $"Cell ({cell.x},{cell.y})";
            // Pitch, not CellSize: laying cells out on size alone makes the piece
            // narrower than the grid cells it is meant to cover.
            visual.transform.localPosition = geometry.ShapeCellOffset(cell);
            visual.transform.localScale = Vector3.one * geometry.CellSize;

            var renderer = visual.GetComponent<SpriteRenderer>();
            if (renderer != null)
            {
                // A null sprite leaves the prefab's own, so a missing skin degrades to
                // plain tinted squares rather than to invisible pieces.
                if (blockSprite != null) renderer.sprite = blockSprite;
                renderer.color = color;
                cellRenderers.Add(renderer);
                cellBaseColors.Add(color);
            }
        }
    }

    private void ClearVisuals()
    {
        cellRenderers.Clear();
        cellBaseColors.Clear();
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            GameObject child = transform.GetChild(i).gameObject;
            if (Application.isPlaying) Destroy(child);
            else DestroyImmediate(child);
        }
    }

    private void SetScale(float scale) => transform.localScale = Vector3.one * scale;

    // ---------- drag trail ----------

    /// <summary>
    /// Drops a fading copy of the piece behind it as it moves.
    ///
    /// A ghost mirrors every cell, not one square at the pivot: a lone block trailing an
    /// L-piece is a shape the piece does not have, which is the same mistake the landing
    /// effect made twice before it was fixed.
    ///
    /// Spaced by distance travelled rather than by time, so lining a piece up carefully
    /// leaves nothing behind and only a fast sweep smears. That matters because the board
    /// is also drawing the ghost footprint of where the piece would land, and a trail that
    /// fired during slow, precise movement would compete with the read the player needs most.
    /// </summary>
    private void EmitTrail()
    {
        if (trailLength <= 0 || cellRenderers.Count == 0) return;

        // GridGeometry is a struct; an uninitialised one has a zero pitch, which would make
        // the spacing test always pass and drop a ghost every frame.
        float spacing = trailSpacingInCells * geometry.Pitch;
        if (spacing <= 0f) return;
        if ((transform.position - lastTrailPosition).sqrMagnitude < spacing * spacing) return;

        lastTrailPosition = transform.position;

        Transform ghost = RentGhost();
        if (ghost == null) return;

        ghost.SetPositionAndRotation(transform.position, transform.rotation);
        ghost.localScale = transform.localScale;
        StartCoroutine(TrailRoutine(ghost));
    }

    private Transform RentGhost()
    {
        foreach (Transform ghost in trailPool)
            if (ghost != null && !ghost.gameObject.activeSelf)
            {
                ghost.gameObject.SetActive(true);
                return ghost;
            }

        if (trailPool.Count >= trailLength) return null;

        if (trailRoot == null)
        {
            // Parented to the scene, not to the piece: a ghost marks where the piece *was*,
            // so it must not follow it.
            trailRoot = new GameObject(name + " Trail").transform;
        }

        var go = new GameObject("Ghost " + trailPool.Count);
        go.transform.SetParent(trailRoot, false);

        // One child per cell, matching the piece's own layout.
        for (int i = 0; i < cellRenderers.Count; i++)
        {
            SpriteRenderer source = cellRenderers[i];
            if (source == null) continue;

            var cell = new GameObject("Cell " + i);
            cell.transform.SetParent(go.transform, false);
            cell.transform.localPosition = source.transform.localPosition;
            cell.transform.localScale = source.transform.localScale;

            var renderer = cell.AddComponent<SpriteRenderer>();
            renderer.sprite = source.sprite;
            renderer.sharedMaterial = source.sharedMaterial;
            renderer.sortingOrder = DragSortingOrder - 1;
        }

        trailPool.Add(go.transform);
        return go.transform;
    }

    private IEnumerator TrailRoutine(Transform ghost)
    {
        var renderers = ghost.GetComponentsInChildren<SpriteRenderer>();
        float elapsed = 0f;

        while (elapsed < trailFade)
        {
            elapsed += Time.deltaTime;
            float a = trailAlpha * (1f - Easing.OutQuad(Mathf.Clamp01(elapsed / trailFade)));

            for (int i = 0; i < renderers.Length; i++)
            {
                Color c = i < cellBaseColors.Count ? cellBaseColors[i] : Color.white;
                c.a = a;
                renderers[i].color = c;
            }

            yield return null;
        }

        ghost.gameObject.SetActive(false);
    }

    private void ClearTrail()
    {
        foreach (Transform ghost in trailPool)
            if (ghost != null) ghost.gameObject.SetActive(false);
    }

    private void OnDestroy()
    {
        if (trailRoot != null) Destroy(trailRoot.gameObject);
    }

    private void SetAlpha(float alpha)
    {
        tintAlpha = alpha;
        ApplyTint();
    }

    /// <summary>
    /// Marks whether this piece still fits anywhere on the board. Dead pieces dim.
    /// </summary>
    public void SetPlayable(bool value)
    {
        if (playable == value) return;

        playable = value;
        ApplyTint();
    }

    /// <summary>
    /// Brightness is derived, not stored, so the two things that drive it cannot disagree.
    /// Held beats dead: a piece the player has picked up is lit even if it has nowhere to
    /// land, because dimming what is under their finger reads as the game refusing input.
    /// </summary>
    private float Brightness => IsDragging ? dragGlow : (playable ? 1f : unplayableDim);

    private void ApplyTint()
    {
        float brightness = Brightness;

        for (int i = 0; i < cellRenderers.Count && i < cellBaseColors.Count; i++)
        {
            SpriteRenderer renderer = cellRenderers[i];
            if (renderer == null) continue;

            Color c = cellBaseColors[i] * brightness;
            c.a = tintAlpha;   // after the multiply, which would scale alpha too
            renderer.color = c;
        }
    }

    private void SetSortingOrder(int order)
    {
        foreach (SpriteRenderer renderer in cellRenderers)
        {
            if (renderer != null) renderer.sortingOrder = order;
        }
    }
}
