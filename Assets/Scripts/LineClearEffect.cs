using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using BlockBlast.Core;

/// <summary>
/// Every particle burst on the board: the neon flare a completed line leaves behind --
/// a bright band down the cleared row or column, a scatter of small cubes, and the
/// board's own frame lighting up -- plus the small puff a tray slot gives off when its
/// piece is played.
///
/// They live together because they share one pool. Splitting them would mean two pools
/// sized for their worst case, for effects that never fire at the same moment.
///
/// Modelled on the reference footage rather than invented: there the cleared cells do not
/// simply fade. A bar of light fills the whole line, shatters into dozens of little
/// squares, and the board border flares at the same moment, which is what makes a clear
/// read as an event rather than an erasure.
///
/// Particles are integrated in one Update over a flat array, not one coroutine each. A
/// triple clear throws well over a hundred of them, and a coroutine apiece means that
/// many allocations on the single frame the board is already busiest -- exactly the shape
/// of a hitch on a mid-range phone.
/// </summary>
public class LineClearEffect : MonoBehaviour
{
    [Header("Band")]
    [SerializeField] private float bandDuration = 0.34f;

    [Tooltip("How far the band swells past the line's thickness, in cells.")]
    [SerializeField] private float bandSwell = 0.9f;

    [Tooltip("Brightness multiplier for the band. Above 1 the colour goes HDR and bloom " +
             "turns it into a real flash rather than a pale overlay.")]
    [SerializeField, Range(1f, 4f)] private float bandFlare = 2.6f;

    [Header("Particles")]
    [Tooltip("Cubes thrown per cleared cell.")]
    [SerializeField, Range(0, 12)] private int particlesPerCell = 4;

    [SerializeField] private float particleDuration = 0.62f;
    [SerializeField] private float particleSpeed = 4.2f;
    [SerializeField] private float particleGravity = -9f;

    [Tooltip("Sized to read as the tile shattering into cubes. Much smaller and the burst " +
             "dissolves into white specks that lose the colour entirely.")]
    [SerializeField] private Vector2 particleSizeRange = new Vector2(0.22f, 0.55f);

    [Tooltip("Hard ceiling on live particles. A full-board clear would otherwise spike the " +
             "count far past anything the effect needs to read.")]
    [SerializeField, Range(32, 512)] private int maxParticles = 224;

    [Header("Tray Puff")]
    [Tooltip("Cubes thrown when a piece leaves its tray slot. Zero disables the puff.")]
    [SerializeField, Range(0, 16)] private int trayPuffParticles = 7;

    [Header("Landing Flash")]
    [Tooltip("How long the piece's own silhouette lingers after it lands. Zero disables it.")]
    [SerializeField] private float flashDuration = 0.34f;

    [Tooltip("How far past its own size the silhouette swells before it fades.")]
    [SerializeField] private float flashGrowth = 1.55f;

    [Header("Frame Flare")]
    [SerializeField] private float frameFlashDuration = 0.42f;

    [Tooltip("Extra flash duration per line beyond the first and per combo step, so a " +
             "streak holds the frame lit longer instead of blinking the same way every time.")]
    [SerializeField] private float frameFlashPerStep = 0.09f;

    [Tooltip("Ceiling for the above. Past this the frame stops reading as a flash.")]
    [SerializeField] private float frameFlashMaxDuration = 1.1f;

    [Tooltip("How far a big streak overdrives the flash colour. Above 1 the frame goes " +
             "past the flash hue into white, which is what a long combo should look like.")]
    [SerializeField, Range(1f, 3f)] private float frameFlashMaxOverdrive = 2.1f;

    [Header("Draw Order")]
    [SerializeField] private int bandSortingOrder = 60;
    [SerializeField] private int particleSortingOrder = 70;

    /// <summary>One live cube. A struct in a flat array so a burst allocates nothing.</summary>
    private struct Particle
    {
        public SpriteRenderer Renderer;
        public Vector3 Position;
        public Vector3 Velocity;
        public Color Color;
        public float Age;
        public float Size;
    }

    private GridManager gridManager;
    private GridVisualizer visualizer;
    private GameManager gameManager;
    private SpawnManager spawnManager;
    private BlockSkin skin;

    private readonly List<SpriteRenderer> bandPool = new List<SpriteRenderer>();
    private readonly List<SpriteRenderer> flashPool = new List<SpriteRenderer>();

    private Particle[] particles;
    private int liveParticles;

    // Resolved on demand rather than cached at Awake -- see FrameRestColor.
    private Coroutine frameRoutine;
    private DeterministicRandom rng = new DeterministicRandom(0x9E3779B9u);

    private void Awake()
    {
        gridManager = FindAnyObjectByType<GridManager>();
        visualizer = FindAnyObjectByType<GridVisualizer>();
        spawnManager = FindAnyObjectByType<SpawnManager>();

        // Fall back whenever the visualizer has no skin yet, not just when the visualizer
        // is missing: Unity gives no Awake order, so it may exist but not have resolved
        // its own skin, and taking its null without retrying left this effect silent.
        skin = visualizer != null ? visualizer.Skin : null;
        if (skin == null) skin = Resources.Load<BlockSkin>(BlockSkin.ResourcesPath);

        particles = new Particle[Mathf.Max(32, maxParticles)];
    }

    private void OnEnable()
    {
        if (gridManager != null)
        {
            gridManager.CellsCleared += HandleCellsCleared;
            gridManager.CellsFilled += HandleCellsFilled;
        }
        if (spawnManager != null) spawnManager.PieceConsumed += HandlePieceConsumed;

        if (gameManager == null) gameManager = FindAnyObjectByType<GameManager>();
        if (gameManager != null) gameManager.TurnResolved += HandleTurnResolved;
    }

    private void OnDisable()
    {
        if (gridManager != null)
        {
            gridManager.CellsCleared -= HandleCellsCleared;
            gridManager.CellsFilled -= HandleCellsFilled;
        }
        if (spawnManager != null) spawnManager.PieceConsumed -= HandlePieceConsumed;
        if (gameManager != null) gameManager.TurnResolved -= HandleTurnResolved;
    }

    // ---------- events ----------

    /// <summary>
    /// Lights the whole board edge for the turn, scaled by how much the player pulled off.
    ///
    /// Driven from the resolved turn rather than from the cleared cells, because the score
    /// -- and therefore the combo -- is only settled after the board has been cleared. A
    /// single line gives the plain flash it always did; a streak holds the edge lit longer
    /// and drives it past the flash colour toward white.
    /// </summary>
    private void HandleTurnResolved(LineClearResult cleared, TurnScore score)
    {
        if (!cleared.Any) return;

        int steps = Mathf.Max(0, cleared.LineCount - 1) + Mathf.Max(0, score.Combo - 1);
        FlareFrame(steps);
    }

    private void HandleCellsCleared(ulong mask)
    {
        if (mask == 0UL || gridManager == null || skin == null) return;

        GridGeometry geometry = gridManager.Geometry;
        BoardState board = gridManager.Board;

        // Bands follow whole lines, so derive which rows and columns the mask completed
        // rather than drawing one band per cell.
        for (int y = 0; y < board.Height; y++)
        {
            ulong row = BoardState.RowMask(y, board.Width);
            if ((mask & row) == row) SpawnBand(geometry, horizontal: true, index: y);
        }

        for (int x = 0; x < board.Width; x++)
        {
            ulong column = BoardState.ColumnMask(x, board.Height);
            if ((mask & column) == column) SpawnBand(geometry, horizontal: false, index: x);
        }

        SpawnBurst(geometry, mask);
        // The frame is flared from TurnResolved, not here: CellsCleared fires before the
        // score is settled, so a combo read at this point would be one turn stale.
    }

    /// <summary>
    /// The cells popping tells the player something arrived; the flash tells them what,
    /// by lighting up the shape they just placed.
    /// </summary>
    private void HandleCellsFilled(ulong mask, Color color)
    {
        if (mask == 0UL || gridManager == null || skin == null) return;

        ShapeFlash(mask, color);
    }

    /// <summary>A short puff in the piece's own colour, so the slot does not just blink empty.</summary>
    private void HandlePieceConsumed(Vector3 where, Color tint)
    {
        if (trayPuffParticles <= 0 || skin == null || skin.Particle == null || gridManager == null) return;

        GridGeometry geometry = gridManager.Geometry;
        for (int i = 0; i < trayPuffParticles; i++) SpawnParticle(where, geometry.CellSize, tint);
    }

    /// <summary>
    /// Flashes the footprint of the piece that just landed: one swelling, fading copy of
    /// every cell it filled.
    ///
    /// Both earlier attempts drew a shape the piece does not have -- first the block
    /// outline, which is a rounded rectangle, then a circle. Either one over an L-piece or
    /// a long bar reads as a stray graphic that happens to be near the move rather than as
    /// that move landing. The union of per-cell sprites *is* the piece, so what swells and
    /// fades is the thing the player just put down.
    ///
    /// Tinted white, not with the colour: these are the pre-shaded block sprites, which
    /// already carry their hue. Multiplying one by its own colour squares it and lands
    /// near black.
    /// </summary>
    private void ShapeFlash(ulong mask, Color color)
    {
        Sprite sprite = skin != null ? skin.SpriteFor(color) : null;
        if (sprite == null || flashDuration <= 0f || gridManager == null) return;

        GridGeometry geometry = gridManager.Geometry;

        ulong walk = mask;
        while (walk != 0UL)
        {
            int bit = BoardState.TrailingZeroCount(walk);
            walk &= walk - 1UL;

            SpriteRenderer flash = Rent(flashPool, "LandingFlash", sprite, particleSortingOrder + 1);
            flash.transform.position = geometry.CellToWorld(bit & 7, bit >> 3);
            StartCoroutine(FlashRoutine(flash, geometry.CellSize));
        }
    }

    private IEnumerator FlashRoutine(SpriteRenderer flash, float cellSize)
    {
        float elapsed = 0f;
        while (elapsed < flashDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / flashDuration);

            flash.transform.localScale =
                Vector3.one * cellSize * Mathf.LerpUnclamped(1f, flashGrowth, Easing.OutCubic(t));

            // Overdriven at the start so bloom flares it, easing back as it fades out.
            Color lit = Color.white * Mathf.LerpUnclamped(1.9f, 1f, t);
            lit.a = 1f - Easing.OutQuad(t);
            flash.color = lit;
            yield return null;
        }

        flash.gameObject.SetActive(false);
    }

    // ---------- band ----------

    private void SpawnBand(GridGeometry geometry, bool horizontal, int index)
    {
        SpriteRenderer band = RentBand();

        band.transform.position = horizontal
            ? new Vector3(geometry.Center.x, geometry.CellToWorld(0, index).y, 0f)
            : new Vector3(geometry.CellToWorld(index, 0).x, geometry.Center.y, 0f);

        StartCoroutine(BandRoutine(band, geometry, horizontal));
    }

    private IEnumerator BandRoutine(SpriteRenderer band, GridGeometry geometry, bool horizontal)
    {
        float length = horizontal ? geometry.TotalWidth : geometry.TotalHeight;
        Color color = skin.ClearFlashColor;
        float elapsed = 0f;

        while (elapsed < bandDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / bandDuration);

            // Snaps to full length immediately, then swells outward as it fades: the light
            // is the event, the spread is the aftermath.
            float thickness = geometry.CellSize * (1f + bandSwell * Easing.OutQuad(t));
            band.transform.localScale = horizontal
                ? new Vector3(length * 1.05f, thickness, 1f)
                : new Vector3(thickness, length * 1.05f, 1f);

            // Brightest at the instant of the clear, falling away as it spreads.
            float flare = Mathf.LerpUnclamped(bandFlare, 1f, Easing.OutQuad(t));
            Color lit = color * flare;
            lit.a = 1f - Easing.InQuad(t);
            band.color = lit;
            yield return null;
        }

        band.gameObject.SetActive(false);
    }

    private SpriteRenderer RentBand() => Rent(bandPool, "ClearBand", skin.Glow, bandSortingOrder);

    /// <summary>Shared pool rental for the one-off sprites bands and flashes need.</summary>
    private SpriteRenderer Rent(List<SpriteRenderer> pool, string label, Sprite sprite, int sortingOrder)
    {
        for (int i = 0; i < pool.Count; i++)
        {
            if (pool[i] != null && !pool[i].gameObject.activeSelf)
            {
                // Reassigned every time: this pool is shared by callers whose sprite
                // varies, and a reused renderer would otherwise keep the sprite it was
                // created with -- the previous piece's colour.
                pool[i].sprite = sprite;
                pool[i].gameObject.SetActive(true);
                return pool[i];
            }
        }

        SpriteRenderer created = CreateRenderer($"{label}_{pool.Count}", sprite, sortingOrder);
        pool.Add(created);

        // CreateRenderer leaves it inactive, which is right for a pool but made this
        // method hand out a renderer it had not claimed: a second rent in the same frame
        // found it still inactive and returned the same object. Multi-line clears drew one
        // band instead of one per line, and a landing lit fewer cells than the piece has.
        created.gameObject.SetActive(true);
        return created;
    }

    // ---------- particles ----------

    private void SpawnBurst(GridGeometry geometry, ulong mask)
    {
        if (particlesPerCell <= 0 || skin.Particle == null) return;

        while (mask != 0UL)
        {
            int bit = BoardState.TrailingZeroCount(mask);
            mask &= mask - 1UL;

            Vector3 origin = geometry.CellToWorld(bit & 7, bit >> 3);
            for (int i = 0; i < particlesPerCell; i++)
                SpawnParticle(origin, geometry.CellSize, skin.ClearFlashColor);
        }
    }

    /// <summary>
    /// Adds one cube. Silently drops the request once the array is full rather than
    /// growing it: past a couple of hundred the burst reads the same and the cost does not.
    /// </summary>
    private void SpawnParticle(Vector3 origin, float cellSize, Color tint)
    {
        if (particles == null || liveParticles >= particles.Length) return;

        int index = liveParticles++;
        SpriteRenderer renderer = particles[index].Renderer;
        if (renderer == null)
        {
            renderer = CreateRenderer($"ClearParticle_{index}", skin.Particle, particleSortingOrder);
            particles[index].Renderer = renderer;
        }

        // Start scattered inside the cell rather than all from its centre, or the burst
        // reads as a starburst instead of a shattering tile.
        Vector3 position = origin + new Vector3(
            (rng.NextFloat() - 0.5f) * cellSize,
            (rng.NextFloat() - 0.5f) * cellSize,
            0f);

        float angle = rng.NextFloat() * Mathf.PI * 2f;
        float speed = particleSpeed * (0.35f + rng.NextFloat());

        particles[index].Position = position;
        particles[index].Velocity = new Vector3(
            Mathf.Cos(angle) * speed * 0.6f, Mathf.Abs(Mathf.Sin(angle)) * speed, 0f);
        particles[index].Color = tint;
        particles[index].Age = 0f;
        particles[index].Size = Mathf.Lerp(particleSizeRange.x, particleSizeRange.y, rng.NextFloat());

        renderer.gameObject.SetActive(true);
        renderer.transform.position = position;
        renderer.transform.localScale = Vector3.one * particles[index].Size;
        renderer.color = tint;
    }

    private void Update()
    {
        if (liveParticles == 0) return;

        float dt = Time.deltaTime;

        for (int i = liveParticles - 1; i >= 0; i--)
        {
            particles[i].Age += dt;
            float t = particles[i].Age / particleDuration;

            if (t >= 1f)
            {
                Retire(i);
                continue;
            }

            particles[i].Velocity.y += particleGravity * dt;
            particles[i].Position += particles[i].Velocity * dt;

            Transform tr = particles[i].Renderer.transform;
            tr.position = particles[i].Position;
            tr.localScale = Vector3.one * particles[i].Size * (1f - Easing.InQuad(t) * 0.7f);

            Color c = particles[i].Color * Mathf.LerpUnclamped(1.8f, 1f, t);
            c.a = 1f - Easing.InQuad(t);
            particles[i].Renderer.color = c;
        }
    }

    /// <summary>
    /// Retires a particle by swapping the last live one into its place, so the live range
    /// stays contiguous and no per-particle "is alive" test is needed.
    /// </summary>
    private void Retire(int index)
    {
        particles[index].Renderer.gameObject.SetActive(false);

        int last = liveParticles - 1;
        if (index != last)
        {
            // Swap the whole record, renderers included, so every slot keeps its own.
            Particle moved = particles[last];
            particles[last] = particles[index];
            particles[index] = moved;
        }

        liveParticles = last;
    }

    // ---------- frame ----------

    /// <summary>
    /// The colour the frame returns to after a flare, taken from the skin every time.
    ///
    /// This used to be captured once in Awake from the frame renderer's current colour,
    /// with a comment claiming the visualizer had already tinted it. It had not: this
    /// component is created by GameBootstrap at execution order -1000, so its Awake runs
    /// before GridVisualizer builds the frame at all. The cached value stayed at its
    /// white default, and the first line clear restored the board's neon edge to white
    /// and left it there for the rest of the run.
    ///
    /// Asking GridVisualizer each time removes the ordering dependency rather than papering
    /// over it, and it is also the only way the danger tint survives a clear: the frame's
    /// resting colour is no longer a constant from the skin but a live blend of the skin
    /// colour and how full the board is.
    /// </summary>
    private Color FrameRestColor =>
        visualizer != null ? visualizer.FrameRestColor
                           : (skin != null ? skin.BoardBorderColor : Color.white);

    /// <summary>
    /// Whether a clear is currently driving the frame's colour. GridVisualizer checks this
    /// before writing its own resting tint, so the two never fight over the same renderer.
    /// </summary>
    public bool IsFlaringFrame => frameRoutine != null;

    private void FlareFrame(int steps)
    {
        if (visualizer == null || visualizer.Frame == null) return;

        if (frameRoutine != null)
        {
            StopCoroutine(frameRoutine);
            visualizer.Frame.color = FrameRestColor;
        }

        float duration = Mathf.Min(frameFlashMaxDuration, frameFlashDuration + steps * frameFlashPerStep);
        // Four steps is a genuinely big turn; past that the escalation is already at its top.
        float overdrive = Mathf.Lerp(1f, frameFlashMaxOverdrive, Mathf.Clamp01(steps / 4f));

        frameRoutine = StartCoroutine(FrameRoutine(visualizer.Frame, duration, overdrive));
    }

    private IEnumerator FrameRoutine(SpriteRenderer frame, float duration, float overdrive)
    {
        Color rest = FrameRestColor;
        Color flash = skin.ClearFlashColor * overdrive;
        flash.a = skin.ClearFlashColor.a;   // the multiply scales alpha too

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;

            // Rest is re-read each frame so a clear that drops the board out of the danger
            // band eases back to the *new* resting colour rather than to the old warning one.
            frame.color = Color.LerpUnclamped(
                FrameRestColor, flash, Easing.Pulse(elapsed / duration, 0.18f));
            yield return null;
        }

        frame.color = FrameRestColor;
        frameRoutine = null;
    }

    // ---------- construction ----------

    private SpriteRenderer CreateRenderer(string label, Sprite sprite, int sortingOrder)
    {
        var go = new GameObject(label);
        go.transform.SetParent(transform, false);

        var renderer = go.AddComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.sortingOrder = sortingOrder;
        if (skin != null && skin.SpriteMaterial != null) renderer.sharedMaterial = skin.SpriteMaterial;
        go.SetActive(false);

        return renderer;
    }

    /// <summary>Live particle count. Exposed for tests and for spotting a leak in the pool.</summary>
    public int LiveParticleCount => liveParticles;
}
