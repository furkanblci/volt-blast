using UnityEngine;
using BlockBlast.Core;

/// <summary>
/// Device vibration, as short shaped pulses where the platform allows and a plain buzz
/// where it does not.
///
/// Unity's cross-platform <c>Handheld.Vibrate</c> is a single fixed ~500 ms rumble with no
/// intensity control: fired on every placement that is half a second of identical noise per
/// move, arriving long after the thing it punctuates. Android's own Vibrator can do 12 ms at
/// a chosen amplitude on the same frame as the visual, so that is the path this prefers.
///
/// It is only a preference. The shaped path reaches into Java by name, and a device or an
/// OEM build can refuse any step of that -- which is exactly what happened: a build shipped
/// where nothing vibrated at all, because a failure anywhere in the chain fell through to
/// silence. There are three steps now, each a real degradation rather than a cliff:
///
///   1. <c>VibrationEffect</c> -- chosen length and force, the intended feel
///   2. <c>Vibrator.vibrate(ms)</c> -- the right length, whatever force the motor gives
///   3. <c>Handheld.Vibrate</c> -- a fixed half-second, only if there is no Vibrator at all
///
/// Step 2 matters more than it looks: without it, a phone with a vibrator but no amplitude
/// control jumped straight to half a second per placement.
///
/// Keeping a real call to <c>Handheld.Vibrate</c> in the code has a second effect worth
/// knowing: it is how Unity decides to put <c>android.permission.VIBRATE</c> in the manifest.
/// </summary>
public static class Haptics
{
    /// <summary>
    /// Whether haptics may fire, read straight from the player's saved preference.
    ///
    /// This used to be a separate bool that only SettingsPanel wrote to, so a player who
    /// switched haptics off and relaunched got them back until they happened to open the
    /// settings panel again. Reading the setting is one fewer copy of the truth -- but note
    /// it also means a preference switched off long ago is now honoured from launch.
    /// </summary>
    public static bool Enabled => GameSettings.Haptics;

#if UNITY_ANDROID && !UNITY_EDITOR
    private const float MinimumInterval = 0.045f;
    private static float lastFiredAt = -1f;

    private static AndroidJavaObject vibrator;
    private static bool probed;
    private static bool shaped;        // the device accepts VibrationEffect
#endif

    // Amplitudes are about a third down from where they started, durations untouched.
    // Softening by shortening would have flattened the shapes into each other -- the two
    // knocks of a streak only read as two while there is time between them -- so the force
    // came down and the rhythm stayed.

    /// <summary>A piece landing: a short, light tick.</summary>
    public static void Light() => OneShot(12, 48);

    /// <summary>A line cleared: firmer, still brief.</summary>
    public static void Medium() => OneShot(20, 95);

    /// <summary>
    /// A multi-line clear or a streak: two knocks rather than one long push, because a
    /// bigger event should read as *more*, and length alone just reads as a longer buzz.
    /// </summary>
    public static void Heavy() => Pattern(new long[] { 0, 16, 34, 26 }, new int[] { 0, 125, 0, 175 });

    /// <summary>The end of a run: one weighted thud.</summary>
    public static void Thud() => OneShot(45, 140);

    /// <summary>
    /// A cue the player asked for, used when the vibrate toggle is switched on. Without it
    /// there is no way to tell a working device from a broken code path from a preference
    /// that was off all along -- which is the position this got into once already.
    /// </summary>
    public static void Test() => OneShot(25, 110);

    // ---------- implementation ----------

    private static void OneShot(long milliseconds, int amplitude)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (!Ready()) return;

        if (shaped)
        {
            try
            {
                using (var effect = new AndroidJavaClass("android.os.VibrationEffect"))
                using (var one = effect.CallStatic<AndroidJavaObject>(
                           "createOneShot", milliseconds, Mathf.Clamp(amplitude, 1, 255)))
                {
                    vibrator.Call("vibrate", one);
                }
                return;
            }
            catch (System.Exception e)
            {
                // Whatever refused, stop trying: one failure per launch is a diagnostic,
                // one per placement is a stall.
                Debug.LogWarning("[Haptics] shaped vibration failed, falling back: " + e.Message);
                shaped = false;
            }
        }

        Timed(milliseconds);
#endif
    }

    private static void Pattern(long[] timings, int[] amplitudes)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (!Ready()) return;

        if (shaped)
        {
            try
            {
                using (var effect = new AndroidJavaClass("android.os.VibrationEffect"))
                using (var wave = effect.CallStatic<AndroidJavaObject>(
                           "createWaveform", timings, amplitudes, -1))
                {
                    vibrator.Call("vibrate", wave);
                }
                return;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[Haptics] waveform failed, falling back: " + e.Message);
                shaped = false;
            }
        }

        // Sum the pattern rather than replaying it: without amplitude control the knocks
        // would fuse anyway, so one buzz of the same total length is the honest reduction.
        long total = 0;
        foreach (long t in timings) total += t;
        Timed(total);
#endif
    }

#if UNITY_ANDROID && !UNITY_EDITOR
    /// <summary>
    /// A buzz of a chosen length, for devices with a vibrator but no amplitude control.
    ///
    /// This step was missing: without it every such device dropped straight to
    /// <c>Handheld.Vibrate</c>, which is a fixed half-second regardless of what was asked
    /// for. A 12 ms buzz is not a shaped tick, but it is the difference between a tap and
    /// the phone going off in your hand -- and it is the only way "softer" means anything
    /// on hardware that cannot vary its force.
    /// </summary>
    private static void Timed(long milliseconds)
    {
        if (vibrator != null)
        {
            try
            {
                // System.Math, not Mathf: Mathf has no long overload, so two longs bind
                // to Max(float, float) and come back a float. That compiles, and then
                // looks for Java's vibrate(float) -- which does not exist -- and throws on
                // the device. The type has to survive all the way to the JNI call.
                vibrator.Call("vibrate", System.Math.Max(1L, milliseconds));
                return;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[Haptics] timed vibration failed, falling back: " + e.Message);
                vibrator = null;
            }
        }

        Fallback();
    }

    /// <summary>
    /// The blunt path, reached only when there is no usable Vibrator at all. Also the
    /// reason Unity adds the VIBRATE permission to the manifest, so this call earns its
    /// place even on devices that never execute it.
    /// </summary>
    private static void Fallback() => Handheld.Vibrate();

    /// <summary>
    /// Rate-limits, and resolves the Vibrator once. The limit is short enough that a clear
    /// and the placement that caused it can both be felt, and long enough that a combo
    /// chain cannot fuse into one continuous rumble.
    ///
    /// Returns true whenever vibration is allowed at all, whether or not the shaped path
    /// is available -- the fallback needs no Java object.
    /// </summary>
    private static bool Ready()
    {
        if (!Enabled) return false;
        if (Time.unscaledTime - lastFiredAt < MinimumInterval) return false;

        if (!probed)
        {
            probed = true;
            try
            {
                int apiLevel;
                using (var version = new AndroidJavaClass("android.os.Build$VERSION"))
                    apiLevel = version.GetStatic<int>("SDK_INT");

                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                    vibrator = activity.Call<AndroidJavaObject>("getSystemService", "vibrator");

                shaped = apiLevel >= 26
                         && vibrator != null
                         && vibrator.Call<bool>("hasVibrator")
                         && vibrator.Call<bool>("hasAmplitudeControl");

                Debug.Log("[Haptics] api " + apiLevel + ", shaped vibration " + (shaped ? "on" : "off"));
            }
            catch (System.Exception e)
            {
                // A device without a vibrator, or an OEM that hides the service. Silence is
                // not the right answer here -- Handheld.Vibrate still works on plenty of
                // phones where reaching the service by name does not.
                Debug.LogWarning("[Haptics] Vibrator unavailable, using Handheld.Vibrate: " + e.Message);
                vibrator = null;
                shaped = false;
            }
        }

        lastFiredAt = Time.unscaledTime;
        return true;
    }
#endif
}
