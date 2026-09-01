using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using System.Collections;
using BlockBlast.Core;

/// <summary>
/// Owns the post-processing that makes the neon actually glow, and the decision about
/// whether this device should pay for it.
///
/// Bloom is the one effect that separates "bright coloured tiles" from "lit tubes", and
/// it is also the only per-frame cost in the game that scales with screen resolution
/// rather than with what is on the board. So it is switched on deliberately, configured
/// cheaply, and dropped on hardware that should not be running a full-screen blur chain.
///
/// The tier test is a heuristic, not a benchmark: it errs toward keeping the effect,
/// because a phone that can run this board at all can almost certainly run a half-res
/// four-iteration bloom. The player can still turn it off.
/// </summary>
[DefaultExecutionOrder(-400)]
public class NeonPostFx : MonoBehaviour
{
    /// <summary>Where the profile lives, so the effect works without scene wiring.</summary>
    public const string ProfileResourcePath = "NeonPostFx";

    [Tooltip("Profile holding the bloom and vignette. Falls back to Resources/NeonPostFx.")]
    [SerializeField] private VolumeProfile profile;

    [Tooltip("Devices with less system memory than this (MB) skip post-processing.")]
    [SerializeField] private int minimumMemoryMB = 2048;

    [Tooltip("Devices with fewer cores than this skip post-processing.")]
    [SerializeField] private int minimumCores = 4;

    [Tooltip("Pull the UI canvas into the camera's stack so post-processing reaches it.")]
    [SerializeField] private bool routeCanvasThroughCamera = true;

    [Tooltip("Where the UI plane sits in front of the camera. Anything past the near clip " +
             "plane works; the board is at z 0 and the camera well behind it.")]
    [SerializeField] private float canvasPlaneDistance = 1f;

    [Tooltip("Sorting order for the UI. Must beat every SpriteRenderer in the scene: a " +
             "camera-space canvas sorts against sprites instead of always winning. Well " +
             "clear of BlockInstance.DragSortingOrder (200), the highest the game uses.")]
    [SerializeField] private int canvasSortingOrder = 1000;

    [Header("Surge")]
    [Tooltip("How long a clear's bloom surge takes to fall back to normal.")]
    [SerializeField] private float surgeDuration = 0.45f;

    private Volume volume;
    private Camera targetCamera;
    private Bloom bloom;
    private float baseIntensity;
    private Coroutine surge;

    /// <summary>Whether the glow is currently being rendered.</summary>
    public bool Enabled { get; private set; }

    private void Awake()
    {
        if (profile == null) profile = Resources.Load<VolumeProfile>(ProfileResourcePath);

        targetCamera = Camera.main;
        if (targetCamera == null || profile == null) return;

        var go = new GameObject("NeonPostFxVolume");
        go.transform.SetParent(transform, false);

        volume = go.AddComponent<Volume>();
        volume.isGlobal = true;
        volume.priority = 0f;
        volume.profile = profile;

        if (profile.TryGet(out bloom)) baseIntensity = bloom.intensity.value;

        if (routeCanvasThroughCamera) RouteCanvas();

        // Both gates must agree: capable hardware and the player's own choice.
        Apply(GameSettings.Glow && ShouldEnable());
    }

    /// <summary>
    /// Moves the UI out of Screen Space - Overlay and into the camera's stack.
    ///
    /// An overlay canvas is composited *after* post-processing, so bloom never touches it.
    /// In a game whose entire look is glow that left the score, the combo readout and the
    /// end screen as the only flat things on screen -- measured, the gold best-score text
    /// went from a hard edge straight to the page background to a halo still reading above
    /// it 96px away.
    ///
    /// Camera shake is unaffected: the canvas is repositioned in front of the camera every
    /// frame, so it tracks the shake and stays put on screen exactly as an overlay did.
    ///
    /// The sorting order is not optional, and it is the one thing this change silently
    /// breaks. An overlay canvas always draws last; a camera-space one takes its place in
    /// the transparent queue like any sprite. At the canvas's default order of 0 the tray
    /// pieces (order 10) punched straight through the end screen's full-bleed backdrop,
    /// and a dragged piece (order 200) went over the settings panel. The order therefore
    /// has to clear every SpriteRenderer the game can produce, not merely the ones that
    /// happened to be on screen when it was last checked.
    ///
    /// The scene authors this too, so the Game view is right in edit mode. It is re-asserted
    /// here because a canvas whose camera reference has gone stale silently falls back to
    /// overlay, and the failure looks like "the glow got worse" rather than like a broken
    /// reference.
    /// </summary>
    private void RouteCanvas()
    {
        foreach (Canvas canvas in FindObjectsByType<Canvas>(FindObjectsSortMode.None))
        {
            // Nested canvases inherit their root's mode; setting them individually would
            // detach them from it.
            if (canvas.transform.parent != null) continue;

            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = targetCamera;
            canvas.planeDistance = canvasPlaneDistance;
            canvas.sortingOrder = canvasSortingOrder;
        }
    }

    /// <summary>
    /// A coarse capability check. Deliberately generous: the cost here is a half-resolution
    /// blur over a scene with a few dozen quads, not a deferred lighting pass.
    /// </summary>
    private bool ShouldEnable()
    {
        if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.OpenGLES3 &&
            SystemInfo.graphicsMemorySize > 0 && SystemInfo.graphicsMemorySize < 512)
            return false;

        if (SystemInfo.systemMemorySize > 0 && SystemInfo.systemMemorySize < minimumMemoryMB) return false;
        if (SystemInfo.processorCount > 0 && SystemInfo.processorCount < minimumCores) return false;

        return true;
    }

    /// <summary>
    /// Briefly drives bloom past its resting value. This is the cheapest juice in the
    /// game -- one float on a volume, no extra draws -- and it is the only effect that
    /// reaches the whole screen, which is what makes a big clear feel bigger than a
    /// small one rather than just wider.
    /// </summary>
    public void Surge(float amount)
    {
        if (!Enabled || bloom == null || amount <= 0f) return;

        if (surge != null) StopCoroutine(surge);
        surge = StartCoroutine(SurgeRoutine(amount));
    }

    private IEnumerator SurgeRoutine(float amount)
    {
        float peak = baseIntensity + amount;
        float elapsed = 0f;

        while (elapsed < surgeDuration)
        {
            elapsed += Time.deltaTime;
            // Snap up, ease down: the rise is the event, the fall is the afterglow.
            bloom.intensity.value = Mathf.LerpUnclamped(
                peak, baseIntensity, Easing.OutCubic(elapsed / surgeDuration));
            yield return null;
        }

        bloom.intensity.value = baseIntensity;
        surge = null;
    }

    /// <summary>Re-reads the preference. Called when the player flips the toggle.</summary>
    public void Refresh() => Apply(GameSettings.Glow && ShouldEnable());

    /// <summary>Turns the glow on or off. Exposed so a settings toggle can drive it.</summary>
    public void Apply(bool enable)
    {
        Enabled = enable;

        if (volume != null) volume.enabled = enable;

        // Leave the profile at rest when switching off, or a surge caught mid-flight
        // would be frozen in as the new baseline next time it is switched on.
        if (!enable && bloom != null) bloom.intensity.value = baseIntensity;

        // The camera flag is what actually costs anything; without it the volume is inert.
        var data = targetCamera != null ? targetCamera.GetComponent<UniversalAdditionalCameraData>() : null;
        if (data != null) data.renderPostProcessing = enable;
    }
}
