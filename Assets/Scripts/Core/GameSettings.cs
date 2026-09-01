using System;
using UnityEngine;

namespace BlockBlast.Core
{
    /// <summary>
    /// The player's preferences, persisted immediately on change.
    ///
    /// Static rather than a component because these are read from places that have no
    /// business finding a settings object first -- the haptics wrapper, and later the
    /// audio mixer -- and because they must survive a scene reload, which a component
    /// would not.
    ///
    /// Written through on every change rather than batched at quit: mobile can kill the
    /// process straight from the background, and a preference that silently fails to
    /// stick is worse than one that was never offered.
    /// </summary>
    public static class GameSettings
    {
        private const string SoundKey = "BlockBlast.Settings.Sound";
        private const string MusicKey = "BlockBlast.Settings.Music";
        private const string HapticsKey = "BlockBlast.Settings.Haptics";
        private const string GlowKey = "BlockBlast.Settings.Glow";

        /// <summary>Raised whenever any preference changes, so UI and systems can follow.</summary>
        public static event Action Changed;

        private static bool loaded;
        private static bool sound = true;
        private static bool music = true;
        private static bool haptics = true;
        private static bool glow = true;

        public static bool Sound
        {
            get { Load(); return sound; }
            set { Load(); if (sound == value) return; sound = value; Write(SoundKey, value); }
        }

        public static bool Music
        {
            get { Load(); return music; }
            set { Load(); if (music == value) return; music = value; Write(MusicKey, value); }
        }

        public static bool Haptics
        {
            get { Load(); return haptics; }
            set { Load(); if (haptics == value) return; haptics = value; Write(HapticsKey, value); }
        }

        /// <summary>
        /// Post-processed glow. The one setting here with a real frame cost, so it is the
        /// one a player on weak hardware needs to be able to switch off themselves --
        /// the automatic device check is a heuristic and will get some phones wrong.
        /// </summary>
        public static bool Glow
        {
            get { Load(); return glow; }
            set { Load(); if (glow == value) return; glow = value; Write(GlowKey, value); }
        }

        private static void Load()
        {
            if (loaded) return;
            loaded = true;

            // Everything defaults to on: a player who has never opened settings should get
            // the full experience, not a muted one.
            sound = PlayerPrefs.GetInt(SoundKey, 1) != 0;
            music = PlayerPrefs.GetInt(MusicKey, 1) != 0;
            haptics = PlayerPrefs.GetInt(HapticsKey, 1) != 0;
            glow = PlayerPrefs.GetInt(GlowKey, 1) != 0;
        }

        private static void Write(string key, bool value)
        {
            PlayerPrefs.SetInt(key, value ? 1 : 0);
            PlayerPrefs.Save();
            Changed?.Invoke();
        }

        /// <summary>Drops cached values so a test or an external write is picked up.</summary>
        public static void Reload()
        {
            loaded = false;
            Load();
            Changed?.Invoke();
        }
    }
}
