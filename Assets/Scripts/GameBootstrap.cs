using UnityEngine;

/// <summary>
/// The composition root: the one place that says what the game is made of and in what
/// order it comes up.
///
/// This used to be hidden inside GameManager.Awake, which quietly added seven components
/// to itself. That worked, but nothing in the scene told you it was happening, and the
/// order was an accident of the order the lines happened to be written in. A reader had
/// to open GameManager to find out why a Volume or a drag controller existed at all.
///
/// What is built here is built here *because it has no authored state* -- no positions to
/// place, no references to wire, nothing a designer would want to touch in the Inspector.
/// Anything with authored state stays in the scene: the camera, the canvas and its HUD
/// text, the grid, the spawner, the score. Those are not created here and never should be.
///
/// The board's cells, the tray's pieces and the effect pools are *not* built here either.
/// They are built by the systems that own them, on demand, because their number and
/// placement depend on the screen and on the run in progress -- an authored 8x8 of cells
/// stops lining up the moment the grid size or the aspect ratio changes.
/// </summary>
[DefaultExecutionOrder(-1000)]
public class GameBootstrap : MonoBehaviour
{
    [Tooltip("Log what was created. Useful when a system is missing and you need to know " +
             "whether the bootstrap skipped it or it failed later.")]
    [SerializeField] private bool logComposition;

    private void Awake()
    {
        // Order is deliberate, not incidental:
        //
        //  1. MobileRuntime   frame rate and physics, before anything renders a frame
        //  2. NeonPostFx      camera post-processing, before the camera is first used
        //  3. BoardLayout     camera size and the board/tray bands
        //  4. BlockDragController  input, once there is something to drag
        //  5. TurnFeedback    shake and haptics, reacting to the game
        //  6. LineClearEffect particle pools for the board
        //  7. PraisePopup     the points-and-word popup
        //  8. GameOverScreen  the end-of-run takeover
        //  9. SettingsPanel   the gear menu, last so it sits above the HUD
        // 10. AudioDirector   sound, after everything it listens to exists
        // 11. TitleIntro     the wordmark, last so it covers everything else
        //
        // Items 4-9 are order-independent in practice; they are listed in the order they
        // matter to the player so the list reads as the game's own structure.
        int created = 0;
        created += Ensure<MobileRuntime>();
        created += Ensure<NeonPostFx>();
        created += Ensure<BoardLayout>();
        created += Ensure<BlockDragController>();
        created += Ensure<TurnFeedback>();
        created += Ensure<LineClearEffect>();
        created += Ensure<PraisePopup>();
        created += Ensure<GameOverScreen>();
        created += Ensure<SettingsPanel>();
        created += Ensure<AudioDirector>();
        created += Ensure<TitleIntro>();

        if (logComposition)
            Debug.Log($"[GameBootstrap] composed the scene, {created} component(s) created.", this);
    }

    /// <summary>
    /// Adds the component unless the scene already has one, so dropping a system in by
    /// hand to expose its settings in the Inspector keeps working and never doubles up.
    /// Returns 1 when it created something, so the caller can report a total.
    /// </summary>
    private int Ensure<T>() where T : Component
    {
        if (FindAnyObjectByType<T>() != null) return 0;

        gameObject.AddComponent<T>();
        return 1;
    }
}
