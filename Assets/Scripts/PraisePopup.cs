using System.Collections;
using TMPro;
using UnityEngine;
using BlockBlast.Core;

/// <summary>
/// The "60 / Great!" that pops over the board when a clear lands.
///
/// This is the game's main feedback for *how well* a move went. The score readout says
/// what the total is; this says what the move was worth, in the moment, where the player
/// is already looking. The reference shows the points in white over a warm glow with the
/// praise word beneath in cyan, drifting up and fading in well under a second.
///
/// Which word appears is driven by the turn's real numbers, so the praise escalates with
/// the play rather than being decorative.
/// </summary>
public class PraisePopup : MonoBehaviour
{
    [Header("Words")]
    [Tooltip("Praise ladder, weakest first. The tier is picked by lines cleared plus combo.")]
    //
    // These were once neon-themed nouns -- Spark, Flash, Blaze, Surge, Overload -- which
    // named the effect instead of praising the move. A player who clears two lines and is
    // told "Blaze!" has to work out whether that is good news. The point of this popup is
    // to say "you did well, and this well", so the ladder is plain escalating praise that
    // needs no decoding. The neon belongs in how it looks, not in what it says.
    [SerializeField] private string[] words = { "GOOD!", "GREAT!", "EXCELLENT!", "AMAZING!", "INCREDIBLE!" };

    [Header("Timing")]
    [SerializeField] private float holdDuration = 0.34f;
    [SerializeField] private float fadeDuration = 0.36f;

    [Tooltip("How far the popup drifts upward over its life, in cells.")]
    [SerializeField] private float riseInCells = 0.9f;

    [Tooltip("How far above the cleared cells the popup starts, in cells. Sitting on the " +
             "clear itself puts the text over the brightest thing on screen.")]
    [SerializeField] private float liftAboveClearInCells = 1.9f;

    [Header("Style")]
    // The number is the substance and the word is the flourish, so the number is the
    // bigger of the two. It used to be the other way round, which left the praise
    // shouting over the thing the player actually earned.
    [SerializeField] private float pointsFontSize = 13f;
    // Bumped back up from 8: the word has to hold its own over a board that is at its
    // brightest at exactly the moment it appears, and at 8 it read as a caption.
    [SerializeField] private float wordFontSize = 10.5f;
    [SerializeField] private Color pointsColor = Color.white;
    // Near-white with a cyan bias rather than flat cyan. Saturated colour loses to a lit
    // board; a white core with the hue carried by the outline and the bloom does not.
    [SerializeField] private Color wordColor = new Color32(215, 252, 255, 255);

    [Tooltip("How far past full brightness the text is driven as it arrives. Above 1 the " +
             "glyphs clear the bloom threshold and the popup lights up rather than merely " +
             "appearing -- the same trick the landing cells and clear bands use.")]
    [SerializeField, Range(1f, 3f)] private float arrivalFlare = 2.6f;

    [Header("Stars")]
    [SerializeField, Range(0, 8)] private int starCount = 4;
    [SerializeField] private float starSize = 0.5f;

    [Header("Glow")]
    [Tooltip("Size of the warm halo behind the points, in cells. Zero disables it.")]
    [SerializeField] private float pointsGlowSize = 2.4f;
    [SerializeField] private int sortingOrder = 120;

    private GameManager gameManager;
    private GridManager gridManager;
    private BlockSkin skin;

    private SpriteRenderer pointsGlow;
    private TextMeshPro pointsText;
    private TextMeshPro wordText;
    private SpriteRenderer[] stars;
    private Transform root;

    private Coroutine playing;
    private DeterministicRandom rng = new DeterministicRandom(0x2545F491u);

    private void Awake()
    {
        gameManager = GetComponent<GameManager>();
        if (gameManager == null) gameManager = FindAnyObjectByType<GameManager>();

        gridManager = FindAnyObjectByType<GridManager>();

        // Fall back whenever the visualizer has no skin yet, not just when the visualizer
        // is missing: Unity gives no Awake order, so it may exist but not have resolved
        // its own skin, and the old ternary took its null and never tried Resources.
        GridVisualizer visualizer = FindAnyObjectByType<GridVisualizer>();
        skin = visualizer != null ? visualizer.Skin : null;
        if (skin == null) skin = Resources.Load<BlockSkin>(BlockSkin.ResourcesPath);

        Build();
    }

    private void OnEnable()
    {
        if (gameManager != null) gameManager.TurnResolved += HandleTurnResolved;
    }

    private void OnDisable()
    {
        if (gameManager != null) gameManager.TurnResolved -= HandleTurnResolved;
    }

    /// <summary>
    /// Builds the popup once and reuses it. Rebuilding per clear would mean allocating
    /// TextMeshPro objects on the exact frame the board is already busiest.
    /// </summary>
    private void Build()
    {
        var go = new GameObject("PraisePopup");
        go.transform.SetParent(transform, false);
        root = go.transform;

        // Halo first so it sits behind the number, which is what gives the points
        // their warmth in the reference instead of reading as plain white text.
        var glowGo = new GameObject("PointsGlow");
        glowGo.transform.SetParent(root, false);
        pointsGlow = glowGo.AddComponent<SpriteRenderer>();
        pointsGlow.sprite = skin != null ? skin.Glow : null;
        pointsGlow.sortingOrder = sortingOrder;
        if (skin != null && skin.SpriteMaterial != null) pointsGlow.sharedMaterial = skin.SpriteMaterial;

        pointsText = CreateText("Points", pointsFontSize, pointsColor, sortingOrder + 2);
        wordText = CreateText("Word", wordFontSize, wordColor, sortingOrder + 2);

        stars = new SpriteRenderer[starCount];
        for (int i = 0; i < starCount; i++)
        {
            var starGo = new GameObject($"Star_{i}");
            starGo.transform.SetParent(root, false);

            var renderer = starGo.AddComponent<SpriteRenderer>();
            renderer.sprite = skin != null ? skin.Star : null;
            renderer.sortingOrder = sortingOrder;
            if (skin != null && skin.SpriteMaterial != null) renderer.sharedMaterial = skin.SpriteMaterial;
            stars[i] = renderer;
        }

        root.gameObject.SetActive(false);
    }

    private TextMeshPro CreateText(string label, float size, Color color, int order)
    {
        var go = new GameObject(label);
        go.transform.SetParent(root, false);

        var text = go.AddComponent<TextMeshPro>();
        text.fontSize = size;
        text.color = color;
        text.alignment = TextAlignmentOptions.Center;
        text.fontStyle = FontStyles.Bold;
        // Enough dark edge to survive the clear flash underneath, and no more: at 0.22 the
        // outline ate into the glyphs and the text read as blurred rather than as outlined.
        text.outlineWidth = 0.16f;
        text.outlineColor = new Color32(6, 8, 18, 255);
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.sortingOrder = order;

        // The board is small on a phone; a rect that clips would silently swallow the
        // longer praise words.
        text.rectTransform.sizeDelta = new Vector2(20f, 4f);
        return text;
    }

    private void HandleTurnResolved(LineClearResult cleared, TurnScore score)
    {
        if (!cleared.Any || root == null) return;

        if (playing != null) StopCoroutine(playing);
        playing = StartCoroutine(PlayRoutine(cleared, score));
    }

    private IEnumerator PlayRoutine(LineClearResult cleared, TurnScore score)
    {
        GridGeometry geometry = gridManager != null
            ? gridManager.Geometry
            : new GridGeometry(8, 8, 1f, 0.1f, Vector2.zero);

        // Sit above the cleared cells rather than at a fixed spot, so the popup points at
        // the move the player just made.
        Vector2 center = MaskCenter(cleared.ClearedMask);
        Vector3 start = geometry.CellToWorld(Mathf.RoundToInt(center.x), Mathf.RoundToInt(center.y))
                        + new Vector3(0f, geometry.Pitch * liftAboveClearInCells, 0f);

        // Keep it on the board: a clear along the top row would otherwise push the
        // popup off the playfield entirely.
        float ceiling = geometry.CellToWorld(0, geometry.Height - 1).y;
        start.y = Mathf.Min(start.y, ceiling);

        pointsText.text = score.Total.ToString();
        wordText.text = PickWord(cleared, score);

        Vector3 pointsAt = new Vector3(0f, geometry.Pitch * 0.85f, 0f);
        pointsText.transform.localPosition = pointsAt;
        if (pointsGlow != null)
        {
            pointsGlow.transform.localPosition = pointsAt;
            pointsGlow.transform.localScale = Vector3.one * pointsGlowSize * geometry.Pitch;
        }
        wordText.transform.localPosition = Vector3.zero;

        PlaceStars(geometry);
        root.gameObject.SetActive(true);

        float total = holdDuration + fadeDuration;
        float elapsed = 0f;
        float rise = riseInCells * geometry.Pitch;

        while (elapsed < total)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / total);

            root.position = start + new Vector3(0f, rise * Easing.OutCubic(t), 0f);

            // Punch in over the hold, then fade; the scale settles before the fade starts
            // so the word is legible at full size for a moment.
            float pop = elapsed < holdDuration
                ? Easing.OutBack(elapsed / holdDuration, 2.6f)
                : 1f;
            root.localScale = Vector3.one * Mathf.LerpUnclamped(0.55f, 1f, pop);

            float alpha = elapsed <= holdDuration
                ? 1f
                : 1f - Easing.InQuad((elapsed - holdDuration) / fadeDuration);

            // Overdriven on arrival, easing back to normal over the hold. The fade out is
            // left un-flared so the popup leaves quietly instead of flaring twice.
            float flare = elapsed < holdDuration
                ? Mathf.LerpUnclamped(arrivalFlare, 1f, Easing.OutCubic(elapsed / holdDuration))
                : 1f;

            SetAlpha(alpha, flare);
            yield return null;
        }

        root.gameObject.SetActive(false);
        playing = null;
    }

    /// <summary>Escalates with the size of the play: more lines and a longer streak read louder.</summary>
    private string PickWord(LineClearResult cleared, TurnScore score)
    {
        if (words == null || words.Length == 0) return string.Empty;

        int tier = (cleared.LineCount - 1) + Mathf.Max(0, score.Combo - 1);
        return words[Mathf.Clamp(tier, 0, words.Length - 1)];
    }

    private void PlaceStars(GridGeometry geometry)
    {
        if (stars == null) return;

        float radius = geometry.Pitch * 0.95f;
        for (int i = 0; i < stars.Length; i++)
        {
            if (stars[i] == null) continue;

            float angle = (i / (float)stars.Length + rng.NextFloat() * 0.12f) * Mathf.PI * 2f;
            stars[i].transform.localPosition = new Vector3(
                Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius * 0.5f, 0f);
            stars[i].transform.localScale = Vector3.one * starSize * (0.6f + rng.NextFloat() * 0.6f);
        }
    }

    private void SetAlpha(float alpha, float flare = 1f)
    {
        // Multiply then set alpha: scaling the colour scales alpha too, which would make
        // the flare double as an opacity ramp and undo the fade.
        Color p = pointsColor * flare; p.a = alpha; pointsText.color = p;
        Color w = wordColor * flare; w.a = alpha; wordText.color = w;

        if (pointsGlow != null && skin != null)
        {
            Color g = skin.PopupGlowColor;
            g.a *= alpha * 0.75f;
            pointsGlow.color = g;
        }

        if (stars == null) return;
        foreach (SpriteRenderer star in stars)
        {
            if (star == null) continue;
            Color c = star.color;
            c.a = alpha;
            star.color = c;
        }
    }

    private static Vector2 MaskCenter(ulong mask)
    {
        if (mask == 0UL) return new Vector2(3.5f, 3.5f);

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
}
