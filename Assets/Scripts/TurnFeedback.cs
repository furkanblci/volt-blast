using UnityEngine;
using BlockBlast.Core;

/// <summary>
/// Turns the outcome of a move into physical feedback: shake and vibration scaled to what
/// the player actually pulled off.
///
/// Kept apart from GameManager so the rules never grow a dependency on how the game feels,
/// and so all of it can be switched off in one place. It listens to
/// <see cref="GameManager.TurnResolved"/>, which already carries the cleared mask and the
/// score breakdown, so nothing here has to re-derive what happened.
///
/// Feedback is deliberately silent on an ordinary placement. A cue that fires on every
/// single move stops meaning anything.
/// </summary>
public class TurnFeedback : MonoBehaviour
{
    [Header("Shake")]
    [Tooltip("Shake for a single-line clear, in world units.")]
    [SerializeField] private float singleLineShake = 0.06f;

    [Tooltip("Extra shake per line beyond the first.")]
    [SerializeField] private float perExtraLineShake = 0.07f;

    [Tooltip("Extra shake per combo step, so a streak escalates.")]
    [SerializeField] private float perComboShake = 0.02f;

    [Header("Glow Surge")]
    [Tooltip("Extra bloom for a single-line clear.")]
    [SerializeField] private float singleLineSurge = 0.9f;

    [Tooltip("Extra bloom per line beyond the first, and per combo step.")]
    [SerializeField] private float perLineSurge = 0.7f;

    [SerializeField] private float perComboSurge = 0.35f;

    [Header("Haptics")]
    [SerializeField] private bool haptics = true;

    [Tooltip("Lines cleared at once before the heaviest cue is used.")]
    [SerializeField] private int heavyThreshold = 2;

    private GameManager gameManager;
    private CameraShake cameraShake;
    private NeonPostFx postFx;
    private BlockDragController dragController;

    private void Awake()
    {
        gameManager = GetComponent<GameManager>();
        if (gameManager == null) gameManager = FindAnyObjectByType<GameManager>();

        postFx = FindAnyObjectByType<NeonPostFx>();
        dragController = FindAnyObjectByType<BlockDragController>();
        cameraShake = FindAnyObjectByType<CameraShake>();
        if (cameraShake == null && Camera.main != null)
        {
            // Attaching at runtime keeps existing scenes working without a manual step.
            cameraShake = Camera.main.gameObject.AddComponent<CameraShake>();
        }
    }

    private void OnEnable()
    {
        if (gameManager == null) return;

        gameManager.TurnResolved += HandleTurnResolved;
        gameManager.OnGameStateChanged += HandleGameStateChanged;

        // The moment a piece leaves the tray is the one the player's finger is already
        // committed to, so it is where a tick reads as the game answering the touch.
        if (dragController != null) dragController.PieceGrabbed += HandleGrabbed;
    }

    private void OnDisable()
    {
        if (gameManager == null) return;

        gameManager.TurnResolved -= HandleTurnResolved;
        gameManager.OnGameStateChanged -= HandleGameStateChanged;
        if (dragController != null) dragController.PieceGrabbed -= HandleGrabbed;
    }

    private void HandleTurnResolved(LineClearResult cleared, TurnScore score)
    {
        if (!cleared.Any)
        {
            // A plain placement gets the lightest possible acknowledgement and no shake.
            if (haptics) Haptics.Light();
            return;
        }

        if (postFx != null)
        {
            postFx.Surge(singleLineSurge
                         + (cleared.LineCount - 1) * perLineSurge
                         + Mathf.Max(0, score.Combo - 1) * perComboSurge);
        }

        if (cameraShake != null)
        {
            float amount = singleLineShake
                           + (cleared.LineCount - 1) * perExtraLineShake
                           + Mathf.Max(0, score.Combo - 1) * perComboShake;
            cameraShake.Shake(amount);
        }

        if (!haptics) return;

        if (cleared.LineCount >= heavyThreshold) Haptics.Heavy();
        else Haptics.Medium();
    }

    private void HandleGrabbed(BlockInstance piece)
    {
        if (haptics) Haptics.Light();
    }

    private void HandleGameStateChanged(bool isGameOver)
    {
        if (!isGameOver) return;

        if (cameraShake != null) cameraShake.Shake(0.2f);
        // A single weighted thud, not the streak's double knock: the run ending is one
        // event, and the celebratory pattern would read as a reward.
        if (haptics) Haptics.Thud();
    }
}
