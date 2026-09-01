using UnityEngine;

namespace BlockBlast.Core
{
    /// <summary>
    /// Easing curves for the presentation layer.
    ///
    /// Code rather than AnimationCurve assets because these are shared by many small
    /// effects and want to be identical everywhere -- a pop that overshoots slightly
    /// differently per prefab reads as sloppiness. Pure functions, so the shapes can be
    /// asserted in tests instead of eyeballed.
    ///
    /// All take and return normalized time; inputs are clamped so a caller overshooting
    /// its duration cannot produce a wild value.
    /// </summary>
    public static class Easing
    {
        public static float OutQuad(float t)
        {
            t = Mathf.Clamp01(t);
            return 1f - (1f - t) * (1f - t);
        }

        public static float InQuad(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t;
        }

        public static float OutCubic(float t)
        {
            t = Mathf.Clamp01(t);
            float inverted = 1f - t;
            return 1f - inverted * inverted * inverted;
        }

        public static float InOutQuad(float t)
        {
            t = Mathf.Clamp01(t);
            return t < 0.5f ? 2f * t * t : 1f - 2f * (1f - t) * (1f - t);
        }

        /// <summary>
        /// Overshoots past 1 before settling. The workhorse for anything that should feel
        /// like it has weight -- a piece landing, a number ticking up.
        /// </summary>
        public static float OutBack(float t, float overshoot = 1.70158f)
        {
            t = Mathf.Clamp01(t);
            float c3 = overshoot + 1f;
            float p = t - 1f;
            return 1f + c3 * p * p * p + overshoot * p * p;
        }

        /// <summary>Springs past the target and oscillates in. Use sparingly; it reads as loud.</summary>
        public static float OutElastic(float t)
        {
            t = Mathf.Clamp01(t);
            if (t <= 0f) return 0f;
            if (t >= 1f) return 1f;

            const float period = 0.3f;
            return Mathf.Pow(2f, -10f * t) * Mathf.Sin((t * 10f - 0.75f) * (2f * Mathf.PI) / period) + 1f;
        }

        /// <summary>
        /// A 0 -> 1 -> 0 pulse, peaking at <paramref name="peak"/>. For punches that
        /// return to where they started rather than travelling somewhere.
        /// </summary>
        public static float Pulse(float t, float peak = 0.35f)
        {
            t = Mathf.Clamp01(t);
            peak = Mathf.Clamp(peak, 0.001f, 0.999f);

            return t < peak
                ? OutQuad(t / peak)
                : 1f - InOutQuad((t - peak) / (1f - peak));
        }
    }
}
