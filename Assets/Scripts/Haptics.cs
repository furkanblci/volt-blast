using UnityEngine;
using BlockBlast.Core;

/// <summary>
/// Device vibration, as short shaped pulses rather than one blunt buzz.
///
/// Unity's cross-platform <c>Handheld.Vibrate</c> is a single fixed ~500 ms rumble with no
/// intensity control. Fired on every placement that is half a second of identical noise per
/// move: it carries no information, it arrives long after the thing it is meant to punctuate,
/// and it is actively unpleasant. This talks to Android's Vibrator directly instead, where a
/// cue can be 12 ms at a chosen amplitude and land on the same frame as the visual.
///
/// The shapes are deliberately short. A haptic that outlasts its animation stops feeling like
/// part of the event and starts feeling like the phone is broken -- the placement tick is
/// gone before the cell has finished popping, and even the heaviest cue here is under 90 ms.
///
/// API 26+ gets <c>VibrationEffect</c> with real amplitudes and waveforms. Below that (our
/// minimum is 25) only untimed durations exist, so the shapes degrade to their lengths, which
/// is still far closer to the intent than a half-second buzz.
/// </summary>
public static class Haptics
{
    /// <summary>
    /// Whether haptics may fire, read straight from the player's saved preference.
    ///
    /// This used to be a separate bool that only SettingsPanel wrote to, so a player who
    /// switched haptics off and relaunched got them back until they happened to open the
    /// settings panel again. Reading the setting is one fewer copy of the truth.
    /// </summary>
    public static bool Enabled => GameSettings.Haptics;

#if UNITY_ANDROID && !UNITY_EDITOR
    private const float MinimumInterval = 0.045f;
    private static float lastFiredAt = -1f;

    private static AndroidJavaObject vibrator;
    private static bool probed;
    private static bool supportsAmplitude;
    private static int apiLevel;
#endif

    /// <summary>A piece landing: a short, light tick.</summary>
    public static void Light() => OneShot(12, 70);

    /// <summary>A line cleared: firmer, still brief.</summary>
    public static void Medium() => OneShot(20, 140);

    /// <summary>
    /// A multi-line clear or a streak: two knocks rather than one long push, because a
    /// bigger event should read as *more*, and length alone just reads as a longer buzz.
    /// </summary>
    public static void Heavy() => Pattern(new long[] { 0, 16, 34, 26 }, new int[] { 0, 180, 0, 255 });

    /// <summary>The end of a run: one weighted thud.</summary>
    public static void Thud() => OneShot(45, 200);

    // ---------- implementation ----------

    private static void OneShot(long milliseconds, int amplitude)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (!Ready()) return;

        if (supportsAmplitude)
        {
            using (var effect = new AndroidJavaClass("android.os.VibrationEffect"))
            using (var one = effect.CallStatic<AndroidJavaObject>(
                       "createOneShot", milliseconds, Mathf.Clamp(amplitude, 1, 255)))
            {
                vibrator.Call("vibrate", one);
            }
        }
        else
        {
            vibrator.Call("vibrate", milliseconds);
        }
#endif
    }

    private static void Pattern(long[] timings, int[] amplitudes)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (!Ready()) return;

        if (supportsAmplitude)
        {
            using (var effect = new AndroidJavaClass("android.os.VibrationEffect"))
            using (var wave = effect.CallStatic<AndroidJavaObject>(
                       "createWaveform", timings, amplitudes, -1))
            {
                vibrator.Call("vibrate", wave);
            }
        }
        else
        {
            vibrator.Call("vibrate", timings, -1);
        }
#endif
    }

#if UNITY_ANDROID && !UNITY_EDITOR
    /// <summary>
    /// Resolves the Vibrator once and rate-limits. The limit is short enough that a clear
    /// and the placement that caused it can both be felt, and long enough that a combo
    /// chain cannot fuse into one continuous rumble.
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
                using (var version = new AndroidJavaClass("android.os.Build$VERSION"))
                    apiLevel = version.GetStatic<int>("SDK_INT");

                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                    vibrator = activity.Call<AndroidJavaObject>("getSystemService", "vibrator");

                supportsAmplitude = apiLevel >= 26
                                    && vibrator != null
                                    && vibrator.Call<bool>("hasAmplitudeControl");
            }
            catch (System.Exception e)
            {
                // A device without a vibrator, or an OEM that hides the service: silence is
                // the correct outcome, not an exception on the frame a piece lands.
                Debug.LogWarning("[Haptics] unavailable: " + e.Message);
                vibrator = null;
            }
        }

        if (vibrator == null) return false;

        lastFiredAt = Time.unscaledTime;
        return true;
    }
#endif
}
