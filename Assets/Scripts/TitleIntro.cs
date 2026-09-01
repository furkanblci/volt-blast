using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using BlockBlast.Core;

/// <summary>
/// The wordmark shown when the app opens.
///
/// Drawn as text in the game's own typeface rather than as an image: it stays crisp at any
/// density, it costs no texture, and it cannot drift out of step with the rest of the UI
/// the way a baked logo would after a font or palette change.
///
/// This is an intro, not a menu. It plays once per launch, it takes about a second and a
/// half, and a tap skips it -- a title card the player cannot get past is a toll gate, and
/// the second launch is the one where that starts to grate.
/// </summary>
[DefaultExecutionOrder(-200)]
public class TitleIntro : MonoBehaviour
{
    [Header("Wordmark")]
    [Tooltip("Display face for the mark. Deliberately not the project default: Orbitron " +
             "is the game's identity but its slashed zero reads as an icon rather than a " +
             "digit when it stands alone, so the HUD uses a rounder face and the logo -- " +
             "which contains no digits -- keeps the sci-fi one.")]
    [SerializeField] private TMP_FontAsset wordmarkFont;

    /// <summary>Where the display face lives, so no scene wiring is needed.</summary>
    private const string WordmarkFontResource = "Orbitron SDF";

    /// <summary>Tube edge and halo for the mark, matching the rest of the game's light.</summary>
    private const string WordmarkMaterialResource = "WordmarkNeon";

    [SerializeField] private string title = "VOLT BLAST";

    [Header("Timing")]
    [Tooltip("How long the mark takes to light up.")]
    [SerializeField] private float riseDuration = 0.55f;

    [Tooltip("How long it holds at full brightness before leaving.")]
    [SerializeField] private float holdDuration = 0.65f;

    [SerializeField] private float fadeDuration = 0.35f;

    [Header("Colours")]
    [Tooltip("Face colour. White, so the palette gradient below reads as light passing " +
             "through the glyphs rather than as the letters being painted.")]
    [SerializeField] private Color titleColor = new Color32(255, 255, 255, 255);

    [Tooltip("Gradient across the mark, in the block palette. A wordmark in flat white was " +
             "the one thing on screen not made of the game's own light.")]
    [SerializeField] private Color gradientLeft = new Color32(40, 214, 240, 255);

    [SerializeField] private Color gradientRight = new Color32(255, 72, 158, 255);

    /// <summary>True while the intro is on screen, so input can be held off.</summary>
    public bool IsPlaying { get; private set; }

    private CanvasGroup group;
    private TextMeshProUGUI titleText;
    private RectTransform root;

    private void Start()
    {
        // Once per launch. A scene reload on restart must not replay it.
        // Destroy(this), never Destroy(gameObject): GameBootstrap adds this component to
        // the Bootstrap object alongside the drag controller, audio, feedback and every
        // other system. Tearing down the GameObject took the whole game's input with it.
        if (Played) { Destroy(this); return; }
        Played = true;

        if (!Build()) { Destroy(this); return; }
        StartCoroutine(Play());
    }

    /// <summary>Static so it survives a scene reload within the same session.</summary>
    private static bool Played;

    private bool Build()
    {
        Canvas canvas = FindAnyObjectByType<Canvas>();
        if (canvas == null) return false;

        root = UIFactory.Stretch(canvas.transform, "TitleIntro");
        root.SetAsLastSibling();

        group = root.gameObject.AddComponent<CanvasGroup>();
        group.alpha = 1f;
        // Blocks the board underneath, so a tap during the intro skips rather than
        // accidentally picking up a tray piece the player cannot even see yet.
        group.blocksRaycasts = true;

        RectTransform bg = UIFactory.Stretch(root, "Backdrop");
        Image plate = UIFactory.Sprite(bg, null, new Color32(8, 8, 15, 255));
        plate.raycastTarget = true;

        // 860 of a 1080 reference width: auto-sizing will happily run the mark edge to edge
        // if the box lets it, and a wordmark touching both screen edges reads as clipped
        // rather than as centred.
        titleText = UIFactory.Text(root, "Wordmark", title, 132f, titleColor,
            new Vector2(860f, 240f), Vector2.zero);

        // Auto-sized rather than set to a number that happens to fit today. Orbitron is a
        // wide face -- "VOLT BLAST" at a fixed 132pt ran off both edges -- and the title is
        // a serialized string, so the next person to shorten or lengthen it must not have
        // to rediscover the point size that keeps it on screen.
        titleText.textWrappingMode = TextWrappingModes.NoWrap;
        titleText.enableAutoSizing = true;
        titleText.fontSizeMin = 36f;
        titleText.fontSizeMax = 132f;

        if (wordmarkFont == null) wordmarkFont = Resources.Load<TMP_FontAsset>(WordmarkFontResource);
        if (wordmarkFont != null) titleText.font = wordmarkFont;

        Material neon = Resources.Load<Material>(WordmarkMaterialResource);
        if (neon != null) titleText.fontSharedMaterial = neon;

        // A horizontal sweep across the whole mark rather than per-character, so it reads
        // as one lit sign instead of ten differently coloured letters.
        titleText.enableVertexGradient = true;
        // Barely lightened at the top, pure at the bottom. Measured at a 0.35 mix the
        // gradient washed out to near-white -- the halo and bloom already push the face
        // that way, so the colour has to start further from white than looks right in the
        // inspector.
        titleText.colorGradient = new VertexGradient(
            Color.Lerp(gradientLeft, Color.white, 0.12f),
            Color.Lerp(gradientRight, Color.white, 0.12f),
            gradientLeft,
            gradientRight);

        titleText.color = titleColor;

        return true;
    }

    private IEnumerator Play()
    {
        IsPlaying = true;

        // Light up rather than fade in: the mark is a neon sign, and a sign switching on is
        // a different gesture from a picture appearing.
        float elapsed = 0f;
        while (elapsed < riseDuration && !Skipped())
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Easing.OutCubic(Mathf.Clamp01(elapsed / riseDuration));

            titleText.alpha = t;
            // Scale, not letter spacing: auto-sizing re-fits the text every time the
            // spacing changes, and the mark visibly jitters while it settles.
            titleText.rectTransform.localScale = Vector3.one * Mathf.LerpUnclamped(1.12f, 1f, t);
            yield return null;
        }

        titleText.alpha = 1f;
        titleText.rectTransform.localScale = Vector3.one;

        float held = 0f;
        while (held < holdDuration && !Skipped())
        {
            held += Time.unscaledDeltaTime;
            yield return null;
        }

        group.blocksRaycasts = false;
        yield return UIFactory.Fade(group, group.alpha, 0f, fadeDuration);

        IsPlaying = false;
        Destroy(root.gameObject);
        Destroy(this);
    }

    private static bool Skipped() =>
        Input.GetMouseButtonDown(0) || (Input.touchCount > 0 && Input.GetTouch(0).phase == TouchPhase.Began);
}
