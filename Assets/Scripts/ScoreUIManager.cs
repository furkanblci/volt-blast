using System.Collections;
using TMPro;
using UnityEngine;
using BlockBlast.Core;

/// <summary>
/// Drives the score HUD.
///
/// The score counts up to its new value instead of snapping. A number that jumps gives no
/// sense of how much a move was worth; one that rolls makes a big clear feel big, and the
/// length of the roll is itself the feedback.
///
/// The combo readout climbs the block palette as a streak builds -- cyan, violet, magenta,
/// orange -- so the player can read the streak from colour alone without parsing the
/// digits. The ramp is the game's own colours rather than a generic white-to-yellow fade,
/// which read as a different piece of software sitting on top of the board.
/// </summary>
public class ScoreUIManager : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private TextMeshProUGUI paramScoreText;
    [SerializeField] private TextMeshProUGUI paramHighScoreText;
    [SerializeField] private TextMeshProUGUI paramComboText;

    [Header("Combo Settings")]
    [SerializeField] private string comboPrefix = "COMBO x";

    [Tooltip("One colour per combo step from x2 upward; the last is held for longer " +
             "streaks. Discrete stops rather than a lerp -- a streak is a count, and " +
             "interpolating between two palette colours lands on muddy off-palette mixes.")]
    [SerializeField] private Color[] comboRamp =
    {
        new Color32(40, 214, 240, 255),
        new Color32(188, 122, 255, 255),
        new Color32(255, 72, 158, 255),
        new Color32(255, 178, 50, 255),
    };

    [Tooltip("How far the readout rises as it fades in, in canvas units.")]
    [SerializeField] private float comboRise = 26f;

    [SerializeField] private float comboFadeDuration = 0.18f;

    [Header("Roll-up")]
    [Tooltip("Shortest time a count-up takes.")]
    [SerializeField] private float minRollDuration = 0.15f;

    [Tooltip("Longest time a count-up takes, however large the gain.")]
    [SerializeField] private float maxRollDuration = 0.7f;

    [Tooltip("Points that stretch the roll to its maximum length.")]
    [SerializeField] private int rollPointsForMaxDuration = 400;

    [Header("Punch")]
    [SerializeField] private float punchDuration = 0.26f;
    [SerializeField, Range(0f, 1f)] private float scorePunch = 0.16f;
    [SerializeField, Range(0f, 1.5f)] private float comboPunch = 0.5f;

    private int displayedScore;
    private int targetScore;
    private int lastCombo;

    private Coroutine rollRoutine;
    private Coroutine scorePunchRoutine;
    private Coroutine comboPunchRoutine;

    // Resting scales captured once. Reading them at punch time instead would let an
    // interrupted punch bake its midpoint in as the new baseline, and the text would
    // creep larger every combo.
    private Vector3 scoreBaseScale = Vector3.one;
    private Vector3 comboBaseScale = Vector3.one;
    private Vector2 comboBasePosition;
    private Coroutine comboFadeRoutine;

    private void Awake()
    {
        if (paramScoreText != null) scoreBaseScale = paramScoreText.transform.localScale;
        if (paramComboText != null)
        {
            comboBaseScale = paramComboText.transform.localScale;
            comboBasePosition = paramComboText.rectTransform.anchoredPosition;
        }
    }

    private void Start()
    {
        if (ScoreManager.Instance == null)
        {
            Debug.LogWarning("[ScoreUIManager] No ScoreManager in the scene; the HUD will not update.", this);
            return;
        }

        ScoreManager.Instance.OnScoreChanged += HandleScoreChanged;
        ScoreManager.Instance.OnHighScoreChanged += HandleHighScoreChanged;
        ScoreManager.Instance.OnComboChanged += HandleComboChanged;

        // Seed without animating: the opening value is not something the player earned.
        displayedScore = targetScore = ScoreManager.Instance.GetScore();
        lastCombo = ScoreManager.Instance.GetCombo();

        RenderScore(displayedScore);
        HandleHighScoreChanged(ScoreManager.Instance.GetHighScore());
        RenderCombo(lastCombo);
    }

    private void OnDestroy()
    {
        if (ScoreManager.Instance == null) return;

        ScoreManager.Instance.OnScoreChanged -= HandleScoreChanged;
        ScoreManager.Instance.OnHighScoreChanged -= HandleHighScoreChanged;
        ScoreManager.Instance.OnComboChanged -= HandleComboChanged;
    }

    // ---------- score ----------

    private void HandleScoreChanged(int score)
    {
        int gain = score - targetScore;
        targetScore = score;

        if (!isActiveAndEnabled || gain <= 0)
        {
            // A reset or a decrease should land immediately rather than counting backwards.
            displayedScore = targetScore;
            RenderScore(displayedScore);
            return;
        }

        if (rollRoutine != null) StopCoroutine(rollRoutine);
        rollRoutine = StartCoroutine(RollRoutine(gain));

        Punch(paramScoreText, ref scorePunchRoutine, scoreBaseScale, scorePunch);
    }

    private IEnumerator RollRoutine(int gain)
    {
        int from = displayedScore;
        int to = targetScore;

        // Bigger gains roll for longer, up to a cap, so a huge combo reads as an event
        // without ever leaving the HUD lagging behind the board.
        float duration = Mathf.Lerp(
            minRollDuration, maxRollDuration,
            Mathf.Clamp01(gain / (float)Mathf.Max(1, rollPointsForMaxDuration)));

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            displayedScore = Mathf.RoundToInt(
                Mathf.Lerp(from, to, Easing.OutCubic(elapsed / duration)));
            RenderScore(displayedScore);
            yield return null;
        }

        displayedScore = to;
        RenderScore(displayedScore);
        rollRoutine = null;
    }

    private void RenderScore(int value)
    {
        if (paramScoreText != null) paramScoreText.text = value.ToString();
    }

    private void HandleHighScoreChanged(int highScore)
    {
        if (paramHighScoreText != null) paramHighScoreText.text = highScore.ToString();
    }

    // ---------- combo ----------

    private void HandleComboChanged(int combo)
    {
        bool grew = combo > lastCombo;
        lastCombo = combo;

        RenderCombo(combo);
        if (grew) Punch(paramComboText, ref comboPunchRoutine, comboBaseScale, comboPunch);
    }

    private void RenderCombo(int combo)
    {
        if (paramComboText == null) return;

        // A combo of 1 is just "you cleared a line"; only a streak is worth announcing.
        bool visible = combo > 1 && comboRamp != null && comboRamp.Length > 0;

        if (!visible)
        {
            if (paramComboText.gameObject.activeSelf) FadeCombo(false);
            return;
        }

        paramComboText.text = comboPrefix + combo;
        paramComboText.color = comboRamp[Mathf.Min(combo - 2, comboRamp.Length - 1)];

        if (!paramComboText.gameObject.activeSelf) FadeCombo(true);
    }

    /// <summary>
    /// Brings the readout in or out. It used to snap on and off with SetActive, which put
    /// a word on screen with no arrival -- the one element in the game that simply
    /// appeared. It rises as it fades so the eye catches the movement.
    /// </summary>
    private void FadeCombo(bool show)
    {
        if (!isActiveAndEnabled)
        {
            // No coroutines outside play; land on the end state directly.
            paramComboText.gameObject.SetActive(show);
            paramComboText.alpha = show ? 1f : 0f;
            paramComboText.rectTransform.anchoredPosition = comboBasePosition;
            return;
        }

        if (comboFadeRoutine != null) StopCoroutine(comboFadeRoutine);
        comboFadeRoutine = StartCoroutine(ComboFadeRoutine(show));
    }

    private IEnumerator ComboFadeRoutine(bool show)
    {
        if (show) paramComboText.gameObject.SetActive(true);

        float from = show ? 0f : paramComboText.alpha;
        float to = show ? 1f : 0f;
        float elapsed = 0f;

        while (elapsed < comboFadeDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Easing.OutCubic(elapsed / comboFadeDuration);
            paramComboText.alpha = Mathf.Lerp(from, to, t);
            // Only the entrance travels; on the way out it fades in place, so a streak
            // ending does not draw as much attention as one starting.
            paramComboText.rectTransform.anchoredPosition = show
                ? comboBasePosition + Vector2.down * comboRise * (1f - t)
                : comboBasePosition;
            yield return null;
        }

        paramComboText.alpha = to;
        paramComboText.rectTransform.anchoredPosition = comboBasePosition;
        if (!show) paramComboText.gameObject.SetActive(false);
        comboFadeRoutine = null;
    }

    // ---------- punch ----------

    private void Punch(TextMeshProUGUI text, ref Coroutine slot, Vector3 baseScale, float amount)
    {
        if (text == null || amount <= 0f || !isActiveAndEnabled) return;

        if (slot != null)
        {
            StopCoroutine(slot);
            // Restore the baseline before restarting, so a punch interrupted mid-swell
            // does not stack on top of the size it had reached.
            text.transform.localScale = baseScale;
        }

        slot = StartCoroutine(PunchRoutine(text.transform, baseScale, amount));
    }

    private IEnumerator PunchRoutine(Transform target, Vector3 baseScale, float amount)
    {
        float elapsed = 0f;

        // Unscaled time so the HUD still reacts if the game is ever paused mid-turn.
        while (elapsed < punchDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            target.localScale = baseScale * (1f + Easing.Pulse(elapsed / punchDuration) * amount);
            yield return null;
        }

        target.localScale = baseScale;
    }
}
