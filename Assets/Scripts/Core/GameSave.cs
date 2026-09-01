using System;
using System.Text;
using UnityEngine;

namespace BlockBlast.Core
{
    /// <summary>
    /// A whole run, flattened for storage. Board and tray travel together so a resumed
    /// game cannot come back with pieces that no longer suit the board it was saved on.
    ///
    /// Occupancy and colours are hex strings rather than ulong/uint fields because
    /// JsonUtility's support for unsigned 64-bit values is not dependable across
    /// platforms; hex costs a few hundred bytes and always round-trips.
    /// </summary>
    [Serializable]
    public class GameSave
    {
        // v2 added trayColors, because tray colours come from a palette rather than the
        // shape asset and a resumed run otherwise repainted its pieces.
        //
        // v3 is a palette change, not a format change. Cell colours are the key the skin
        // looks sprites up by, so a board saved under the old palette resolves to nothing
        // and renders as flat untextured squares. Rejecting those saves costs one run and
        // is the only way the reskin lands cleanly.
        public const int CurrentVersion = 3;

        public int version = CurrentVersion;

        public int boardWidth;
        public int boardHeight;

        /// <summary>16 hex chars: the occupancy bitboard.</summary>
        public string occupancyHex = string.Empty;

        /// <summary>512 hex chars: 64 packed RGBA32 cells, low index first.</summary>
        public string colorsHex = string.Empty;

        /// <summary>Asset names of the pieces still in the tray, parallel to <see cref="traySlots"/>.</summary>
        public string[] trayShapeIds = Array.Empty<string>();

        /// <summary>Which tray slot each surviving piece occupies.</summary>
        public int[] traySlots = Array.Empty<int>();

        /// <summary>Packed RGBA32 tint of each surviving piece, stored signed so JsonUtility is happy.</summary>
        public int[] trayColors = Array.Empty<int>();

        public int score;
        public int combo;

        public bool HasBoard => !string.IsNullOrEmpty(occupancyHex);

        /// <summary>Tint of the tray piece at <paramref name="index"/>, or white when absent.</summary>
        public uint TrayColorAt(int index) =>
            trayColors != null && (uint)index < (uint)trayColors.Length
                ? unchecked((uint)trayColors[index])
                : 0xFFFFFFFFu;

        // ---------- capture / apply ----------

        public static GameSave Capture(
            BoardState board, int score, int combo, string[] shapeIds, int[] slots, uint[] trayTints = null)
        {
            var packedTrayTints = new int[shapeIds?.Length ?? 0];
            for (int i = 0; i < packedTrayTints.Length; i++)
            {
                packedTrayTints[i] = trayTints != null && i < trayTints.Length
                    ? unchecked((int)trayTints[i])
                    : unchecked((int)0xFFFFFFFFu);
            }

            var boardColors = new uint[BoardState.CellCapacity];
            board.CopyTo(out ulong occupancy, boardColors);

            return new GameSave
            {
                version = CurrentVersion,
                boardWidth = board.Width,
                boardHeight = board.Height,
                occupancyHex = occupancy.ToString("X16"),
                colorsHex = EncodeColors(boardColors),
                trayShapeIds = shapeIds ?? Array.Empty<string>(),
                traySlots = slots ?? Array.Empty<int>(),
                trayColors = packedTrayTints,
                score = score,
                combo = combo
            };
        }

        /// <summary>
        /// Restores the board. Returns false when the save is unusable or was written for
        /// a different board size, in which case the board is left untouched and the
        /// caller should start a fresh run.
        /// </summary>
        public bool TryApplyTo(BoardState board)
        {
            if (board == null || version != CurrentVersion) return false;
            if (boardWidth != board.Width || boardHeight != board.Height) return false;
            if (!ulong.TryParse(occupancyHex, System.Globalization.NumberStyles.HexNumber,
                    System.Globalization.CultureInfo.InvariantCulture, out ulong occupancy))
                return false;

            board.Restore(occupancy, DecodeColors(colorsHex));
            return true;
        }

        // ---------- hex codec ----------

        private static string EncodeColors(uint[] colors)
        {
            var builder = new StringBuilder(BoardState.CellCapacity * 8);
            for (int i = 0; i < BoardState.CellCapacity; i++)
                builder.Append(colors[i].ToString("X8"));

            return builder.ToString();
        }

        private static uint[] DecodeColors(string hex)
        {
            var colors = new uint[BoardState.CellCapacity];
            if (string.IsNullOrEmpty(hex) || hex.Length < BoardState.CellCapacity * 8) return colors;

            for (int i = 0; i < BoardState.CellCapacity; i++)
            {
                if (uint.TryParse(hex.Substring(i * 8, 8), System.Globalization.NumberStyles.HexNumber,
                        System.Globalization.CultureInfo.InvariantCulture, out uint value))
                    colors[i] = value;
            }

            return colors;
        }
    }

    /// <summary>
    /// Reads and writes the saved run. PlayerPrefs rather than a file because it is the
    /// one store that behaves identically in the Editor and on Android, and the payload
    /// is well under a kilobyte.
    ///
    /// High score is kept in its own key so wiping a run never costs the player their best.
    /// </summary>
    public static class SaveSystem
    {
        private const string SaveKey = "BlockBlast.Run.v1";
        private const string HighScoreKey = "BlockBlast.HighScore";

        public static void Write(GameSave save)
        {
            if (save == null) return;
            PlayerPrefs.SetString(SaveKey, JsonUtility.ToJson(save));
            PlayerPrefs.Save();
        }

        /// <summary>Returns null when there is no usable save, so callers can just null-check.</summary>
        public static GameSave Read()
        {
            string json = PlayerPrefs.GetString(SaveKey, string.Empty);
            if (string.IsNullOrEmpty(json)) return null;

            try
            {
                GameSave save = JsonUtility.FromJson<GameSave>(json);
                return save != null && save.version == GameSave.CurrentVersion && save.HasBoard ? save : null;
            }
            catch (Exception e)
            {
                // A corrupt save must never block startup; drop it and start clean.
                Debug.LogWarning($"[SaveSystem] Discarding unreadable save: {e.Message}");
                Clear();
                return null;
            }
        }

        public static void Clear()
        {
            PlayerPrefs.DeleteKey(SaveKey);
            PlayerPrefs.Save();
        }

        public static int ReadHighScore() => PlayerPrefs.GetInt(HighScoreKey, 0);

        public static void WriteHighScore(int value)
        {
            if (value <= ReadHighScore()) return;
            PlayerPrefs.SetInt(HighScoreKey, value);
            PlayerPrefs.Save();
        }
    }
}
