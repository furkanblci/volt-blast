using UnityEngine;
using BlockBlast.Core;

/// <summary>
/// Turns game events into sound.
///
/// Kept apart from the systems that raise those events for the same reason TurnFeedback is:
/// the rules should never grow a dependency on how the game sounds, and everything audible
/// should be switchable from one place. It listens to events that already exist rather than
/// asking the game to tell it things.
///
/// Voices are a fixed pool. A pool caps the worst case instead of letting a busy turn
/// allocate sources, and -- more usefully on a phone -- it means a burst of overlapping
/// sounds degrades by dropping the oldest voice rather than by turning into mud.
///
/// Nothing here fails loudly. A missing bank or a missing clip is silence: audio is the
/// least important thing in the build to be strict about, and a null-check that throws
/// during a clear would cost a frame in the one moment the game cannot spare one.
/// </summary>
[DefaultExecutionOrder(-300)]
public class AudioDirector : MonoBehaviour
{
    [Tooltip("Bank of clips. Falls back to Resources/SoundBank.")]
    [SerializeField] private SoundBank bank;

    [Tooltip("Simultaneous sound effects. Past this the oldest voice is reused.")]
    [SerializeField, Range(2, 12)] private int voices = 4;

    private AudioSource[] pool;
    private int nextVoice;
    private AudioSource musicSource;

    private GameManager gameManager;
    private BlockDragController dragController;

    private void Awake()
    {
        if (bank == null) bank = Resources.Load<SoundBank>(SoundBank.ResourcesPath);

        pool = new AudioSource[Mathf.Max(2, voices)];
        for (int i = 0; i < pool.Length; i++) pool[i] = CreateSource("Voice " + i, false);

        musicSource = CreateSource("Music", true);

        gameManager = FindAnyObjectByType<GameManager>();
        dragController = FindAnyObjectByType<BlockDragController>();
    }

    private AudioSource CreateSource(string label, bool loop)
    {
        var go = new GameObject(label);
        go.transform.SetParent(transform, false);

        var source = go.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = loop;
        // 2D: the board is a flat plane a few units across, so positioning sounds in
        // space would pan them by an amount that means nothing and only ever annoys.
        source.spatialBlend = 0f;
        return source;
    }

    private void OnEnable()
    {
        if (gameManager != null)
        {
            gameManager.TurnResolved += HandleTurnResolved;
            gameManager.OnGameStateChanged += HandleGameStateChanged;
        }

        if (dragController != null)
        {
            dragController.PieceGrabbed += HandleGrabbed;
            dragController.PieceRejected += HandleRejected;
        }

        UIFactory.ButtonPressed += HandleButtonPressed;
        GameSettings.Changed += ApplySettings;
    }

    private void OnDisable()
    {
        if (gameManager != null)
        {
            gameManager.TurnResolved -= HandleTurnResolved;
            gameManager.OnGameStateChanged -= HandleGameStateChanged;
        }

        if (dragController != null)
        {
            dragController.PieceGrabbed -= HandleGrabbed;
            dragController.PieceRejected -= HandleRejected;
        }

        UIFactory.ButtonPressed -= HandleButtonPressed;
        GameSettings.Changed -= ApplySettings;
    }

    private void Start() => ApplySettings();

    // ---------- events ----------

    private void HandleTurnResolved(LineClearResult cleared, TurnScore score)
    {
        if (bank == null) return;

        Play(bank.Place, 1f, bank.PlaceGain);

        // The clear rides on top of the placement rather than replacing it: the piece
        // did land, and dropping that sound makes a clearing move feel like it skipped
        // a step.
        if (cleared.Any)
            Play(bank.Clear, bank.ClearPitch(cleared.LineCount, score.Combo), bank.ClearGain);
    }

    private void HandleGameStateChanged(bool isGameOver)
    {
        if (!isGameOver || bank == null) return;

        Play(bank.GameOver, 1f, bank.GameOverGain);
    }

    private void HandleGrabbed(BlockInstance piece)
    {
        if (bank != null) Play(bank.Pickup, 1f, bank.PickupGain);
    }

    private void HandleRejected(BlockInstance piece)
    {
        if (bank != null) Play(bank.Rejected, 1f, bank.RejectedGain);
    }

    private void HandleButtonPressed()
    {
        if (bank != null) Play(bank.Button, 1f, bank.ButtonGain);
    }

    // ---------- playback ----------

    /// <summary>Plays a one-shot on the next voice, silently doing nothing if it cannot.</summary>
    public void Play(AudioClip clip, float pitch = 1f, float gain = 1f)
    {
        if (clip == null || bank == null || pool == null || !GameSettings.Sound) return;

        AudioSource source = pool[nextVoice];
        nextVoice = (nextVoice + 1) % pool.Length;

        source.clip = clip;
        source.pitch = pitch;
        // The clip's own gain balances the set; the master is the player's volume for all
        // of it. Clamped because the two multiplied can exceed what a source accepts.
        source.volume = Mathf.Clamp01(bank.SfxVolume * gain);
        source.Play();
    }

    /// <summary>Re-reads the sound and music preferences.</summary>
    private void ApplySettings()
    {
        if (musicSource == null) return;

        bool wantMusic = bank != null && bank.Music != null && GameSettings.Music;

        musicSource.clip = bank != null ? bank.Music : null;
        musicSource.volume = bank != null ? bank.MusicVolume : 0f;

        if (wantMusic && !musicSource.isPlaying) musicSource.Play();
        else if (!wantMusic && musicSource.isPlaying) musicSource.Stop();

        // Sound effects are gated at Play time rather than muted here, so switching the
        // toggle never leaves a voice running that the player just asked to silence.
        if (!GameSettings.Sound && pool != null)
            foreach (AudioSource source in pool)
                if (source != null && source.isPlaying) source.Stop();
    }
}
