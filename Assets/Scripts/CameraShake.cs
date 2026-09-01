using UnityEngine;

/// <summary>
/// Decaying positional shake, applied as an offset on top of wherever the camera
/// otherwise is.
///
/// Written as an offset rather than by writing absolute positions so it composes with any
/// future camera movement instead of fighting it, and so an interrupted shake always
/// leaves the camera exactly where it started.
/// </summary>
[DefaultExecutionOrder(100)]
public class CameraShake : MonoBehaviour
{
    [Tooltip("How quickly a shake dies away. Higher is snappier.")]
    [SerializeField] private float decay = 6f;

    [Tooltip("Noise frequency. Low reads as a lurch, high as a rattle.")]
    [SerializeField] private float frequency = 26f;

    [Tooltip("Ceiling on displacement in world units, so a huge combo cannot throw the board off screen.")]
    [SerializeField] private float maxOffset = 0.45f;

    /// <summary>
    /// Perlin noise does not use its full 0..1 range: in practice it stays near the middle,
    /// roughly 0.25..0.75. Treating it as 0..1 made every shake about a quarter of its
    /// requested size -- a single-line clear moved the camera 1.8px on a 1080p screen, which
    /// is why it read as no shake at all. This rescales the noise to actually span -1..1 so
    /// the amount asked for is the amount delivered.
    /// </summary>
    private const float NoiseSpanCorrection = 2f;

    private Transform target;
    private Vector3 appliedOffset;
    private float strength;
    private float seed;

    private void Awake()
    {
        target = transform;
        seed = Random.value * 100f;
    }

    /// <summary>Adds to any shake already running, so a double clear hits harder than a single.</summary>
    public void Shake(float amount)
    {
        strength = Mathf.Min(strength + Mathf.Max(0f, amount), maxOffset);
    }

    private void LateUpdate()
    {
        // Always remove last frame's offset first; the camera's own position is whatever
        // is left, so this never accumulates drift.
        target.localPosition -= appliedOffset;
        appliedOffset = Vector3.zero;

        if (strength <= 0.0001f)
        {
            strength = 0f;
            return;
        }

        float t = Time.time * frequency;
        appliedOffset = new Vector3(
            Wobble(seed, t) * strength,
            Wobble(seed + 37f, t) * strength,
            0f);

        target.localPosition += appliedOffset;
        strength = Mathf.MoveTowards(strength, 0f, decay * strength * Time.deltaTime + 0.0005f);
    }

    /// <summary>One axis of noise, in -1..1, clamped so a correction cannot overshoot.</summary>
    private static float Wobble(float axisSeed, float t) =>
        Mathf.Clamp((Mathf.PerlinNoise(axisSeed, t) - 0.5f) * 2f * NoiseSpanCorrection, -1f, 1f);

    private void OnDisable()
    {
        target.localPosition -= appliedOffset;
        appliedOffset = Vector3.zero;
        strength = 0f;
    }
}
