using System;
using UnityEngine;
using BlockBlast.Core;

/// <summary>
/// Sequences a turn and owns the run.
///
/// The old flow started inside GridManager.PlaceBlock, fanned out to the score and
/// game-over systems mid-mutation, and deferred the game-over test to a coroutine that
/// waited a frame. Because the spawner refilled the tray in the same frame a piece was
/// consumed, that check could run against either the old tray or the new one depending
/// on ordering -- so "no moves left" was not reproducible.
///
/// A turn is now one synchronous, ordered method: place, find lines, score, clear,
/// consume, refill, then test for game over exactly once against the tray that actually
/// exists. Nothing else may trigger a game-over evaluation.
/// </summary>
public class GameManager : MonoBehaviour
{
    /// <summary>
    /// Resolves lazily when the static is empty. Recompiling while play mode is running
    /// reloads the domain, which clears statics without re-running Awake on objects that
    /// already exist -- leaving the singleton null for the rest of the session. That
    /// cannot happen in a build, but it silently breaks the Editor, and a scene lookup on
    /// the rare null is far cheaper than the confusion.
    /// </summary>
    public static GameManager Instance
    {
        get
        {
            if (instance == null) instance = FindAnyObjectByType<GameManager>();
            return instance;
        }
        private set => instance = value;
    }

    private static GameManager instance;

    [Header("References")]
    [SerializeField] private GridManager gridManager;
    [SerializeField] private SpawnManager spawnManager;
    [SerializeField] private PlacementValidator placementValidator;
    [SerializeField] private ScoreManager scoreManager;

    [Header("Run")]
    [Tooltip("Pick up the previous run's board, tray and score on launch.")]
    [SerializeField] private bool resumeSavedRun = true;

    public bool IsGameOver { get; private set; }

    public delegate void GameStateChangedHelper(bool isGameOver);
    public event GameStateChangedHelper OnGameStateChanged;

    /// <summary>
    /// Fires after a turn is fully resolved, with what the turn cleared and what it paid.
    /// The hook for clear effects and combo popups, which is why it carries real values
    /// instead of forcing the presentation layer to re-derive them.
    /// </summary>
    public event Action<LineClearResult, TurnScore> TurnResolved;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        ResolveReferences();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void Start() => StartGame();

    private void ResolveReferences()
    {
        if (gridManager == null) gridManager = FindAnyObjectByType<GridManager>();
        if (spawnManager == null) spawnManager = FindAnyObjectByType<SpawnManager>();
        if (placementValidator == null) placementValidator = FindAnyObjectByType<PlacementValidator>();
        if (scoreManager == null) scoreManager = FindAnyObjectByType<ScoreManager>();
    }

    private bool HasRequiredReferences =>
        gridManager != null && spawnManager != null && scoreManager != null;

    // ---------- run lifecycle ----------

    public void StartGame()
    {
        ResolveReferences();
        if (!HasRequiredReferences)
        {
            Debug.LogError("[GameManager] Missing GridManager, SpawnManager or ScoreManager; cannot start.", this);
            return;
        }

        IsGameOver = false;

        if (!(resumeSavedRun && TryResumeSavedRun()))
        {
            StartFreshRun();
        }

        OnGameStateChanged?.Invoke(false);

        // A resumed board can already be finished, so evaluate before handing over control.
        EvaluateGameOver();
    }

    /// <summary>Wipes any saved run and restarts. Bound to the game-over screen's button.</summary>
    public void RestartGame()
    {
        SaveSystem.Clear();
        ResolveReferences();
        if (!HasRequiredReferences) return;

        IsGameOver = false;
        StartFreshRun();
        OnGameStateChanged?.Invoke(false);
        EvaluateGameOver();
    }

    private void StartFreshRun()
    {
        gridManager.ClearGrid();
        scoreManager.ResetScore();
        spawnManager.ClearTray();
        spawnManager.RefillTray();
        SaveRun();
    }

    private bool TryResumeSavedRun()
    {
        GameSave save = SaveSystem.Read();
        if (save == null) return false;

        if (!gridManager.TryRestore(save))
        {
            // Board size changed since the save was written; the tray would not match it.
            SaveSystem.Clear();
            return false;
        }

        if (!spawnManager.TryRestoreTray(save))
        {
            SaveSystem.Clear();
            return false;
        }

        scoreManager.Restore(save.score, save.combo);

        // A save written mid-turn could have an empty tray; top it up before play resumes.
        if (spawnManager.IsTrayEmpty) spawnManager.RefillTray();

        return true;
    }

    // ---------- the turn ----------

    /// <summary>
    /// Commits a dropped piece. Returns false and changes nothing when the anchor is
    /// illegal, so the drag controller can simply send the piece home.
    /// </summary>
    public bool TryCommitPlacement(BlockInstance piece, Vector2Int anchor)
    {
        if (IsGameOver || piece == null || piece.IsConsumed || piece.BlockData == null) return false;
        if (!HasRequiredReferences) return false;

        if (!gridManager.TryPlace(piece.Table, anchor.x, anchor.y, piece.TintColor, out ulong filled))
            return false;

        // Find lines before clearing so the score sees the completed board, and so a
        // clear animation can be driven off the mask before the cells actually go away.
        LineClearResult cleared = gridManager.FindCompletedLines();
        TurnScore turn = scoreManager.ScoreTurn(BoardState.PopCount(filled), cleared.LineCount);
        gridManager.ApplyClear(cleared);

        spawnManager.Consume(piece);
        if (spawnManager.IsTrayEmpty) spawnManager.RefillTray();

        TurnResolved?.Invoke(cleared, turn);
        SaveRun();

        // Exactly one game-over evaluation per turn, after the tray has settled.
        EvaluateGameOver();
        return true;
    }

    /// <summary>The only place a run is allowed to end.</summary>
    private void EvaluateGameOver()
    {
        if (IsGameOver || !HasRequiredReferences) return;
        if (BoardRules.HasAnyMove(gridManager.Board, spawnManager.GetTrayTables())) return;

        IsGameOver = true;
        scoreManager.CommitHighScore();

        // Drop the run so the next launch starts clean rather than resuming a dead board.
        SaveSystem.Clear();
        OnGameStateChanged?.Invoke(true);
    }

    // ---------- persistence ----------

    private void SaveRun()
    {
        if (IsGameOver || !HasRequiredReferences) return;

        spawnManager.CaptureTray(out string[] shapeIds, out int[] slots, out uint[] colors);
        SaveSystem.Write(GameSave.Capture(
            gridManager.Board, scoreManager.GetScore(), scoreManager.GetCombo(), shapeIds, slots, colors));
    }

    // Mobile can kill the process straight from the background, so the pause callback is
    // the last dependable place to flush; OnApplicationQuit often never runs on Android.
    private void OnApplicationPause(bool paused)
    {
        if (paused) SaveRun();
    }

    private void OnApplicationFocus(bool focused)
    {
        if (!focused) SaveRun();
    }

    private void OnApplicationQuit() => SaveRun();
}
