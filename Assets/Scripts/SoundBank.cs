using UnityEngine;

/// <summary>
/// Every sound the game makes, in one asset.
///
/// The clips currently in it are generated placeholders with the right *shape* -- length,
/// attack, pitch direction -- and are meant to be replaced. That replacement is the reason
/// this is an asset rather than a set of fields on the director: swapping a sound is
/// dropping a file in and reassigning one slot, with no code touched and nothing to
/// rebuild. A missing clip is silence, not an error, so the game keeps working while the
/// set is half-finished.
///
/// Mixing lives here too. Balance is a property of the sound set, so a louder replacement
/// clip is corrected in the same place it was assigned.
/// </summary>
[CreateAssetMenu(fileName = "SoundBank", menuName = "Block Blast/Sound Bank")]
public class SoundBank : ScriptableObject
{
    /// <summary>Where the bank lives, so audio needs no scene wiring.</summary>
    public const string ResourcesPath = "SoundBank";

    [Header("Placement")]
    [Tooltip("Lifting a piece out of the tray.")]
    [SerializeField] private AudioClip pickup;

    [Tooltip("A piece landing on the board.")]
    [SerializeField] private AudioClip place;

    [Tooltip("A drop the board refused.")]
    [SerializeField] private AudioClip rejected;

    [Header("Clears")]
    [SerializeField] private AudioClip clear;

    [Tooltip("Pitch added per extra line and per combo step. Raising the pitch of one " +
             "clip is what makes a bigger clear sound bigger without needing a second " +
             "recording for every tier.")]
    [SerializeField, Range(0f, 0.3f)] private float clearPitchStep = 0.07f;

    [Tooltip("Ceiling for the above. Past roughly this the clip stops reading as the " +
             "same sound and starts reading as a chirp.")]
    [SerializeField, Range(1f, 2.5f)] private float maxClearPitch = 1.7f;

    [Header("Run")]
    [SerializeField] private AudioClip gameOver;
    [SerializeField] private AudioClip button;

    [Header("Music")]
    [Tooltip("Looped continuously. Must loop seamlessly -- a click at the loop point is " +
             "far more noticeable than anything else in the mix, because it repeats.")]
    [SerializeField] private AudioClip music;

    [Header("Mix")]
    [SerializeField, Range(0f, 1f)] private float sfxVolume = 0.8f;
    [SerializeField, Range(0f, 1f)] private float musicVolume = 0.3f;

    public AudioClip Pickup => pickup;
    public AudioClip Place => place;
    public AudioClip Rejected => rejected;
    public AudioClip Clear => clear;
    public AudioClip GameOver => gameOver;
    public AudioClip Button => button;
    public AudioClip Music => music;

    public float SfxVolume => sfxVolume;
    public float MusicVolume => musicVolume;

    /// <summary>
    /// How high to pitch the clear for a given turn. Lines and combo both count, so a
    /// four-line clear and a long streak escalate through the same ceiling rather than
    /// each having their own scale.
    /// </summary>
    public float ClearPitch(int lineCount, int combo)
    {
        int steps = Mathf.Max(0, lineCount - 1) + Mathf.Max(0, combo - 1);
        return Mathf.Min(maxClearPitch, 1f + steps * clearPitchStep);
    }
}
