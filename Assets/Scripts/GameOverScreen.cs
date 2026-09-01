using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using BlockBlast.Core;

/// <summary>
/// The screen a run ends on.
///
/// A full-bleed takeover with a crown, the score, and a single play button. Deliberately
/// not a small dialog over the board -- the run is over, and leaving the dead board
/// visible behind a popup makes it read as a pause.
///
/// The backdrop is a pool of light rather than a flat wall. On a near-black page a dark
/// screen would read as nothing having happened, and the reference's bright purple belongs
/// to the pre-neon palette; a spotlight is dark enough to belong here and bright enough to
/// be an arrival.
///
/// Two states share the layout. Beating the best score says so and shows a crown; an
/// ordinary loss shows the score plainly. Same screen either way, because a separate
/// "you did badly" screen is a punishment the game does not need.
/// </summary>
public class GameOverScreen : MonoBehaviour
{
    [Header("Timing")]
    [Tooltip("Pause before the screen appears, so the last clear finishes playing.")]
    [SerializeField] private float appearDelay = 0.55f;

    [SerializeField] private float fadeDuration = 0.28f;

    [Tooltip("How long the score counts up once the screen has faded in.")]
    [SerializeField] private float rollDuration = 0.65f;

    [Header("Colours")]
    [Tooltip("Title on a new best: gold, matching the crown and the HUD's best readout.")]
    [SerializeField] private Color bestTitleColor = new Color32(255, 196, 0, 255);

    [Tooltip("Title on an ordinary loss. Cyan -- the board frame's own colour. Gold is " +
             "what beating your best looks like and should not be spent on a plain end.")]
    [SerializeField] private Color plainTitleColor = new Color32(120, 226, 255, 255);

    [SerializeField] private Color subtitleColor = new Color32(150, 160, 210, 255);
    [SerializeField] private Color buttonColor = new Color32(255, 72, 158, 255);

    private GameManager gameManager;
    private BlockSkin skin;

    private CanvasGroup group;
    private TextMeshProUGUI titleText;
    private TextMeshProUGUI subtitleText;
    private TextMeshProUGUI scoreText;
    private Image crown;

    private Coroutine transition;

    private void Awake()
    {
        gameManager = FindAnyObjectByType<GameManager>();

        GridVisualizer visualizer = FindAnyObjectByType<GridVisualizer>();
        skin = visualizer != null ? visualizer.Skin : null;
        if (skin == null) skin = Resources.Load<BlockSkin>(BlockSkin.ResourcesPath);

        Build();
    }

    private void OnEnable()
    {
        if (gameManager != null) gameManager.OnGameStateChanged += HandleGameStateChanged;
    }

    private void OnDisable()
    {
        if (gameManager != null) gameManager.OnGameStateChanged -= HandleGameStateChanged;
    }

    private void Build()
    {
        Canvas canvas = FindAnyObjectByType<Canvas>();
        if (canvas == null)
        {
            Debug.LogError("[GameOverScreen] No Canvas in the scene; the end screen cannot be built.", this);
            return;
        }

        RectTransform root = UIFactory.Stretch(canvas.transform, "GameOverScreen");
        // Last sibling so it covers the HUD; the run is over and the score readout behind
        // it would only compete with the one on this screen.
        root.SetAsLastSibling();

        group = root.gameObject.AddComponent<CanvasGroup>();
        group.alpha = 0f;
        group.blocksRaycasts = true;

        // Opaque backdrop rather than a dim: this replaces the board, it does not veil it.
        RectTransform bg = UIFactory.Stretch(root, "Background");
        Image bgImage = UIFactory.Sprite(bg, skin != null ? skin.BestScoreBackground : null, Color.white);
        bgImage.raycastTarget = true;
        if (bgImage.sprite == null) bgImage.color = new Color32(20, 12, 38, 255);

        RectTransform crownRect = UIFactory.Box(root, "Crown", new Vector2(150f, 150f), new Vector2(0f, 430f));
        crown = UIFactory.Sprite(crownRect, skin != null ? skin.Crown : null, bestTitleColor);

        // No outline. The canvas now renders through the camera, so bloom picks the title
        // out on its own; an outline only puts a dark seam between the glyph and its glow.
        titleText = UIFactory.Text(root, "Title", "Best Score!", 96f, bestTitleColor,
            new Vector2(900f, 140f), new Vector2(0f, 300f));

        subtitleText = UIFactory.Text(root, "Subtitle", "Golden Best Score", 34f, subtitleColor,
            new Vector2(900f, 60f), new Vector2(0f, 220f), FontStyles.Normal);

        scoreText = UIFactory.Text(root, "Score", "0", 150f, Color.white,
            new Vector2(900f, 200f), new Vector2(0f, 60f));

        Button play = UIFactory.PillButton(root, "PlayButton", string.Empty,
            skin != null ? skin.ButtonSprite : null, skin != null ? skin.PlayIcon : null,
            buttonColor, Color.white, new Vector2(420f, 140f), new Vector2(0f, -160f), 48f);
        play.onClick.AddListener(Replay);

        root.gameObject.SetActive(false);
    }

    private void HandleGameStateChanged(bool isGameOver)
    {
        if (group == null) return;

        if (transition != null) StopCoroutine(transition);
        transition = StartCoroutine(isGameOver ? Show() : Hide());
    }

    private IEnumerator Show()
    {
        // Let the board finish its last clear before covering it, or the player never sees
        // the move that ended the run.
        yield return new WaitForSecondsRealtime(appearDelay);

        int score = ScoreManager.Instance != null ? ScoreManager.Instance.GetScore() : 0;
        int best = ScoreManager.Instance != null ? ScoreManager.Instance.GetHighScore() : 0;
        bool isBest = score >= best && score > 0;

        titleText.text = isBest ? "Best Score!" : "Game Over";
        titleText.color = isBest ? bestTitleColor : plainTitleColor;
        subtitleText.text = isBest ? "Golden Best Score" : $"Best  {best}";
        // Start at zero so the number is counted out rather than simply presented.
        scoreText.text = "0";
        if (crown != null) crown.enabled = isBest;

        group.gameObject.SetActive(true);
        group.blocksRaycasts = true;
        // Re-assert the draw order: other systems add to the canvas after this was
        // built, and a sibling created later would otherwise sit on top of it.
        group.transform.SetAsLastSibling();
        transition = null;

        yield return UIFactory.Fade(group, 0f, 1f, fadeDuration);

        // Reveal in order -- field, then title, then the number. Everything arriving at
        // once is one event; arriving in sequence gives the score somewhere to land.
        StartCoroutine(PopIn(titleText.transform));
        if (crown != null && crown.enabled) StartCoroutine(PopIn(crown.transform));
        yield return RollScore(score);
    }

    /// <summary>Counts the final score up, the same way the HUD counts a gain.</summary>
    private IEnumerator RollScore(int score)
    {
        float elapsed = 0f;
        while (elapsed < rollDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            scoreText.text = Mathf.RoundToInt(
                Mathf.Lerp(0f, score, Easing.OutCubic(elapsed / rollDuration))).ToString();
            yield return null;
        }

        scoreText.text = score.ToString();
        StartCoroutine(PopIn(scoreText.transform));
    }

    private IEnumerator PopIn(Transform target)
    {
        const float duration = 0.34f;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            target.localScale = Vector3.one * (1f + Easing.Pulse(elapsed / duration) * 0.22f);
            yield return null;
        }

        target.localScale = Vector3.one;
    }

    private IEnumerator Hide()
    {
        if (!group.gameObject.activeSelf) yield break;

        group.blocksRaycasts = false;
        yield return UIFactory.Fade(group, group.alpha, 0f, fadeDuration * 0.7f);

        group.gameObject.SetActive(false);
        transition = null;
    }

    private void Replay()
    {
        if (gameManager != null) gameManager.RestartGame();
    }
}
