using UnityEngine;

/// <summary>
/// Runtime settings a phone build needs but the Editor does not enforce.
///
/// Unity's defaults are aimed at desktop: an uncapped frame rate that burns battery for
/// frames nobody sees, a physics world stepped every frame by a game that has none, and a
/// screen that dims mid-puzzle because the player has not touched it for thirty seconds.
///
/// Applied from code rather than left to Project Settings so the reasoning stays next to
/// the values, and so they hold in the Editor too -- profiling a build that is configured
/// differently from what you were testing is how performance work goes wrong.
/// </summary>
[DefaultExecutionOrder(-500)]
public class MobileRuntime : MonoBehaviour
{
    [Tooltip("Frames per second to aim for. 60 on any device that can hold it.")]
    [SerializeField, Range(30, 120)] private int targetFrameRate = 60;

    [Tooltip("Keep the display awake while the game is open.")]
    [SerializeField] private bool preventScreenSleep = true;

    [Tooltip("Stop stepping the 2D physics world. Nothing in this game uses it, and the " +
             "step runs every frame regardless.")]
    [SerializeField] private bool disablePhysics = true;

    private void Awake()
    {
        // vSync overrides targetFrameRate wherever it is honoured, so it has to go first
        // or the frame cap is silently ignored.
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = targetFrameRate;

        if (preventScreenSleep) Screen.sleepTimeout = SleepTimeout.NeverSleep;

        if (disablePhysics)
        {
            Physics2D.simulationMode = SimulationMode2D.Script;
            Physics.simulationMode = SimulationMode.Script;
        }
    }

    private void OnDestroy()
    {
        // Leave the Editor as we found it, or these leak into other scenes and other
        // projects opened in the same session.
        if (preventScreenSleep) Screen.sleepTimeout = SleepTimeout.SystemSetting;

        if (disablePhysics)
        {
            Physics2D.simulationMode = SimulationMode2D.FixedUpdate;
            Physics.simulationMode = SimulationMode.FixedUpdate;
        }
    }
}
