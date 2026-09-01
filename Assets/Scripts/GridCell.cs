using System.Collections;
using UnityEngine;
using BlockBlast.Core;

/// <summary>
/// One board square's visual. Knows its coordinates and how to look filled, empty, or
/// about to be cleared -- state itself lives in the board, not in the cells that render it.
///
/// Because the board is the source of truth, these animations are free to lag it: by the
/// time a clear plays, the data already says empty, so nothing downstream has to wait for
/// the tween. Any new state cancels a running animation rather than queueing behind it,
/// which is what keeps a fast player from seeing stale cells.
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
public class GridCell : MonoBehaviour
{
    [SerializeField] private int gridX;
    [SerializeField] private int gridY;
    [SerializeField] private SpriteRenderer spriteRenderer;

    [Header("Animation")]
    [SerializeField] private float fillDuration = 0.17f;
    [SerializeField] private float clearDuration = 0.26f;

    [Tooltip("How far the pop overshoots when a piece lands.")]
    [SerializeField] private float fillOvershoot = 2.2f;

    [Tooltip("How far a clearing cell swells before it collapses.")]
    [SerializeField] private float clearSwell = 1.28f;

    [Tooltip("How far past full brightness a landing cell flares. Values above 1 push the " +
             "colour into HDR, which is what bloom picks up as a flash.")]
    [SerializeField, Range(1f, 3f)] private float landingFlare = 2.1f;

    private Color emptyColor = new Color(0.16f, 0.18f, 0.28f, 1f);
    private Sprite emptySprite;
    private int emptySortingOrder;
    private int filledSortingOrder = 1;

    // Sprites are baked in their final colour, so the renderer tint must stay white or
    // the two multiply together -- tinting a (22,34,66) sprite with (22,34,66) is very
    // nearly black, which is what made the whole board read as a void.
    private Color restTint = Color.white;

    private Vector3 baseScale = Vector3.one;
    private Coroutine activeAnimation;

    public int GridX => gridX;
    public int GridY => gridY;
    public bool IsFilled { get; private set; }

    private void Awake()
    {
        if (spriteRenderer == null) spriteRenderer = GetComponent<SpriteRenderer>();
    }

    public void Initialize(int x, int y, Sprite empty, Color emptyTint, int emptyOrder, int filledOrder)
    {
        if (spriteRenderer == null) spriteRenderer = GetComponent<SpriteRenderer>();

        gridX = x;
        gridY = y;
        emptySprite = empty;
        emptyColor = emptyTint;
        emptySortingOrder = emptyOrder;
        filledSortingOrder = filledOrder;

        // Captured after the visualizer has scaled us to cell size; every animation is
        // relative to this so cell size stays a single setting on the grid.
        baseScale = transform.localScale;

        SetEmpty();
    }

    // ---------- instant state ----------

    /// <summary>
    /// Shows a filled block. A null <paramref name="sprite"/> keeps whatever sprite the
    /// cell already has and just tints it, which is how a save written under a different
    /// skin still renders instead of vanishing.
    /// </summary>
    public void SetFilled(Sprite sprite, Color color)
    {
        StopAnimation();
        IsFilled = true;
        restTint = TintFor(sprite, color);
        Apply(sprite, restTint, filledSortingOrder, baseScale);
    }

    public void SetEmpty()
    {
        StopAnimation();
        IsFilled = false;
        restTint = TintFor(emptySprite, emptyColor);
        Apply(emptySprite, restTint, emptySortingOrder, baseScale);
    }

    /// <summary>
    /// White for a pre-shaded sprite, the raw colour only when we are falling back to an
    /// untextured square.
    /// </summary>
    private static Color TintFor(Sprite sprite, Color color) => sprite != null ? Color.white : color;

    // ---------- animated state ----------

    /// <summary>Pops the cell in as a piece lands on it.</summary>
    public void PlayFill(Sprite sprite, Color color, float delay = 0f)
    {
        StopAnimation();
        IsFilled = true;

        restTint = TintFor(sprite, color);

        if (!isActiveAndEnabled)
        {
            Apply(sprite, restTint, filledSortingOrder, baseScale);
            return;
        }

        activeAnimation = StartCoroutine(FillRoutine(sprite, restTint, delay));
    }

    /// <summary>
    /// Fades a settled cell down to a dead ember and leaves it there.
    ///
    /// Used when the run ends. The board freezing and a screen appearing over it reads as
    /// the game being interrupted; the lights going out reads as the run being over. The
    /// cell keeps its sprite, so it goes dark rather than empty -- the losing board is
    /// still legible under the takeover.
    /// </summary>
    public void PlayDrain(float delay, float toBrightness)
    {
        if (!IsFilled) return;

        StopAnimation();

        if (!isActiveAndEnabled)
        {
            if (spriteRenderer != null) spriteRenderer.color = restTint * toBrightness;
            return;
        }

        activeAnimation = StartCoroutine(DrainRoutine(delay, toBrightness));
    }

    private IEnumerator DrainRoutine(float delay, float toBrightness)
    {
        if (delay > 0f) yield return new WaitForSeconds(delay);

        const float duration = 0.3f;
        Color from = spriteRenderer != null ? spriteRenderer.color : restTint;
        Color to = restTint * toBrightness;
        to.a = from.a;

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            if (spriteRenderer != null)
                spriteRenderer.color = Color.LerpUnclamped(from, to, Easing.OutCubic(elapsed / duration));
            yield return null;
        }

        if (spriteRenderer != null) spriteRenderer.color = to;
        activeAnimation = null;
    }

    /// <summary>
    /// Swells, flashes white, then collapses. The cell is already empty in the data, so
    /// this is purely how the player is told about it.
    /// </summary>
    public void PlayClear(float delay = 0f)
    {
        StopAnimation();
        IsFilled = false;

        if (!isActiveAndEnabled)
        {
            restTint = TintFor(emptySprite, emptyColor);
            Apply(emptySprite, restTint, emptySortingOrder, baseScale);
            return;
        }

        activeAnimation = StartCoroutine(ClearRoutine(delay));
    }

    private IEnumerator FillRoutine(Sprite sprite, Color color, float delay)
    {
        Apply(sprite, color, filledSortingOrder, baseScale * 0.55f);

        if (delay > 0f) yield return new WaitForSeconds(delay);

        float elapsed = 0f;
        while (elapsed < fillDuration)
        {
            elapsed += Time.deltaTime;
            float k = Mathf.Clamp01(elapsed / fillDuration);

            transform.localScale = Vector3.LerpUnclamped(
                baseScale * 0.55f, baseScale, Easing.OutBack(k, fillOvershoot));

            // Overdrive the tint on the way in and let it fall back to normal. The colour
            // exceeds 1 for a moment, which is exactly what bloom reads as a flare -- a
            // scale pop alone reads as movement, not as the cell lighting up.
            if (spriteRenderer != null)
            {
                float flare = Mathf.LerpUnclamped(landingFlare, 1f, Easing.OutQuad(k));
                spriteRenderer.color = color * flare;
            }

            yield return null;
        }

        transform.localScale = baseScale;
        if (spriteRenderer != null) spriteRenderer.color = color;
        activeAnimation = null;
    }

    private IEnumerator ClearRoutine(float delay)
    {
        Color from = spriteRenderer != null ? spriteRenderer.color : Color.white;

        if (delay > 0f) yield return new WaitForSeconds(delay);

        // Clearing cells draw above filled ones so the collapse is never hidden behind
        // a piece that landed on top of the same row.
        if (spriteRenderer != null) spriteRenderer.sortingOrder = filledSortingOrder + 1;

        float elapsed = 0f;
        while (elapsed < clearDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / clearDuration);

            // Swell, then collapse to nothing while fading. The cell keeps its own colour
            // rather than blowing out to white: the sprite is already the finished colour,
            // and the band and particles supply the flash.
            float scale = t < 0.35f
                ? Mathf.LerpUnclamped(1f, clearSwell, Easing.OutQuad(t / 0.35f))
                : Mathf.LerpUnclamped(clearSwell, 0f, Easing.InQuad((t - 0.35f) / 0.65f));

            transform.localScale = baseScale * scale;

            if (spriteRenderer != null)
            {
                Color c = from;
                c.a = 1f - Easing.InQuad(t);
                spriteRenderer.color = c;
            }

            yield return null;
        }

        restTint = TintFor(emptySprite, emptyColor);
        Apply(emptySprite, restTint, emptySortingOrder, baseScale);
        activeAnimation = null;
    }

    // ---------- internals ----------

    /// <summary>A null sprite leaves the current one in place rather than blanking the cell.</summary>
    private void Apply(Sprite sprite, Color color, int sortingOrder, Vector3 scale)
    {
        transform.localScale = scale;
        if (spriteRenderer == null) return;

        if (sprite != null) spriteRenderer.sprite = sprite;
        spriteRenderer.color = color;
        spriteRenderer.sortingOrder = sortingOrder;
    }

    private void StopAnimation()
    {
        if (activeAnimation == null) return;
        StopCoroutine(activeAnimation);
        activeAnimation = null;
    }

    private void OnDisable()
    {
        // A disabled cell cannot finish its coroutine, so settle it rather than leaving
        // it frozen mid-tween at a fraction of its size.
        if (activeAnimation == null) return;

        activeAnimation = null;
        Apply(IsFilled ? null : emptySprite, restTint,
            IsFilled ? filledSortingOrder : emptySortingOrder, baseScale);
    }
}
