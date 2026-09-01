using UnityEngine;
using BlockBlast.Core;

/// <summary>
/// Holds the running score and combo and reports changes to the UI.
///
/// The arithmetic itself moved to <see cref="ScoreRules"/> so it can be tuned and
/// tested without entering Play mode. What stays here is the scene-facing state: the
/// current totals, the persisted best, and the events the HUD listens to.
///
/// Scoring is now a single call per turn rather than a placement call plus a separate
/// line-clear call, which is what previously let the combo advance out of step with the
/// placement that caused it.
/// </summary>
public class ScoreManager : MonoBehaviour
{
    /// <summary>
    /// Resolves lazily when the static is empty. Recompiling while play mode is running
    /// reloads the domain, which clears statics without re-running Awake on objects that
    /// already exist -- leaving the singleton null for the rest of the session. That
    /// cannot happen in a build, but it silently breaks the Editor, and a scene lookup on
    /// the rare null is far cheaper than the confusion.
    /// </summary>
    public static ScoreManager Instance
    {
        get
        {
            if (instance == null) instance = FindAnyObjectByType<ScoreManager>();
            return instance;
        }
        private set => instance = value;
    }

    private static ScoreManager instance;

    [Header("Scoring")]
    [SerializeField] private ScoringConfig scoring = ScoringConfig.Default;

    [Header("State (read-only)")]
    [SerializeField] private int currentScore;
    [SerializeField] private int highScore;
    [SerializeField] private int currentCombo;

    public delegate void ScoreChangedHelper(int newScore);
    public event ScoreChangedHelper OnScoreChanged;

    public delegate void ComboChangedHelper(int newCombo);
    public event ComboChangedHelper OnComboChanged;

    public delegate void HighScoreChangedHelper(int newHighScore);
    public event HighScoreChangedHelper OnHighScoreChanged;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        scoring = scoring.Sanitized();
        highScore = SaveSystem.ReadHighScore();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public int GetScore() => currentScore;
    public int GetHighScore() => highScore;
    public int GetCombo() => currentCombo;

    /// <summary>
    /// Scores one completed turn and advances the combo streak. Returns the breakdown so
    /// the caller can drive combo popups and floating numbers from real values.
    /// </summary>
    public TurnScore ScoreTurn(int placedCellCount, int lineCount)
    {
        TurnScore result = ScoreRules.ScoreTurn(placedCellCount, lineCount, ref currentCombo, scoring);

        currentScore += result.Total;
        OnScoreChanged?.Invoke(currentScore);
        OnComboChanged?.Invoke(currentCombo);

        if (currentScore > highScore)
        {
            highScore = currentScore;
            SaveSystem.WriteHighScore(highScore);
            OnHighScoreChanged?.Invoke(highScore);
        }

        return result;
    }

    public void ResetScore()
    {
        currentScore = 0;
        currentCombo = 0;
        OnScoreChanged?.Invoke(currentScore);
        OnComboChanged?.Invoke(currentCombo);
        OnHighScoreChanged?.Invoke(highScore);
    }

    /// <summary>Restores totals from a save without touching the persisted high score.</summary>
    public void Restore(int score, int combo)
    {
        currentScore = Mathf.Max(0, score);
        currentCombo = Mathf.Max(0, combo);
        OnScoreChanged?.Invoke(currentScore);
        OnComboChanged?.Invoke(currentCombo);
        OnHighScoreChanged?.Invoke(highScore);
    }

    /// <summary>Commits the best score. Called when a run ends, in case it ended on the peak.</summary>
    public void CommitHighScore() => SaveSystem.WriteHighScore(currentScore);

#if UNITY_EDITOR
    private void OnValidate() => scoring = scoring.Sanitized();
#endif
}
